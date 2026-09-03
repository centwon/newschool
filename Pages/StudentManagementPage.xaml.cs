using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Collections;
using NewSchool.Controls;
using NewSchool.Dialogs;
using NewSchool.Helpers;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;

namespace NewSchool.Pages;

/// <summary>
/// 학생 관리 페이지 (WinUI3)
///
/// <para>이 페이지는 <b>목록만</b> 맡는다. 고치는 일은 행을 눌러 여는
/// <see cref="Dialogs.StudentEditDialog"/> 가 한다 — 예전에는 행마다 TextBox 를 깔고
/// 상단 저장 버튼으로 한꺼번에 커밋했는데, 저장하지 않은 편집이 화면에 쌓이는 구조라
/// 다른 반을 조회하는 순간 통째로 사라졌다.</para>
///
/// 주요 기능:
/// 1. 학년도/학년/반별 학생 목록 조회
/// 2. 행 클릭 → 편집 다이얼로그(학적 상태·전입·전출 포함)
/// 3. 학생 한 명 추가(다이얼로그) / 엑셀 일괄 추가(AddStudentsPage)
/// 4. 선택한 학생들 일괄 삭제
/// 5. 전체 선택/해제 기능
/// </summary>
public sealed partial class StudentManagementPage : Page, IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _enrollmentService?.Dispose();
        GC.SuppressFinalize(this);
    }

    private readonly EnrollmentService _enrollmentService;
    /// <summary>
    /// 학생 목록 (최적화됨)
    /// ⚡ OptimizedObservableCollection로 UI 업데이트 80% 향상
    /// </summary>
    public OptimizedObservableCollection<StudentManagementViewModel> Students { get; } = new();

    public StudentManagementPage()
    {
        this.InitializeComponent();

        // ⭐ SchoolDatabase.DbPath 사용 (전체 경로)
        _enrollmentService = new EnrollmentService();

        this.Loaded += StudentManagementPage_Loaded;
        this.Unloaded += (_, _) => _enrollmentService?.Dispose();
    }

    private void StudentManagementPage_Loaded(object sender, RoutedEventArgs e)
    {
        ShowCurrentSchool();
        CheckDatabaseInitialization();
    }

    /// <summary>
    /// 어느 학교의 명부인지 필터 줄에 밝힌다.
    ///
    /// <para>조회는 <c>Settings.SchoolCode</c> 하나로만 걸리는데(학교는 Student 가 아니라
    /// Enrollment.SchoolCode 에 붙어 있다), 화면에는 그 사실이 어디에도 없어서 목록이
    /// 비어 있을 때 "학생이 없는 것" 인지 "학교가 안 잡힌 것" 인지 구분할 수 없었다.</para>
    /// </summary>
    private void ShowCurrentSchool()
    {
        string name = Settings.SchoolName?.Value ?? string.Empty;
        string code = Settings.SchoolCode?.Value ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(name))
        {
            TxtSchoolName.Text = name;
        }
        else if (!string.IsNullOrWhiteSpace(code))
        {
            // 이름을 못 받아온 경우 — 코드만이라도 보여 준다.
            TxtSchoolName.Text = $"학교 코드 {code}";
        }
        else
        {
            TxtSchoolName.Text = "학교가 설정되지 않았습니다";
        }
    }

    /// <summary>
    /// 데이터베이스 초기화 확인
    /// </summary>
    private async void CheckDatabaseInitialization()
    {
        try
        {
            // DB 파일 존재 확인
            if (!SchoolDatabase.DatabaseExists())
            {
                System.Diagnostics.Debug.WriteLine("[StudentManagement] DB 파일이 없습니다. 초기화 시작...");
                await SchoolDatabase.InitAsync();
            }

            // Enrollment 테이블 존재 확인
            // ⭐ SchoolDatabase.DbPath 사용
            using var repo = new EnrollmentRepository(SchoolDatabase.DbPath);
            try
            {
                // 간단한 쿼리로 테이블 존재 확인
                await repo.GetCountAsync(Settings.SchoolCode.Value, Settings.WorkYear.Value);
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("no such table"))
            {
                System.Diagnostics.Debug.WriteLine("[StudentManagement] Enrollment 테이블이 없습니다. 재초기화...");

                // CREATE TABLE IF NOT EXISTS 를 다시 걸어 빠진 테이블을 채운다.
                await SchoolDatabase.InitAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentManagement] DB 초기화 확인 오류: {ex.Message}");
            await MessageBox.ShowAsync($"데이터베이스 초기화 오류\n{ex.Message}", "오류");
        }
    }

    #region 초기화

    private async void YearSemPicker_YearSemesterChanged(object sender, YearSemesterChangedEventArgs e)
    {
        await ClassFilter.LoadAsync(e.Year, e.Semester);
    }

    #endregion

    #region 이벤트 핸들러

    /// <summary>
    /// 조회 버튼 클릭
    /// </summary>
    private async void OnLookUpClick(object sender, RoutedEventArgs e)
    {
        await LoadStudentsAsync();
    }

    /// <summary>
    /// 삭제 버튼 클릭
    /// </summary>
    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        await DeleteStudentsAsync();
    }

    /// <summary>
    /// 학생 한 명 추가 — 다이얼로그로 받는다.
    /// 필터에 골라 둔 학년도·학년·반을 기본값으로 넘겨, 같은 반을 이어 넣을 때 덜 치게 한다.
    /// </summary>
    private async void OnAddOneClick(object sender, RoutedEventArgs e)
    {
        var dialog = new StudentEditDialog(
            YearSemPicker.Year, ClassFilter.Grade, ClassFilter.ClassNum)
        {
            XamlRoot = this.XamlRoot
        };

        // ⚠ dialog.ShowAsync() 를 직접 부르지 말 것. 이 앱의 대화상자는 모두
        // MessageBox.ShowDialogAsync 의 세마포어(_dialogGate)를 거친다. 직접 부르면
        // 다른 대화상자와 겹칠 때 예외가 나고, async void 라 그 예외가 조용히 사라져
        // "눌러도 아무 반응이 없다" 로 나타난다.
        await MessageBox.ShowDialogAsync(dialog);
        if (!dialog.Saved) return;

        // 추가한 학생이 지금 필터에 걸리는지는 알 수 없으므로(다른 반으로 넣었을 수 있다)
        // 목록을 통째로 다시 읽는다.
        await LoadStudentsAsync();
    }

    /// <summary>
    /// 엑셀로 일괄 추가 — 여러 명은 지금처럼 전용 페이지에서 받는다.
    /// </summary>
    private void OnAddBulkClick(object sender, RoutedEventArgs e)
    {
        if (this.Frame != null)
        {
            this.Frame.Navigate(typeof(AddStudentsPage));
        }
    }

    /// <summary>
    /// 위쪽 "수정" 버튼 — 체크한 학생 한 명을 연다.
    /// (행을 눌러도 같은 다이얼로그가 열리지만, 버튼으로도 갈 수 있어야 한다.)
    /// </summary>
    private async void OnEditSelectedClick(object sender, RoutedEventArgs e)
    {
        var selected = Students.Where(s => s.IsSelected).ToList();

        if (selected.Count == 0)
        {
            await MessageBox.ShowAsync("수정할 학생을 선택하세요.\n(행을 눌러 바로 열 수도 있습니다)", "알림");
            return;
        }

        if (selected.Count > 1)
        {
            await MessageBox.ShowAsync("한 번에 한 명만 수정할 수 있습니다.", "알림");
            return;
        }

        await OpenEditDialogAsync(selected[0]);
    }

    /// <summary>
    /// 행 클릭 — 학생 정보 편집 다이얼로그를 연다.
    /// (위쪽 "수정" 버튼도 같은 <see cref="OpenEditDialogAsync"/> 로 들어온다.)
    /// </summary>
    private async void OnStudentRowClick(object sender, RoutedEventArgs e)
    {
        // DataContext 가 아니라 Tag 를 본다 — ItemsRepeater 는 실체화한 요소에
        // DataContext 를 채워 주지 않는다(템플릿의 XAML 주석 참고).
        if ((sender as FrameworkElement)?.Tag is not StudentManagementViewModel vm) return;

        await OpenEditDialogAsync(vm);
    }

    /// <summary>
    /// 편집 다이얼로그를 열고, 저장됐으면 목록에 반영한다.
    /// </summary>
    private async Task OpenEditDialogAsync(StudentManagementViewModel vm)
    {
        var dialog = new StudentEditDialog(vm.EnrollmentNo, vm.StudentID)
        {
            XamlRoot = this.XamlRoot
        };

        // 불러오기는 띄우기 전에 끝낸다 — 실패한 다이얼로그를 빈 채로 띄우지 않는다.
        string? loadFailure = await dialog.LoadAsync();
        if (loadFailure != null)
        {
            await MessageBox.ShowAsync(loadFailure, "오류");
            return;
        }

        // 위 OnAddOneClick 의 주석 참고 — 반드시 이 경로로 띄운다.
        await MessageBox.ShowDialogAsync(dialog);
        if (!dialog.Saved) return;

        // 학년도·학년·반을 옮겼다면 지금 필터에서 빠져야 하므로 다시 읽는다.
        // 그대로면 그 행만 제자리에서 갱신한다(스크롤 위치와 선택이 살아 있다).
        await RefreshRowAsync(vm);

        // 전출일 뒤에 이미 남은 기록이 있으면 알린다. 다이얼로그가 닫힌 뒤라야
        // 띄울 수 있어서(대화상자를 겹칠 수 없다) 여기서 처리한다.
        if (dialog.LeavingNotice != null)
            await MessageBox.ShowAsync(dialog.LeavingNotice, "알림");
    }

    /// <summary>
    /// 편집한 행 하나를 DB 에서 다시 읽어 반영한다.
    /// 학급이 바뀌어 현재 필터에서 벗어났으면 목록 전체를 다시 읽는다.
    /// </summary>
    private async Task RefreshRowAsync(StudentManagementViewModel vm)
    {
        try
        {
            using var repo = new EnrollmentRepository(SchoolDatabase.DbPath);
            var updated = await repo.GetByIdAsync(vm.EnrollmentNo);

            if (updated == null)
            {
                await LoadStudentsAsync();
                return;
            }

            int grade = ClassFilter.Grade;      // 0 = 전체
            int classNo = ClassFilter.ClassNum; // 0 = 전체

            bool stillInFilter = updated.Year == YearSemPicker.Year
                && (grade == 0 || updated.Grade == grade)
                && (classNo == 0 || updated.Class == classNo);

            if (!stillInFilter)
            {
                await LoadStudentsAsync();
                return;
            }

            vm.Year = updated.Year;
            vm.Grade = updated.Grade;
            vm.Class = updated.Class;
            vm.Number = updated.Number;
            vm.Name = updated.Name;
            vm.ChangeType = updated.ChangeType;
            vm.ChangeDate = FormatChangeDate(updated.ChangeDate);
            vm.Memo = updated.Memo;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentManagement] 행 갱신 실패: {ex.Message}");
            await LoadStudentsAsync();
        }
    }

    /// <summary>
    /// 전체 선택/해제 체크박스 클릭
    /// </summary>
    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        // Indeterminate(null) 상태에서 클릭하면 전체 선택
        bool isChecked = ChkSelectAll.IsChecked != false;
        ChkSelectAll.IsChecked = isChecked;

        foreach (var student in Students)
        {
            student.IsSelected = isChecked;
        }
        
        // ItemsRepeater 강제 새로고침
        StudentList.ItemsSource = null;
        StudentList.ItemsSource = Students;
    }

    /// <summary>
    /// 개별 학생 체크박스 클릭
    /// </summary>
    private void OnStudentCheckBoxClick(object sender, RoutedEventArgs e)
    {
        // Tag 로 항목을 받는다. 예전에는 DataContext 를 봤는데 ItemsRepeater 에서는 늘
        // null 이라 이 핸들러가 통째로 헛돌았고, 그래서 개별 체크를 해도 위쪽 전체 선택
        // 체크박스가 중간 상태로 바뀌지 않았다(체크 자체는 x:Bind TwoWay 가 해 준다).
        if ((sender as FrameworkElement)?.Tag is not StudentManagementViewModel vm) return;

        vm.IsSelected = (sender as CheckBox)?.IsChecked == true;
        UpdateSelectAllCheckBoxState();
    }

    /// <summary>
    /// 전체 선택 체크박스 상태 업데이트
    /// </summary>
    private void UpdateSelectAllCheckBoxState()
    {
        if (Students.Count == 0)
        {
            ChkSelectAll.IsChecked = false;
            return;
        }

        int selectedCount = Students.Count(s => s.IsSelected);

        if (selectedCount == 0)
        {
            ChkSelectAll.IsChecked = false;
        }
        else if (selectedCount == Students.Count)
        {
            ChkSelectAll.IsChecked = true;
        }
        else
        {
            ChkSelectAll.IsChecked = null; // Indeterminate
        }
    }

    #endregion

    #region 데이터 로드

    /// <summary>
    /// 학생 목록 로드
    /// </summary>
    private async Task LoadStudentsAsync()
    {
        try
        {
            Students.Clear();
            ChkSelectAll.IsChecked = false;

            // 필터에서 값 가져오기 (학년도·학기 + 학년·반)
            int year = YearSemPicker.Year;
            int grade = ClassFilter.Grade;  // 0 = 전체
            int classNo = ClassFilter.ClassNum; // 0 = 전체

            if (year == 0)
            {
                await MessageBox.ShowAsync("학년도를 선택하세요.", "알림");
                return;
            }
            
            // 전체·학년·학급을 한 경로로 조회한다(0 = 전체). 명부는 학년 단위라 학기는 안 건다.
            //
            // 예전에는 세 갈래였고 셋의 기준이 서로 달랐다:
            //  · 전체·학년 조회는 학기를 Settings.WorkSemester 로 고정해, 1학기에 등록한 학생이
            //    2학기에는 한 명도 안 나왔고,
            //  · 학급 조회는 학기를 무시했으며,
            //  · 전체·학년 조회는 Memo 를 빈 값으로 채워 넣어서, 그 상태로 저장하면
            //    Enrollment.Memo 가 통째로 지워졌다(저장이 vm.Memo 를 그대로 덮어쓴다).
            // includeNotOnRoll: true — 전출·졸업·자퇴한 학생까지 본다.
            // 앱에서 유일하게 그러는 화면이다. 다른 곳(명렬표·좌석·수업·동아리)은
            // 기본값대로 재적만 받는다.
            var enrollments = await _enrollmentService.GetEnrollmentsAsync(
                Settings.SchoolCode.Value, year, grade, classNo, includeNotOnRoll: true);

            var students = enrollments.Select(e => new StudentManagementViewModel
            {
                EnrollmentNo = e.No,
                StudentID = e.StudentID,
                Year = e.Year,
                Grade = e.Grade,
                Class = e.Class,
                Number = e.Number,
                Name = e.Name,
                ChangeType = e.ChangeType,
                ChangeDate = FormatChangeDate(e.ChangeDate),
                Memo = e.Memo,
                IsSelected = false
            }).ToList();

            // ViewModel로 변환
            foreach (var student in students.OrderBy(s => s.Grade).ThenBy(s => s.Class).ThenBy(s => s.Number))
            {
                Students.Add(student);
            }

            // UI 업데이트
            UpdateUI();
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"학생 목록 로드 중 오류가 발생했습니다.\n{ex.Message}", "오류");
            System.Diagnostics.Debug.WriteLine($"[StudentManagement] 로드 오류: {ex.Message}");
        }
        // ItemsRepeater에 ItemsSource 수동 설정
        StudentList.ItemsSource = Students;

        System.Diagnostics.Debug.WriteLine($"[StudentManagement] ItemsRepeater ItemsSource 설정 완료");

    }

    #endregion

    #region 저장 및 삭제

    /// <summary>
    /// 선택된 학생 삭제
    /// </summary>
    /// <summary>
    /// 고른 것 중 <b>그 학생의 마지막 학적</b>인 것들의 이름.
    ///
    /// <para>학적을 빼도 <c>Student</c> 행은 남는다(그게 맞다 — 다른 학년도 기록을 지키기
    /// 위해서다). 다만 그것이 <b>마지막</b> 학적이면 그 학생은 어느 학년도 명부에도 나타나지
    /// 않게 된다. 되돌릴 방법이 화면에 없으므로 빼기 전에 알린다.</para>
    ///
    /// <para>조회에 실패하면 <b>알리지 않고 넘어간다</b> — 이 경고는 거들 뿐이라,
    /// 이것 때문에 빼기 자체가 막히면 안 된다.</para>
    /// </summary>
    private static async Task<List<string>> FindLastEnrollmentsAsync(
        EnrollmentRepository repo, List<StudentManagementViewModel> selected)
    {
        var names = new List<string>();

        // 같은 학생을 여러 줄 고를 수 있다(학년도가 다르면 다른 학적이다) — 한 번만 센다.
        foreach (var studentId in selected.Select(s => s.StudentID).Distinct())
        {
            try
            {
                var history = await repo.GetHistoryByStudentIdAsync(studentId);

                // 이번에 빼는 것을 뺀 나머지가 없으면 마지막이다.
                var removing = selected.Where(s => s.StudentID == studentId)
                                       .Select(s => s.EnrollmentNo)
                                       .ToHashSet();

                if (history.All(h => removing.Contains(h.No)))
                {
                    var name = selected.First(s => s.StudentID == studentId).Name;
                    names.Add(string.IsNullOrWhiteSpace(name) ? studentId : name);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[StudentManagement] 학적 이력 확인 실패({studentId}): {ex.Message}");
            }
        }

        return names;
    }

    private async Task DeleteStudentsAsync()
    {
        try
        {
            var selectedStudents = Students.Where(s => s.IsSelected).ToList();

            if (selectedStudents.Count == 0)
            {
                await MessageBox.ShowAsync("명부에서 뺄 학생을 선택하세요.", "알림");
                return;
            }

            // ⭐ SchoolDatabase.DbPath 사용
            using var enrollmentRepo = new EnrollmentRepository(SchoolDatabase.DbPath);

            // ⚠ 이 버튼이 지우는 것은 <b>그 학년도의 학적</b> 하나뿐이다.
            //   Student·StudentDetail·누가기록·학생부·사진·첨부는 그대로 남는다.
            //   그게 맞다 — 다른 학년도 학적이 있는 학생의 Student 행을 지우면 그 학년도
            //   기록까지 CASCADE 로 날아간다. 틀린 것은 말이었다: 예전 문구는
            //   "학생을 삭제하시겠습니까? 이 작업은 되돌릴 수 없습니다" 였고,
            //   끝나면 "N명의 학생이 삭제되었습니다" 라고 했다.
            var lastOnes = await FindLastEnrollmentsAsync(enrollmentRepo, selectedStudents);

            string message =
                $"선택한 {selectedStudents.Count}명을 {YearSemPicker.Year}학년도 명부에서 뺍니다.\n" +
                "학생 정보와 기록(누가기록·학생부·사진)은 지워지지 않습니다.";

            if (lastOnes.Count > 0)
            {
                message +=
                    $"\n\n다만 다음 {lastOnes.Count}명은 이것이 마지막 학적이라, 빼고 나면\n" +
                    "어느 학년도 명부에도 나타나지 않습니다.\n" +
                    string.Join(", ", lastOnes);
            }

            var confirmed = await MessageBox.ShowConfirmAsync(
                message, "명부에서 빼기", "빼기", "취소");
            if (!confirmed) return;

            int successCount = 0;

            foreach (var vm in selectedStudents)
            {
                try
                {
                    // Enrollment 삭제 — 0행이면 목록에서도 지우지 않는다
                    // (지우면 화면에서만 사라지고 새로고침 시 되살아나 DB 와 어긋난다)
                    if (!await enrollmentRepo.DeleteAsync(vm.EnrollmentNo))
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[StudentManagement] 삭제 0행 - {vm.Name}(EnrollmentNo={vm.EnrollmentNo})");
                        continue;
                    }

                    // Student 는 지우지 않는다. 다른 학년도 학적이 있는 학생의 Student
                    // 행을 지우면 그 학년도 기록까지 CASCADE 로 날아가기 때문이다.
                    // (예전에는 주석 처리된 호출 한 줄만 남아 있어, 읽는 사람이
                    //  "하려다 만 것" 인지 "안 하기로 한 것" 인지 알 수 없었다.)

                    Students.Remove(vm);
                    successCount++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[StudentManagement] 삭제 실패 - {vm.Name}: {ex.Message}");
                }
            }

            if (successCount == selectedStudents.Count)
            {
                await MessageBox.ShowAsync($"{successCount}명을 명부에서 뺐습니다.", "완료");
            }
            else
            {
                await MessageBox.ShowAsync(
                    $"{selectedStudents.Count}명 중 {successCount}명만 뺐습니다.", "일부 실패");
            }
            UpdateUI();
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"명부에서 빼는 중 오류가 발생했습니다.\n{ex.Message}", "오류");
        }
    }

    #endregion

    #region UI 업데이트

    /// <summary>
    /// UI 상태 업데이트
    /// </summary>
    private void UpdateUI()
    {
        bool hasStudents = Students.Count > 0;

        EmptyState.Visibility = hasStudents ? Visibility.Collapsed : Visibility.Visible;
        StudentListContainer.Visibility = hasStudents ? Visibility.Visible : Visibility.Collapsed;

        // 전출·졸업·자퇴·퇴학은 명부에 남아 있어도 지금 이 학교 학생이 아니다.
        // 그 수가 있을 때만 따로 밝힌다 — 늘 붙여 두면 대부분의 반에서 군더더기다.
        int onRoll = Students.Count(s => s.IsActive);

        TxtStudentCount.Text = onRoll == Students.Count
            ? $"총 {Students.Count}명"
            : $"총 {Students.Count}명 (재적 {onRoll} · 전출 등 {Students.Count - onRoll})";
    }

    /// <summary>
    /// 모델의 변동 일자를 목록에 찍을 글자로. 날짜가 없으면 <b>빈 칸</b>이다 —
    /// "1-1-0001" 같은 기본값을 찍으면 날짜를 넣은 것처럼 보인다.
    /// </summary>
    private static string FormatChangeDate(DateTime? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    #endregion
}
#region ViewModel

/// <summary>
/// 학생 관리 ViewModel
/// </summary>
public class StudentManagementViewModel : NotifyPropertyChangedBase
{
    private int _enrollmentNo;
    private string _studentId = string.Empty;
    private int _year;
    private int _grade;
    private int _class;
    private int _number;
    private string _name = string.Empty;
    private string _changeType = EnrollmentChange.Admitted;
    private string _changeDate = string.Empty;
    private string _memo = string.Empty;
    private bool _isSelected;

    public int EnrollmentNo
    {
        get => _enrollmentNo;
        set => SetProperty(ref _enrollmentNo, value);
    }

    public string StudentID
    {
        get => _studentId;
        set => SetProperty(ref _studentId, value);
    }

    public int Year
    {
        get => _year;
        set => SetProperty(ref _year, value);
    }

    public int Grade
    {
        get => _grade;
        set => SetProperty(ref _grade, value);
    }

    public int Class
    {
        get => _class;
        set => SetProperty(ref _class, value);
    }

    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>학적 변동 — 입학·진급·전입·전출·졸업·휴학·유예·정원외·자퇴·퇴학</summary>
    public string ChangeType
    {
        get => _changeType;
        set
        {
            if (SetProperty(ref _changeType, value))
            {
                // 파생 값도 함께 알린다 — 안 하면 다이얼로그에서 전출로 바꿔도
                // 행이 흐려지지 않는다.
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(RowOpacity));
            }
        }
    }

    /// <summary>
    /// 변동 일자 (yyyy-MM-dd). <b>표시용이라 문자열이 맞다</b> — 목록에 글자로 찍을 뿐이다.
    /// 모델(<c>Enrollment.ChangeDate</c>)은 <c>DateTime?</c> 이고, 이 값은
    /// <c>FormatChangeDate</c> 로 만든다(날짜가 없으면 빈 칸).
    /// </summary>
    public string ChangeDate
    {
        get => _changeDate;
        set => SetProperty(ref _changeDate, value);
    }

    /// <summary>지금 명단에 들어가는가(입학·진급·전입).</summary>
    public bool IsActive => EnrollmentChange.IsActive(ChangeType);

    /// <summary>명단에서 빠진 학생은 흐리게 — 목록에서 한눈에 갈라 보이게 한다.</summary>
    public double RowOpacity => IsActive ? 1.0 : 0.5;

    public string Memo
    {
        get => _memo;
        set => SetProperty(ref _memo, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    // IsModified 는 인라인 편집이 있던 시절의 것이다. 편집이 다이얼로그로 옮겨가면서
    // "저장하지 않은 변경" 이라는 상태 자체가 없어져 함께 지웠다.

    public string ClassInfo => $"{Year}학년도 {Grade}학년 {Class}반 {Number}번";
}
#endregion
