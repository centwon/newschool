using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Services;
using NewSchool.ViewModels;

namespace NewSchool.Pages;

/// <summary>
/// StudentSpecPage - 학생부 특기사항 관리 페이지
/// </summary>
public sealed partial class StudentSpecPage : Page, IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _specialService?.Dispose();
        _enrollservice?.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Fields

    private LogCategory _selectedCategory = LogCategory.전체;

    private readonly StudentSpecialService _specialService = new();
    private readonly EnrollmentService _enrollservice = new();

    #endregion

    #region Constructor

    public StudentSpecPage()
    {
        this.InitializeComponent();
        InitializeFilters();
        Loaded += OnPageLoaded;
        // Unloaded 는 XAML 의 Page_Unloaded 에서 처리 (중복 등록 제거)
    }

    /// <summary>
    /// 학생이 한 명도 없으면 빈 화면 대신 다음 할 일을 띄운다.
    /// </summary>
    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        EmptyState.Visibility = await Helpers.SetupProgress.HasAnyStudentAsync()
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    /// <summary>
    /// 안내판의 [학생 추가하기] — 학생 관리 화면으로 보낸다.
    /// </summary>
    private void EmptyState_ActionInvoked(object sender, EventArgs e)
    {
        MainWindow.NavigateFromPage(this.Frame, typeof(StudentManagementPage), "Settings_Student");
    }

    #endregion

    #region Initialization

    private void InitializeFilters()
    {
        var categories = new List<LogCategory>
        {
            LogCategory.전체,
            LogCategory.자율활동,
            LogCategory.진로활동,
            LogCategory.동아리활동,
            LogCategory.봉사활동,
            LogCategory.교과활동,
            LogCategory.개인별세특,
            LogCategory.종합의견
        };
        CBoxCategory.ItemsSource = categories;
        CBoxCategory.SelectedIndex = 0;
    }

    #endregion

    #region Event Handlers - Filters

    private async void YearSemPicker_YearSemesterChanged(object sender, YearSemesterChangedEventArgs e)
    {
        await ClassFilter.LoadAsync(e.Year, e.Semester);
    }

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxCategory.SelectedItem is LogCategory category)
        {
            _selectedCategory = category;
            SpecListViewer.Category = category;
            // 예전에는 여기서 UpdateUIByCategory() 를 불렀는데 그 메서드는 몸통이 비어 있었다
            // — 영역별 화면 조정은 SpecListViewer.Category 가 전부 한다. 함께 지웠다(44차).
        }
    }

    #endregion

    #region Event Handlers - Buttons

    private async void OnQueryClick(object sender, RoutedEventArgs e)
    {
        await LoadSpecsAsync();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var selectedSpecs = SpecListViewer.SelectedSpecs.ToList();

        if (!selectedSpecs.Any())
        {
            await MessageBox.ShowAsync("저장할 항목이 없습니다", "알림");
            return;
        }

        try
        {
            // 학교를 떠난 학생에게 그 뒤 날짜로 기록을 남기려는 것이면 먼저 알린다.
            // 막지는 않는다 — 전출일을 뒤늦게 넣은 경우 이미 적어 둔 기록을 못 고치게 된다.
            var notices = new List<string>();
            var asked = new HashSet<string>();
            foreach (var spec in selectedSpecs)
            {
                var sid = spec.Special.StudentID;
                if (string.IsNullOrWhiteSpace(sid) || !asked.Add(sid)) continue;
                if (!DateTime.TryParse(spec.Special.Date, out var recordDate)) continue;

                var notice = await EnrollmentGuard.DescribeRecordAfterLeavingAsync(
                    sid, spec.Special.Year, recordDate);
                if (notice != null) notices.Add(notice);
            }

            if (notices.Count > 0 &&
                !await MessageBox.ShowConfirmAsync(
                    string.Join("\n\n", notices), "학적 확인", "계속", "취소"))
            {
                return;
            }

            var confirmed = await MessageBox.ShowConfirmAsync(
                $"{selectedSpecs.Count}개 항목을 저장하시겠습니까?",
                "저장 확인", "저장", "취소");

            if (confirmed)
            {
                // 한 트랜잭션으로 저장한다 — 하나라도 실패하면 전부 되돌린다.
                // 예전에는 건별로 돌면서 반영된 것만 세었는데, 도중에 예외가 나면
                // 앞부분만 DB 에 남고 사용자는 몇 건이 들어갔는지 알 수 없었다.
                // 같은 표를 저장하는 교과 세특 화면(CourseSpecPage)은 처음부터
                // 이 경로를 썼다 — 두 화면이 서로 다르게 동작할 이유가 없다.
                await _specialService.SaveManyAsync(selectedSpecs.Select(s => s.Special));

                foreach (var spec in selectedSpecs)
                    spec.MarkAsSaved();

                await MessageBox.ShowAsync("저장되었습니다", "완료");
            }
        }
        catch (Exception ex)
        {
            // 롤백됐으므로 한 건도 저장되지 않았다. 변경 표시를 그대로 두어
            // 사용자가 고쳐 다시 누를 수 있게 한다.
            await MessageBox.ShowAsync(
                $"저장하지 못했습니다. 한 건도 저장되지 않았습니다.\n{ex.Message}", "저장 실패");
        }
    }

    /// <summary>
    /// 삭제 — DB에서 실제 삭제
    /// </summary>
    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var selectedSpecs = SpecListViewer.SelectedSpecs.ToList();

        if (!selectedSpecs.Any())
        {
            await MessageBox.ShowAsync("삭제할 항목이 없습니다", "알림");
            return;
        }

        // DB에 저장된 항목만 카운트
        var savedSpecs = selectedSpecs.Where(s => s.Special.No > 0).ToList();
        var unsavedSpecs = selectedSpecs.Where(s => s.Special.No == 0).ToList();

        string msg = savedSpecs.Count > 0
            ? $"{savedSpecs.Count}개 항목을 DB에서 삭제하시겠습니까?\n(복구할 수 없습니다)"
            : $"{unsavedSpecs.Count}개 미저장 항목을 목록에서 제거하시겠습니까?";

        try
        {
            var confirmed = await MessageBox.ShowConfirmAsync(msg, "삭제 확인", "삭제", "취소");

            if (confirmed)
            {
                int deletedCount = 0;
                foreach (var spec in savedSpecs)
                {
                    // 실제로 지워진 것만 센다 — 예전에는 결과를 버리고 무조건 세어
                    // 한 건도 안 지워져도 "N개 삭제되었습니다"라고 알렸다.
                    if (await _specialService.DeleteAsync(spec.Special.No))
                        deletedCount++;
                }

                // 새로고침
                await LoadSpecsAsync();

                if (deletedCount == savedSpecs.Count)
                    await MessageBox.ShowAsync($"{deletedCount + unsavedSpecs.Count}개 항목이 삭제되었습니다", "완료");
                else
                    await MessageBox.ShowAsync(
                        $"{savedSpecs.Count}개 중 {deletedCount}개만 삭제됐습니다.", "일부 삭제 실패");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"삭제 중 오류가 발생했습니다: {ex.Message}", "오류");
        }
    }

    /// <summary>
    /// 일괄 입력 — BatchDialog 열기
    /// </summary>
    private void OnBatchInputClick(object sender, RoutedEventArgs e)
    {
        int year = YearSemPicker.Year;
        int grade = ClassFilter.Grade;
        int classNo = ClassFilter.ClassNum;

        if (year == 0 || grade == 0 || classNo == 0)
        {
            _ = MessageBox.ShowAsync("학년도, 학년, 반을 모두 선택해주세요", "알림");
            return;
        }

        string? defaultType = _selectedCategory != LogCategory.전체
            ? _selectedCategory.ToString()
            : null;

        var dialog = new Dialogs.StudentSpecBatchDialog(year, 0, grade, classNo, defaultType);
        // 닫힐 때 목록을 다시 읽는다 — 일괄 입력 창은 DB 를 직접 고치는데, 여기가 모르면
        // 화면에는 열기 전의 내용이 그대로 남고 그 상태로 [저장] 을 누르는 순간
        // 방금 일괄로 넣은 내용을 옛 내용으로 덮어쓴다.
        // (누가기록 쪽 다이얼로그들과 같은 규칙 — named method 로 구독해 자기 자신을 해제한다.)
        dialog.Closed += OnBatchDialogClosedReload;
        dialog.Activate();
    }

    private async void OnBatchDialogClosedReload(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
    {
        if (sender is Window w) w.Closed -= OnBatchDialogClosedReload;

        // 필터가 온전할 때만 다시 읽는다. LoadSpecsAsync 는 비어 있으면 안내 대화상자를
        // 띄우는데, 창을 닫았을 뿐인 사용자에게 그것이 튀어나오면 뜬금없다.
        if (YearSemPicker.Year != 0 && ClassFilter.Grade != 0 && ClassFilter.ClassNum != 0)
            await LoadSpecsAsync();
    }

    /// <summary>
    /// 일괄 출력 — 필터 다이얼로그 → PDF/엑셀
    /// </summary>
    private async void OnBatchExportClick(object sender, RoutedEventArgs e)
    {
        int year = YearSemPicker.Year;
        int grade = ClassFilter.Grade;
        int classNo = ClassFilter.ClassNum;

        if (year == 0 || grade == 0 || classNo == 0)
        {
            await MessageBox.ShowAsync("학년도, 학년, 반을 모두 선택해주세요", "알림");
            return;
        }

        var filterDialog = new Dialogs.SpecExportFilterDialog { XamlRoot = this.XamlRoot };
        var result = await MessageBox.ShowDialogAsync(filterDialog);
        if (result != ContentDialogResult.Primary) return;

        var filterType = filterDialog.SelectedType;
        var statusFilter = filterDialog.StatusFilter;
        bool excludeEmpty = filterDialog.ExcludeEmpty;
        bool isPdf = filterDialog.IsPdf;

        try
        {
            string schoolCode = Settings.SchoolCode.Value;

            using var enrollmentService = new EnrollmentService();
            var enrollments = await enrollmentService.GetClassRosterAsync(schoolCode, year, grade, classNo);

            if (enrollments.Count == 0)
            {
                await MessageBox.ShowAsync("해당 학급에 학생이 없습니다.", "알림");
                return;
            }

            using var specService = new StudentSpecialService();
            var studentSpecsList = new List<(int Number, string Name, List<StudentSpecial> Specs)>();
            int totalSpecs = 0;

            // 학급 전체를 IN 쿼리로 한 번에 읽는다 — 예전에는 학생 수만큼 쿼리했다.
            // (같은 화면의 LoadSpecsAsync 는 이미 이 방식이었다.)
            var specsByStudent = await specService.GetByStudentIdsAsync(
                enrollments.Select(e2 => e2.StudentID), year);

            foreach (var enrollment in enrollments.OrderBy(e2 => e2.Number))
            {
                specsByStudent.TryGetValue(enrollment.StudentID, out var found);
                var specs = found ?? new List<StudentSpecial>();

                // 영역 필터 (예전에는 학생별 조회 두 갈래로 갈렸는데, 한쪽도 결국
                //  전부 읽어 메모리에서 걸렀으므로 여기서 한 번만 거른다)
                if (!string.IsNullOrEmpty(filterType))
                    specs = specs.Where(s => s.Type == filterType).ToList();

                // 상태 필터
                if (statusFilter == "draft")
                    specs = specs.Where(s => !s.IsFinalized).ToList();
                else if (statusFilter == "finalized")
                    specs = specs.Where(s => s.IsFinalized).ToList();

                // 빈 항목 제외
                if (excludeEmpty)
                    specs = specs.Where(s => !string.IsNullOrWhiteSpace(s.Content)).ToList();

                if (specs.Count == 0) continue;

                studentSpecsList.Add((enrollment.Number, enrollment.Name, specs));
                totalSpecs += specs.Count;
            }

            if (studentSpecsList.Count == 0)
            {
                await MessageBox.ShowAsync("조건에 맞는 기록이 없습니다.", "알림");
                return;
            }

            string filePath;
            if (isPdf)
            {
                var printService = new StudentSpecPrintService();
                filePath = printService.GenerateClassSpecPdf(year, grade, classNo, studentSpecsList);
            }
            else
            {
                var exportService = new StudentSpecExportService();
                filePath = exportService.ExportClassSpecsToExcel(year, grade, classNo, studentSpecsList);
            }

            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });

            await MessageBox.ShowAsync(
                $"{studentSpecsList.Count}명, 총 {totalSpecs}건의 특기사항을 출력했습니다.\n저장 위치: {filePath}",
                "일괄 출력 완료");
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"일괄 출력 중 오류가 발생했습니다: {ex.Message}", "오류");
        }
    }

    private void OnFontSizeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            FlyoutBase.ShowAttachedFlyout(button);
        }
    }

    private void OnFontSizeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (SpecListViewer != null)
        {
            SpecListViewer.ContentFontSize = e.NewValue;
            TxtFontSize.Text = $"{e.NewValue:F0}";
        }
    }

    #endregion

    #region Data Loading

    private async Task LoadSpecsAsync()
    {
        try
        {
            int selectedYear = YearSemPicker.Year;
            int selectedGrade = ClassFilter.Grade;
            int selectedClass = ClassFilter.ClassNum;

            if (selectedYear == 0 || selectedGrade == 0 || selectedClass == 0)
            {
                await MessageBox.ShowAsync("학년도, 학년, 반을 모두 선택해주세요", "알림");
                return;
            }

            var students = await _enrollservice.GetClassRosterAsync(Settings.SchoolCode, selectedYear, selectedGrade, selectedClass);

            if (!students.Any())
            {
                await MessageBox.ShowAsync("학생이 없습니다", "알림");
                SpecListViewer.LoadSpecs(new List<StudentSpecial>());
                return;
            }

            var studentInfoLookup = students.ToDictionary(
                s => s.StudentID,
                s => (Grade: s.Grade, ClassNum: s.Class, Number: s.Number, Name: s.Name)
            );

            // 학급 전체 기록을 IN 쿼리로 일괄 조회 후 카테고리 필터 (학생 수만큼 쿼리하던 N+1 제거)
            var specsByStudent = await _specialService.GetByStudentIdsAsync(
                students.Select(s => s.StudentID), selectedYear);
            string? typeFilter = _selectedCategory == LogCategory.전체
                ? null
                : _selectedCategory.ToString();

            var allSpecs = new List<StudentSpecial>();

            foreach (var student in students)
            {
                specsByStudent.TryGetValue(student.StudentID, out var specs);
                var list = specs ?? new List<StudentSpecial>();
                if (typeFilter != null)
                    list = list.Where(s => s.Type == typeFilter).ToList();

                if (list.Count > 0)
                {
                    allSpecs.AddRange(list);
                }
                else if (IsAutoCreateCategory(_selectedCategory))
                {
                    allSpecs.Add(CreateEmptySpec(student.StudentID));
                }
            }

            SpecListViewer.LoadSpecs(allSpecs, studentInfoLookup);
            SpecListViewer.StudentInfoMode = StudentInfoMode.NumName;
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"데이터 로드 중 오류: {ex.Message}", "오류");
        }
    }

    #endregion

    #region Helper Methods

    private bool IsAutoCreateCategory(LogCategory category)
    {
        return category switch
        {
            LogCategory.자율활동 => true,
            LogCategory.진로활동 => true,
            LogCategory.종합의견 => true,
            LogCategory.개인별세특 => true,
            _ => false
        };
    }

    private StudentSpecial CreateEmptySpec(string studentId)
    {
        return new StudentSpecial
        {
            No = 0,
            StudentID = studentId,
            Year = YearSemPicker.Year,
            Semester = Helpers.NeisHelper.IsSemesterScoped(_selectedCategory.ToString())
                ? YearSemPicker.Semester : 0,
            Type = _selectedCategory.ToString(),
            Title = string.Empty,
            Content = string.Empty,
            Date = DateTime.Now.ToString("yyyy-MM-dd"),
            TeacherID = Settings.User.Value,
            CourseNo = 0,
            SubjectName = string.Empty,
            IsFinalized = false,
            Tag = string.Empty
        };
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        Dispose();
    }

    #endregion
}
