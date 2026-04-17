using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Services;
using NewSchool.ViewModels;
using NewSchool.Repositories;
using NewSchool.Dialogs;

namespace NewSchool.Pages;

/// <summary>
/// 학생 활동 기록(로그) 관리 페이지
/// WPF PageLog를 WinUI3로 전환
/// Enrollment 모델 직접 사용 (StudentListItemViewModel 제거)
/// </summary>
public sealed partial class PageStudentLog : Page
{
    #region Fields

    private int _year;
    private int _semester;
    private int _grade;
    private int _classroom;
    private LogCategory _category;
    private Enrollment? _selectedStudent = null;

    private readonly StudentLogService _logService;

    #endregion

    #region Properties

    public LogCategory Category
    {
        get => _category;
        set
        {
            if (value != _category)
            {
                _category = value;
                if (LogList != null)
                {
                    LogList.Category = value;
                }
            }
        }
    }

    #endregion

    #region Constructor

    public PageStudentLog()
    {
        this.InitializeComponent();

        _logService = new StudentLogService();

        InitializeControls();
    }

    #endregion

    #region Initialization

    private void InitializeControls()
    {
        // 카테고리 목록 설정
        var logCategories = Enum.GetValues<LogCategory>().Cast<LogCategory>().ToList();
        CBoxCat.ItemsSource = logCategories;
        CBoxCat.SelectedIndex = 0;

        // StudentList 이벤트 연결
        if (StudentList != null)
        {
            StudentList.StudentSelected += OnStudentSelected;
        }

        SetupStudentContextMenu();

        // SchoolFilterPicker가 자동으로 초기화함
    }

    #endregion

    #region Event Handlers - FilterPicker

    /// <summary>
    /// SchoolFilterPicker 선택 변경
    /// </summary>
    private async void FilterPicker_SelectionChanged(object? sender, SchoolFilterChangedEventArgs e)
    {
        _year = e.Year;
        _semester = e.Semester;
        _grade = e.Grade;
        _classroom = e.Class;

        if (_year > 0 && _grade > 0 && _classroom > 0)
        {
            await LoadStudentsAsync();
        }
    }

    /// <summary>
    /// 학생 목록 로드
    /// </summary>
    private async Task LoadStudentsAsync()
    {
        try
        {
            using var service = new EnrollmentService();
            var enrollments = await service.GetEnrollmentsAsync(Settings.SchoolCode, _year, _semester, _grade, _classroom);
            
            StudentList.LoadStudents(enrollments);

            _selectedStudent = null;
            LogList?.Logs?.Clear();
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"학생 목록 로드 중 오류가 발생했습니다: {ex.Message}", "오류");
        }
    }

    #endregion

    private async void CBoxCat_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxCat.SelectedItem == null) return;

        await CheckUnSavedAsync();
        Category = (LogCategory)CBoxCat.SelectedItem;

        await LoadLogsAsync();
        UpdateSpecBoxVisibility();
    }


    #region Event Handlers - Student Selection

    private async void OnStudentSelected(object? sender, Enrollment student)
    {
        await CheckUnSavedAsync();
        _selectedStudent = student;
        await LoadLogsAsync();
        UpdateSpecBoxVisibility();
    }

    #endregion

    #region Event Handlers - Buttons

    private async void BtnNewActLog_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStudent == null)
        {
            await ShowInfoDialogAsync("학생이 선택되지 않았습니다.", "경고");
            return;
        }

        if (_category == LogCategory.교과활동 || _category == LogCategory.동아리활동)
        {
            await ShowInfoDialogAsync($"{_category} 기록은 조회만 가능합니다.", "안내");
            return;
        }

        var currentUser = Settings.User.Value;
        
        var newLog = new StudentLog
        {
            Category = _category == LogCategory.전체 ? LogCategory.기타 : _category,
            TeacherID = currentUser,
            Year = _year,
            Semester = _semester == 0 ? 1 : _semester, // 전체면 1학기로
            StudentID = _selectedStudent.StudentID,
            Date = DateTime.Now,
            SubjectName = string.Empty,
            CourseNo = 0
        };
        var logDialog = new StudentLogDialog(newLog);
        logDialog.Closed += async (s, args) =>
        {
            if (_selectedStudent != null)
                await LoadLogsAsync();
        };
        logDialog.Activate();
    }

    private async void BtnSaveActLog_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStudent == null || LogList == null) return;

        try
        {
            var selectedLogs = LogList.SelectedLogs.ToList();
            using var service = new StudentLogService();
            foreach (var logViewModel in selectedLogs)
            {
                var log = logViewModel.StudentLog;
                
                if (log.No > 0)
                {
                    await service.UpdateAsync(log);
                }
                else
                {
                    var newNo = await service.InsertAsync(log);
                    log.No = newNo;
                    logViewModel.StudentLog = log;
                }
                
                logViewModel.IsSelected = false;
            }

            if (selectedLogs.Count > 0)
            {
                await ShowInfoDialogAsync("저장이 완료되었습니다.", "완료");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"저장 중 오류가 발생했습니다: {ex.Message}", "오류");
        }
    }

    private async void BtnDelActLog_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStudent == null || LogList == null) return;

        try
        {
            var selectedLogs = LogList.SelectedLogs.ToList();

            foreach (var logViewModel in selectedLogs)
            {
                var log = logViewModel.StudentLog;
                
                var result = await ShowDeleteConfirmDialogAsync(log);
                if (result)
                {
                    if (log.No > 0)
                    {
                        using var service = new StudentLogService();
                        await service.DeleteAsync(log.No);
                    }
                    LogList.Logs?.Remove(logViewModel);
                }
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"삭제 중 오류가 발생했습니다: {ex.Message}", "오류");
        }
    }

    private void BtnEditLog_Click(object sender, RoutedEventArgs e)
    {
        LogList?.EditSelectedLog();
    }

    private async void BtnPrint_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStudent == null)
        {
            await ShowInfoDialogAsync("학생을 선택해주세요.", "안내");
            return;
        }

        try
        {
            var logs = LogList?.Logs?.ToList();
            if (logs == null || logs.Count == 0)
            {
                await ShowInfoDialogAsync("인쇄할 기록이 없습니다.", "안내");
                return;
            }

            // StudentCardViewModel 구성
            var studentVm = new StudentCardViewModel();
            await studentVm.LoadStudentAsync(_selectedStudent.StudentID);

            var printService = new StudentLogPrintService();
            string filePath = printService.GenerateStudentLogPdf(studentVm, logs);

            // PDF 열기
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"인쇄 중 오류가 발생했습니다: {ex.Message}", "오류");
        }
    }

    private async void BtnBatchExport_Click(object sender, RoutedEventArgs e)
    {
        await BatchExportAsync();
    }

    private async Task BatchExportAsync()
    {
        int year = FilterPicker.SelectedYear;
        int grade = FilterPicker.SelectedGrade;
        int classNo = FilterPicker.SelectedClass;

        if (year == 0 || grade == 0 || classNo == 0)
        {
            await ShowInfoDialogAsync("학년도, 학년, 반을 모두 선택해주세요.", "안내");
            return;
        }

        // 필터 다이얼로그 표시
        var filterDialog = new Dialogs.BatchExportFilterDialog
        {
            XamlRoot = this.XamlRoot
        };
        var dialogResult = await filterDialog.ShowAsync();
        if (dialogResult != ContentDialogResult.Primary) return;

        var filterCategory = filterDialog.SelectedCategory;
        var filterSemester = filterDialog.SelectedSemester;
        var keyword = filterDialog.Keyword;
        bool isPdf = filterDialog.IsPdf;

        try
        {
            string schoolCode = Settings.SchoolCode.Value;

            // 학급 전체 학생 조회
            using var enrollmentService = new EnrollmentService();
            var enrollments = await enrollmentService.GetClassRosterAsync(schoolCode, year, grade, classNo);

            if (enrollments.Count == 0)
            {
                await ShowInfoDialogAsync("해당 학급에 학생이 없습니다.", "안내");
                return;
            }

            using var logService = new StudentLogService();
            var studentLogsList = new List<(StudentCardViewModel Student, List<StudentLogViewModel> Logs)>();
            int totalLogs = 0;

            foreach (var enrollment in enrollments.OrderBy(e => e.Number))
            {
                // 학기 필터에 따라 조회
                List<StudentLog> logs;
                if (filterSemester == 0)
                {
                    var logs1 = await logService.GetStudentLogsAsync(enrollment.StudentID, year, 1);
                    var logs2 = await logService.GetStudentLogsAsync(enrollment.StudentID, year, 2);
                    logs = logs1.Concat(logs2).ToList();
                }
                else
                {
                    logs = await logService.GetStudentLogsAsync(enrollment.StudentID, year, filterSemester);
                }

                // 카테고리 필터
                if (filterCategory != LogCategory.전체)
                    logs = logs.Where(l => l.Category == filterCategory).ToList();

                // 키워드 필터 (주제, 활동명, 활동내용, 기록에서 검색)
                if (!string.IsNullOrEmpty(keyword))
                {
                    logs = logs.Where(l =>
                        (l.Topic != null && l.Topic.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                        (l.ActivityName != null && l.ActivityName.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                        (l.Description != null && l.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                        (l.Log != null && l.Log.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    ).ToList();
                }

                if (logs.Count == 0) continue;

                // Concat(1학기+2학기) 또는 필터 후 정렬이 필요한 경우만 재정렬
                if (filterSemester == 0 || filterCategory != LogCategory.전체 || !string.IsNullOrEmpty(keyword))
                    logs = logs.OrderByDescending(l => l.Date).ToList();

                var logVms = logs.Select(l => new StudentLogViewModel(l)).ToList();

                var studentVm = new StudentCardViewModel();
                studentVm.LoadFromEnrollment(enrollment);

                studentLogsList.Add((studentVm, logVms));
                totalLogs += logs.Count;
            }

            if (studentLogsList.Count == 0)
            {
                await ShowInfoDialogAsync("조건에 맞는 기록이 없습니다.", "안내");
                return;
            }

            // 하나의 문서로 출력
            string filePath;
            if (isPdf)
            {
                var printService = new StudentLogPrintService();
                filePath = printService.GenerateClassLogPdf(year, grade, classNo, studentLogsList);
            }
            else
            {
                var exportService = new StudentLogExportService();
                filePath = exportService.ExportClassLogsToExcel(year, grade, classNo, studentLogsList);
            }

            // 파일 열기
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });

            await ShowInfoDialogAsync(
                $"{studentLogsList.Count}명, 총 {totalLogs}건의 기록을 출력했습니다.\n저장 위치: {filePath}",
                "일괄 출력 완료");
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"일괄 출력 중 오류가 발생했습니다: {ex.Message}", "오류");
        }
    }

    private void BtnFontResize_Click(object sender, RoutedEventArgs e)
    {
        if (TbMax != null && TbMin != null && TbSize != null && SldFontSize != null)
        {
            TbMax.Text = SldFontSize.Maximum.ToString();
            TbMin.Text = SldFontSize.Minimum.ToString();
            TbSize.Text = SldFontSize.Value.ToString("F0");
        }
    }

    private async void BtnAddLogAll_Click(object sender, RoutedEventArgs e)
    {
        if (_grade == 0 || _classroom == 0)
        {
            await ShowInfoDialogAsync("학년과 반을 먼저 선택해주세요.", "경고");
            return;
        }

        // 교과활동/동아리활동은 학급 일괄 입력 불가 (별도 경로 사용)
        var batchCategory = _category;
        if (batchCategory == LogCategory.전체 ||
            batchCategory == LogCategory.교과활동 ||
            batchCategory == LogCategory.동아리활동)
        {
            batchCategory = LogCategory.자율활동; // 기본값
        }

        try
        {
            var dialog = new StudentLogDialog(
                SchoolDatabase.DbPath,
                batchCategory,
                _year,
                _semester == 0 ? 1 : _semester,
                _grade,
                _classroom);

            dialog.Closed += async (s, args) =>
            {
                if (_selectedStudent != null)
                    await LoadLogsAsync();
            };
            dialog.Activate();
        }
        catch (ArgumentException ex)
        {
            await ShowInfoDialogAsync(ex.Message, "안내");
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"일괄입력 중 오류가 발생했습니다: {ex.Message}", "오류");
        }
    }

    #endregion

    #region Event Handlers - Font Resize

    private void SldFontSize_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Slider slider) return;

        var delta = e.GetCurrentPoint(slider).Properties.MouseWheelDelta;
        if (delta > 0)
        {
            slider.Value = Math.Min(slider.Maximum, slider.Value + 1);
        }
        else
        {
            slider.Value = Math.Max(slider.Minimum, slider.Value - 1);
        }

        e.Handled = true;
    }

    private void SldFontSize_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (LogList != null)
        {
            LogList.FontSize = e.NewValue;
        }

        if (TbSize != null)
        {
            TbSize.Text = e.NewValue.ToString("F0");
        }
    }

    #endregion

    #region Event Handlers - Other

    private void LogList_Unloaded(object sender, RoutedEventArgs e)
    {
        _ = CheckUnSavedAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"[PageStudentLog] {t.Exception?.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
        _logService?.Dispose();
        if (StudentList != null)
            StudentList.StudentSelected -= OnStudentSelected;
    }

    private void ChkShowSpec_Click(object sender, RoutedEventArgs e)
    {
        ShowSpec(ChkShowSpec.IsChecked == true);
    }

    #endregion

    #region Data Loading Methods

    private async Task LoadLogsAsync()
    {
        if (_selectedStudent == null || LogList == null) return;

        try
        {
            List<StudentLog> logs;
            using var service = new StudentLogService();
            if (_semester == 0)
            {
                // 전체 학기: 1학기와 2학기 모두 조회
                var logs1 = await service.GetStudentLogsAsync(_selectedStudent.StudentID, _year,1);
                var logs2 = await service.GetStudentLogsAsync(_selectedStudent.StudentID, _year,2);
                logs = logs1.Concat(logs2).ToList();
            }
            else
            {
                logs = await service.GetStudentLogsAsync(_selectedStudent.StudentID, _year);
            }

            // 카테고리 필터 적용
            if (_category != LogCategory.전체)
            {
                logs = logs.Where(l => l.Category == _category).ToList();
            }

            // 날짜순 정렬
            logs = logs.OrderByDescending(l => l.Date).ToList();

            // ViewModel으로 변환
            var viewModels = logs.Select(l => new StudentLogViewModel(l)).ToList();

            LogList.Logs.Clear();
            foreach (var vm in viewModels)
            {
                LogList.Logs.Add(vm);
            }

            // 학생 개인별 보기이므로 학생 정보 모두 숨김
            LogList.StudentInfoMode = Models.StudentInfoMode.HideAll;
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"로그 로드 중 오류가 발생했습니다: {ex.Message}", "오류");
        }
    }

    /// <summary>
    /// StudentSpecial 데이터 로드
    /// </summary>
    private async void LoadSpecAsync()
    {
        if (_selectedStudent == null || SpecBox == null)
            return;

        try
        {
            using var service = new StudentSpecialService();
            var specials = await service.GetByStudentAsync(_selectedStudent.StudentID, _year);

            // 카테고리에 맞는 Type 찾기
            string type = _category.ToString();
            var special = specials.FirstOrDefault(s => s.Type == type);

            if (special != null)
            {
                // 기존 데이터 로드
                SpecBox.Special = special;
            }
            else
            {
                // 새 데이터 생성
                SpecBox.Special = new StudentSpecial
                {
                    StudentID = _selectedStudent.StudentID,
                    Year = _year,
                    Type = type,
                    Title = $"{_selectedStudent.Name} {type}",
                    Date = DateTime.Now.ToString("yyyy-MM-dd"),
                    TeacherID = Settings.User.Value,
                    IsFinalized = false
                };
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"학생부 기록 로드 중 오류가 발생했습니다: {ex.Message}", "오류");
        }
    }

    #endregion

    #region Helper Methods

    private async Task CheckUnSavedAsync()
    {
        if (_selectedStudent == null || LogList == null) return;

        try
        {
            var modifiedLogs = LogList.Logs.Where(vm => vm.IsSelected).ToList();

            foreach (var logViewModel in modifiedLogs)
            {
                var log = logViewModel.StudentLog;
                var result = await ShowSaveConfirmDialogAsync(log);

                using var service = new StudentLogService();
                if (result)
                {
                    if (log.No > 0)
                    {
                        await service.UpdateAsync(log);
                    }
                    else
                    {
                        var newNo = await service.InsertAsync(log); 
                        log.No = newNo;
                    }
                    logViewModel.IsSelected = false;
                }
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"저장 확인 중 오류가 발생했습니다: {ex.Message}", "오류");
        }
    }

    private void UpdateSpecBoxVisibility()
    {
        if (SpecBox == null || _selectedStudent == null)
        {
            if (SpecBox != null)
                SpecBox.Visibility = Visibility.Collapsed;
            return;
        }

        // 특정 카테고리에서만 SpecBox 표시
        switch (_category)
        {
            case LogCategory.자율활동:
            case LogCategory.진로활동:
            case LogCategory.종합의견:
            case LogCategory.개인별세특:
                LoadSpecAsync();
                SpecBox.Visibility = Visibility.Visible;
                ChkShowSpec.Visibility = Visibility.Visible;
                break;
            default:
                SpecBox.Visibility = Visibility.Collapsed;
                ChkShowSpec.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void ShowSpec(bool showSpec)
    {
        if (SpecBox != null)
        {
            SpecBox.Visibility = showSpec ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    #endregion

    #region 컨텍스트 메뉴

    /// <summary>학생 목록 우클릭 컨텍스트 메뉴 설정</summary>
    private void SetupStudentContextMenu()
    {
        var menu = new MenuFlyout();

        var miAddLog = new MenuFlyoutItem
        {
            Text = "누가기록 작성",
            Icon = new FontIcon { Glyph = "\uE70F" }
        };
        miAddLog.Click += ContextMenu_AddLog_Click;

        var miViewInfo = new MenuFlyoutItem
        {
            Text = "학생 정보 보기",
            Icon = new FontIcon { Glyph = "\uE77B" }
        };
        miViewInfo.Click += ContextMenu_ViewStudentInfo_Click;

        menu.Items.Add(miAddLog);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(miViewInfo);

        StudentList.ItemContextFlyout = menu;
    }

    private async void ContextMenu_AddLog_Click(object sender, RoutedEventArgs e)
    {
        var student = StudentList.SelectedStudent;
        if (student == null) return;

        if (_year == 0)
        {
            await ShowInfoDialogAsync("학년도를 먼저 선택해주세요.", "알림");
            return;
        }

        var logDialog = new StudentLogDialog(
            student,
            _year,
            _semester);
        logDialog.Closed += async (s, args) =>
        {
            await LoadLogsAsync();
        };
        logDialog.Activate();
    }

    private async void ContextMenu_ViewStudentInfo_Click(object sender, RoutedEventArgs e)
    {
        var student = StudentList.SelectedStudent;
        if (student == null) return;

        var card = new StudentCard();
        await card.LoadStudentAsync(student.StudentID);

        var dialog = new ContentDialog
        {
            Title = $"{student.Name} — 학생 정보",
            Content = card,
            CloseButtonText = "닫기",
            XamlRoot = this.XamlRoot,
            MinWidth = 700,
            MaxHeight = 600
        };

        await dialog.ShowAsync();
    }

    #endregion

    #region Dialog Methods

    private async Task<bool> ShowSaveConfirmDialogAsync(StudentLog log)
    {
        return await MessageBox.ShowConfirmAsync(
            $"저장되지 않은 자료가 있습니다. 저장할까요?\n\n" +
            $"대상자: {_selectedStudent?.Grade}학년 {_selectedStudent?.Class}반 {_selectedStudent?.Number}번 {_selectedStudent?.Name}\n" +
            $"날짜: {log.Date:yyyy년 M월 d일}\n" +
            $"주제: {log.Topic}\n",
            "저장 확인", "예", "아니오");
    }

    private async Task<bool> ShowDeleteConfirmDialogAsync(StudentLog log)
    {
        return await MessageBox.ShowConfirmAsync(
            $"삭제하면 되돌릴 수 없습니다. 삭제할까요?\n\n" +
            $"대상자: {_selectedStudent?.Grade}학년 {_selectedStudent?.Class}반 {_selectedStudent?.Number}번 {_selectedStudent?.Name}\n" +
            $"날짜: {log.Date:yyyy년 M월 d일}\n" +
            $"주제: {log.Topic}\n",
            "삭제 확인", "삭제", "취소");
    }

    private async Task ShowInfoDialogAsync(string message, string title = "안내")
    {
        await MessageBox.ShowAsync(message, title);
    }

    #endregion
}
