using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;

namespace NewSchool.Dialogs;

/// <summary>
/// 학생 한 명의 학적을 보고 고치는 다이얼로그.
///
/// <para>학생 관리 페이지가 목록 전용이 되면서, 예전의 인라인 편집(행마다 TextBox + 상단
/// 저장 버튼)을 대신한다. 저장은 이 다이얼로그가 직접 끝낸다 — 확인을 누르면 그 학생만
/// 커밋되므로, 저장하지 않은 편집이 화면에 쌓여 있다가 통째로 날아가는 일이 없다.</para>
///
/// <para>학적 상태와 전입·전출 칸은 <see cref="Enrollment"/> 에 이미 있던 것이다.
/// 그동안 값을 넣을 화면이 없어 늘 비어 있었다.</para>
/// </summary>
public sealed partial class StudentEditDialog : ContentDialog
{
    private readonly bool _isEdit;
    private readonly int _enrollmentNo;
    private readonly string _studentId = string.Empty;

    private Enrollment? _enrollment;
    private InfoBar? _errorInfoBar;

    /// <summary>저장이 실제로 이루어졌는지. 호출한 쪽이 목록을 새로 읽을지 판단한다.</summary>
    public bool Saved { get; private set; }

    /// <summary>
    /// 새 학생 추가. 필터에서 고른 학년도·학년·반을 기본값으로 채워 둔다.
    /// </summary>
    public StudentEditDialog(int year, int grade, int cls)
    {
        this.InitializeComponent();

        _isEdit = false;
        Title = "학생 추가";

        InitializeErrorInfoBar();

        NumYear.Value = year > 0 ? year : DateTime.Now.Year;
        NumGrade.Value = grade > 0 ? grade : 1;
        NumClass.Value = cls > 0 ? cls : 1;
        NumNumber.Value = double.NaN;

        SelectByTag(CBoxStatus, EnrollmentStatus.Enrolled);
    }

    /// <summary>
    /// 기존 학생 수정. 실제 값 채우기는 <see cref="LoadAsync"/> 에서 한다.
    /// </summary>
    public StudentEditDialog(int enrollmentNo, string studentId)
    {
        this.InitializeComponent();

        _isEdit = true;
        _enrollmentNo = enrollmentNo;
        _studentId = studentId ?? string.Empty;
        Title = "학생 정보 수정";

        InitializeErrorInfoBar();
    }

    /// <summary>
    /// DB 에서 값을 읽어 채운다. 수정 모드에서 <c>ShowAsync</c> 전에 한 번 호출한다.
    /// </summary>
    /// <returns>불러오지 못하면 사유, 성공하면 null.</returns>
    public async Task<string?> LoadAsync()
    {
        if (!_isEdit) return null;

        try
        {
            using var repo = new EnrollmentRepository(SchoolDatabase.DbPath);
            _enrollment = await repo.GetByIdAsync(_enrollmentNo);

            if (_enrollment == null)
                return "학적을 찾을 수 없습니다. 목록을 새로 읽어 주세요.";

            TxtStudentId.Text = $"학생 ID  {_enrollment.StudentID}";
            TxtStudentId.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

            TxtName.Text = _enrollment.Name ?? string.Empty;
            SelectByTag(CBoxSex, _enrollment.Sex);

            NumYear.Value = _enrollment.Year;
            NumGrade.Value = _enrollment.Grade;
            NumClass.Value = _enrollment.Class;
            NumNumber.Value = _enrollment.Number;

            // 목록에 없는 값(빈 값, 옛 "전학")이면 아무것도 안 잡힌다. 그대로 저장하면
            // '재학' 으로 채워지므로, 그런 행은 한 번 열었다 저장하는 것으로 정리된다.
            SelectByTag(CBoxStatus, _enrollment.Status);

            DateTransferIn.Date = ParseDate(_enrollment.TransferInDate);
            TxtTransferInSchool.Text = _enrollment.TransferInSchool ?? string.Empty;
            DateTransferOut.Date = ParseDate(_enrollment.TransferOutDate);
            TxtTransferOutSchool.Text = _enrollment.TransferOutSchool ?? string.Empty;

            TxtMemo.Text = _enrollment.Memo ?? string.Empty;

            // 전입·전출에 값이 있으면 접힌 채로 두지 않는다 — 있는 줄 모르고 지나칠 자리다.
            ExpTransfer.IsExpanded =
                DateTransferIn.Date != null || DateTransferOut.Date != null ||
                !string.IsNullOrWhiteSpace(_enrollment.TransferInSchool) ||
                !string.IsNullOrWhiteSpace(_enrollment.TransferOutSchool);

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentEditDialog] 불러오기 실패: {ex}");
            return $"학생 정보를 불러오지 못했습니다.\n{ex.Message}";
        }
    }

    #region 저장

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();

        try
        {
            HideError();

            string? invalid = Validate();
            if (invalid != null)
            {
                ShowError(invalid);
                args.Cancel = true;
                return;
            }

            string? failure = _isEdit ? await SaveEditAsync() : await SaveNewAsync();
            if (failure != null)
            {
                ShowError(failure);
                args.Cancel = true;
                return;
            }

            Saved = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentEditDialog] 저장 오류: {ex}");
            ShowError($"저장 중 오류가 발생했습니다.\n{ex.Message}");
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <returns>문제가 있으면 사유, 없으면 null.</returns>
    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
            return "이름을 입력해 주세요.";

        // 성별은 Student 저장 단계에서 '남'/'여' 만 통과하므로 여기서 먼저 막는다.
        if (TagOf(CBoxSex) is not ("남" or "여"))
            return "성별을 선택해 주세요.";

        if (double.IsNaN(NumYear.Value) || NumYear.Value <= 0) return "학년도를 입력해 주세요.";
        if (double.IsNaN(NumGrade.Value) || NumGrade.Value <= 0) return "학년을 입력해 주세요.";
        if (double.IsNaN(NumClass.Value) || NumClass.Value <= 0) return "반을 입력해 주세요.";
        if (double.IsNaN(NumNumber.Value) || NumNumber.Value <= 0) return "번호를 입력해 주세요.";

        return null;
    }

    private async Task<string?> SaveEditAsync()
    {
        if (_enrollment == null) return "학적을 찾을 수 없습니다.";

        string name = TxtName.Text.Trim();
        string sex = TagOf(CBoxSex) ?? string.Empty;

        int year = (int)NumYear.Value;
        int grade = (int)NumGrade.Value;
        int cls = (int)NumClass.Value;
        int number = (int)NumNumber.Value;

        // 자리(학년도-학년-반-번호)를 옮겼다면 그 자리가 비어 있는지 본다.
        // (학년,반,번호) UNIQUE 제약이 없어 이 검사가 유일한 방어선이다.
        bool seatMoved = year != _enrollment.Year || grade != _enrollment.Grade
                      || cls != _enrollment.Class || number != _enrollment.Number;
        if (seatMoved && await StudentCreationService.IsSeatTakenAsync(year, grade, cls, number))
            return $"{year}학년도 {grade}학년 {cls}반 {number}번은 이미 다른 학생이 쓰고 있습니다.";

        // 1) 정본(Student) 먼저. 이름·성별이 바뀌면 리포지토리가 Enrollment 쪽까지 동기화한다.
        //    결과를 확인한다 — 버리면 정본은 옛 이름인데 학적만 새 이름이 되어 화면마다 달라진다.
        if (_enrollment.Name != name || _enrollment.Sex != sex)
        {
            using var studentService = new StudentService(SchoolDatabase.DbPath);
            var student = await studentService.GetBasicInfoAsync(_enrollment.StudentID);
            if (student != null)
            {
                student.Name = name;
                student.Sex = sex;
                if (!await studentService.UpdateBasicInfoAsync(student))
                    return "이름·성별 갱신이 반영되지 않았습니다.";
            }
        }

        // 2) 학적(Enrollment). 위에서 동기화된 이름·성별을 여기서도 같은 값으로 덮어써야
        //    한다 — 우리가 들고 있는 _enrollment 는 불러온 시점의 옛 값이기 때문이다.
        _enrollment.Name = name;
        _enrollment.Sex = sex;
        _enrollment.Year = year;
        _enrollment.Grade = grade;
        _enrollment.Class = cls;
        _enrollment.Number = number;
        _enrollment.Status = TagOf(CBoxStatus) ?? EnrollmentStatus.Enrolled;
        _enrollment.TransferInDate = FormatDate(DateTransferIn.Date);
        _enrollment.TransferInSchool = TxtTransferInSchool.Text.Trim();
        _enrollment.TransferOutDate = FormatDate(DateTransferOut.Date);
        _enrollment.TransferOutSchool = TxtTransferOutSchool.Text.Trim();
        _enrollment.Memo = TxtMemo.Text.Trim();
        _enrollment.UpdatedAt = DateTime.Now;

        using var repo = new EnrollmentRepository(SchoolDatabase.DbPath);
        if (!await repo.UpdateAsync(_enrollment))
            return "학적이 저장되지 않았습니다.";

        return null;
    }

    private async Task<string?> SaveNewAsync()
    {
        string name = TxtName.Text.Trim();
        string sex = TagOf(CBoxSex) ?? string.Empty;

        int year = (int)NumYear.Value;
        int grade = (int)NumGrade.Value;
        int cls = (int)NumClass.Value;
        int number = (int)NumNumber.Value;

        if (await StudentCreationService.IsSeatTakenAsync(year, grade, cls, number))
            return $"{year}학년도 {grade}학년 {cls}반 {number}번은 이미 다른 학생이 쓰고 있습니다.";

        string studentId = await StudentCreationService.GenerateUniqueStudentIdAsync(year);
        if (string.IsNullOrEmpty(studentId))
            return "학생 ID 를 만들지 못했습니다. 잠시 후 다시 시도해 주세요.";

        string? failure = await StudentCreationService.CreateAsync(
            studentId, name, sex, year, grade, cls, number);
        if (failure != null) return failure;

        // 만들기는 '재학' 으로 고정이라, 상태나 전입·전출·비고를 손댔으면 한 번 더 반영한다.
        await ApplyExtrasToNewAsync(studentId);
        return null;
    }

    /// <summary>
    /// 갓 만든 학적에 상태·전입·전출·비고를 반영한다.
    ///
    /// <para>학생은 이미 저장됐으므로 여기서 실패해도 추가 자체는 되돌리지 않는다 —
    /// 되돌리면 "저장에 실패했다" 는 말과 달리 학생이 사라져 더 혼란스럽다.
    /// 못 담은 값은 곧바로 다시 열어 고칠 수 있다.</para>
    /// </summary>
    private async Task ApplyExtrasToNewAsync(string studentId)
    {
        string status = TagOf(CBoxStatus) ?? EnrollmentStatus.Enrolled;
        string transferInDate = FormatDate(DateTransferIn.Date);
        string transferInSchool = TxtTransferInSchool.Text.Trim();
        string transferOutDate = FormatDate(DateTransferOut.Date);
        string transferOutSchool = TxtTransferOutSchool.Text.Trim();
        string memo = TxtMemo.Text.Trim();

        bool nothingExtra = status == EnrollmentStatus.Enrolled
            && transferInDate.Length == 0 && transferInSchool.Length == 0
            && transferOutDate.Length == 0 && transferOutSchool.Length == 0
            && memo.Length == 0;
        if (nothingExtra) return;

        try
        {
            using var repo = new EnrollmentRepository(SchoolDatabase.DbPath);
            var created = await repo.GetCurrentByStudentIdAsync(studentId);
            if (created == null) return;

            created.Status = status;
            created.TransferInDate = transferInDate;
            created.TransferInSchool = transferInSchool;
            created.TransferOutDate = transferOutDate;
            created.TransferOutSchool = transferOutSchool;
            created.Memo = memo;
            created.UpdatedAt = DateTime.Now;

            await repo.UpdateAsync(created);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentEditDialog] 추가 항목 반영 실패: {ex.Message}");
        }
    }

    #endregion

    #region UI 보조

    private void OnStatusSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 전입·전출을 고르면 그 칸을 펼쳐 준다 — 고르고도 날짜를 안 남기면
        // 나중에 언제 어느 학교로 옮겼는지 알 길이 없다.
        if (ExpTransfer == null) return;

        if (TagOf(CBoxStatus) is EnrollmentStatus.TransferredIn or EnrollmentStatus.TransferredOut)
            ExpTransfer.IsExpanded = true;
    }

    private void OnClearTransferClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        DateTransferIn.Date = null;
        DateTransferOut.Date = null;
        TxtTransferInSchool.Text = string.Empty;
        TxtTransferOutSchool.Text = string.Empty;
    }

    private static void SelectByTag(ComboBox box, string? tag)
    {
        if (string.IsNullOrEmpty(tag)) { box.SelectedItem = null; return; }

        box.SelectedItem = box.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => (i.Tag as string) == tag);
    }

    private static string? TagOf(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string;

    /// <summary>"yyyy-MM-dd" → 날짜. 비었거나 형식이 다르면 null.</summary>
    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                      DateTimeStyles.None, out var parsed)
            ? new DateTimeOffset(parsed)
            : null;
    }

    /// <summary>날짜 → "yyyy-MM-dd". 비었으면 빈 문자열(모델이 문자열로 들고 있다).</summary>
    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    private void InitializeErrorInfoBar()
    {
        _errorInfoBar = new InfoBar
        {
            Severity = InfoBarSeverity.Error,
            IsOpen = false,
            IsClosable = false
        };
    }

    private void ShowError(string message)
    {
        if (_errorInfoBar == null || ErrorContainer == null) return;

        _errorInfoBar.Message = message;
        _errorInfoBar.IsOpen = true;

        if (!ErrorContainer.Children.Contains(_errorInfoBar))
            ErrorContainer.Children.Insert(0, _errorInfoBar);
    }

    private void HideError()
    {
        _errorInfoBar?.IsOpen = false;
    }

    #endregion
}
