using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Dialogs;
using NewSchool.Models;
using NewSchool.Services;
using NewSchool.ViewModels;

namespace NewSchool.Pages;

/// <summary>
/// 수업 활동 기록 페이지
/// 교사가 담당하는 과목의 학생 활동 기록 관리
/// </summary>
public sealed partial class LessonActivityPage : Page
{
    #region Fields

    private Course? _selectedCourse;
    private string? _selectedRoom;
    private Enrollment? _selectedStudent;

    #endregion

    #region Constructor

    public LessonActivityPage()
    {
        this.InitializeComponent();

        InitializeControls();
    }

    #endregion

    #region Initialization

    private void InitializeControls()
    {
        // StudentList 이벤트 연결
        StudentList.StudentSelected += OnStudentSelected;

        // LogListViewer 초기 설정 — 교과활동 모드
        LogList.Category = LogCategory.교과활동;

        SetupStudentContextMenu();
    }

    #endregion

    #region Data Loading

    /// <summary>
    /// 활동 기록 로드
    /// </summary>
    private async Task LoadLogsAsync()
    {
        if (_selectedStudent == null || _selectedCourse == null || LogList == null)
        {
            LogList?.Logs?.Clear();
            return;
        }

        try
        {
            using var logService = new StudentLogService();

            // 해당 학생의 해당 연도 로그 조회
            var logs = await logService.GetStudentLogsAsync(
                _selectedStudent.StudentID,
                Settings.WorkYear.Value,
                Settings.WorkSemester.Value
            );

            // 필터링: 해당 과목의 교과활동만
            logs = logs.Where(l =>
                l.Category == LogCategory.교과활동 &&
                l.SubjectName == _selectedCourse.Subject
            ).ToList();

            // 날짜순 정렬
            logs = logs.OrderByDescending(l => l.Date).ToList();

            // ViewModel 변환
            LogList.Logs.Clear();
            foreach (var log in logs)
            {
                LogList.Logs.Add(new StudentLogViewModel(log));
            }

            // 학생 개별 보기 — PageStudentLog과 동일
            LogList.StudentInfoMode = StudentInfoMode.HideAll;
            LogList.Category = LogCategory.교과활동;

            Debug.WriteLine($"[LessonActivityPage] 로그 로드 완료: {logs.Count}건");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonActivityPage] 로그 로드 실패: {ex.Message}");
            ShowInfoBar($"활동 기록을 불러오는데 실패했습니다: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    #endregion

    #region Event Handlers - Selection

    /// <summary>
    /// 과목/강의실 선택 확정 — CoursePicker 가 수강생 목록까지 함께 전달
    /// </summary>
    private void CoursePickerCtl_LoadError(object? sender, string message)
    {
        ShowInfoBar(message, InfoBarSeverity.Error);
    }

    private void CoursePickerCtl_CourseChanged(object? sender, CourseChangedEventArgs e)
    {
        _selectedCourse = e.Course;
        _selectedRoom = e.Room;

        var sorted = e.Students
            .OrderBy(s => s.Grade)
            .ThenBy(s => s.Class)
            .ThenBy(s => s.Number)
            .ToList();

        StudentList.LoadStudents(sorted);
        TxtStudentCount.Text = $"{sorted.Count}명";

        _selectedStudent = null;
        LogList?.Logs?.Clear();
    }

    /// <summary>
    /// 학생 선택 변경
    /// </summary>
    private async void OnStudentSelected(object? sender, Enrollment student)
    {
        if (SpecBox != null)
            await SpecBox.ConfirmLeaveAsync();

        _selectedStudent = student;
        await LoadLogsAsync();
        await LoadSpecAsync();
    }



    #endregion

    #region Event Handlers - Buttons

    /// <summary>
    /// 편집
    /// </summary>
    private void BtnEditLog_Click(object sender, RoutedEventArgs e)
    {
        LogList?.EditSelectedLog();
    }

    /// <summary>
    /// 새로고침
    /// </summary>
    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await CoursePickerCtl.LoadAsync(Settings.WorkYear.Value, Settings.WorkSemester.Value);
    }

    /// <summary>
    /// 일괄 입력 — 현재 선택된 수업의 수강생 전체 대상
    /// </summary>
    private void BtnBatchInput_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null)
        {
            ShowInfoBar("수업을 먼저 선택해주세요.", InfoBarSeverity.Warning);
            return;
        }

        var dialog = new StudentLogDialog(
            SchoolDatabase.DbPath,
            LogCategory.교과활동,
            Settings.WorkYear.Value,
            Settings.WorkSemester.Value,
            _selectedCourse.No,
            Settings.User.Value);

        // 람다 캡처를 피하기 위해 dialog 를 지역 변수로 두고 명시적으로 닫기 핸들러 분리
        var capturedDialog = dialog;
        async void OnBatchDialogClosed(object s, Microsoft.UI.Xaml.WindowEventArgs args)
        {
            capturedDialog.Closed -= OnBatchDialogClosed;
            if (capturedDialog.IsSuccess)
            {
                await LoadLogsAsync();
                ShowInfoBar($"{capturedDialog.SavedLogs.Count}건이 일괄 저장되었습니다.", InfoBarSeverity.Success);
            }
        }
        dialog.Closed += OnBatchDialogClosed;
        dialog.Activate();
    }

    // 다이얼로그 Closed → 로그 재로드 공용 핸들러 (자기 이벤트 해제)
    private async void OnLogDialogClosedReload(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
    {
        if (sender is Window w) w.Closed -= OnLogDialogClosedReload;
        await LoadLogsAsync();
    }

    /// <summary>
    /// 활동 기록 추가
    /// </summary>
    private void BtnAddLog_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStudent == null)
        {
            ShowInfoBar("학생을 선택해주세요.", InfoBarSeverity.Warning);
            return;
        }

        if (_selectedCourse == null)
        {
            ShowInfoBar("수업을 선택해주세요.", InfoBarSeverity.Warning);
            return;
        }

        // 새 로그 생성
        var newLog = new StudentLog
        {
            Category = LogCategory.교과활동,
            TeacherID = Settings.User.Value,
            Year = Settings.WorkYear.Value,
            Semester = Settings.WorkSemester.Value,
            StudentID = _selectedStudent.StudentID,
            Date = DateTime.Now,
            SubjectName = _selectedCourse.Subject,
            CourseNo = _selectedCourse.No
        };

        var dialog = new StudentLogDialog(newLog);
        dialog.Closed += OnLogDialogClosedReload;
        dialog.Activate();
    }

    /// <summary>
    /// 저장
    /// </summary>
    private async void BtnSaveLog_Click(object sender, RoutedEventArgs e)
    {
        if (LogList == null) return;

        try
        {
            var selectedLogs = LogList.SelectedLogs.ToList();

            if (selectedLogs.Count == 0)
            {
                ShowInfoBar("저장할 기록을 선택해주세요.", InfoBarSeverity.Warning);
                return;
            }

            using var logService = new StudentLogService();

            foreach (var logVm in selectedLogs)
            {
                var log = logVm.StudentLog;

                if (log.No > 0)
                {
                    await logService.UpdateAsync(log);
                }
                else
                {
                    var newNo = await logService.InsertAsync(log);
                    log.No = newNo;
                }

                logVm.IsSelected = false;
            }

            ShowInfoBar($"{selectedLogs.Count}건이 저장되었습니다.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonActivityPage] 저장 실패: {ex.Message}");
            ShowInfoBar($"저장 중 오류가 발생했습니다: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    /// <summary>
    /// 삭제
    /// </summary>
    private async void BtnDeleteLog_Click(object sender, RoutedEventArgs e)
    {
        if (LogList == null) return;

        try
        {
            var selectedLogs = LogList.SelectedLogs.ToList();

            if (selectedLogs.Count == 0)
            {
                ShowInfoBar("삭제할 기록을 선택해주세요.", InfoBarSeverity.Warning);
                return;
            }

            // 확인 다이얼로그
            var confirmed = await MessageBox.ShowConfirmAsync(
                $"{selectedLogs.Count}건의 기록을 삭제하시겠습니까?\n삭제된 기록은 복구할 수 없습니다.",
                "삭제 확인", "삭제", "취소");
            if (!confirmed) return;

            using var logService = new StudentLogService();

            foreach (var logVm in selectedLogs)
            {
                var log = logVm.StudentLog;

                if (log.No > 0)
                {
                    await logService.DeleteAsync(log.No);
                }

                LogList.Logs?.Remove(logVm);
            }

            ShowInfoBar($"{selectedLogs.Count}건이 삭제되었습니다.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonActivityPage] 삭제 실패: {ex.Message}");
            ShowInfoBar($"삭제 중 오류가 발생했습니다: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    #endregion

    #region StudentSpec 로드

    /// <summary>
    /// 학생부 기록 로드
    /// </summary>
    private async Task LoadSpecAsync()
    {
        if (_selectedStudent == null || _selectedCourse == null || SpecBox == null)
        {
            if (SpecBox != null)
                SpecBox.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            SpecBox.StudentName = $"{_selectedStudent.GetClassInfo()} {_selectedStudent.Name}";

            using var service = new StudentSpecialService();
            var specials = await service.GetByStudentAsync(_selectedStudent.StudentID, Settings.WorkYear.Value);

            // 교과활동 타입 + 과목명으로 검색
            string type = "교과활동";
            var special = specials.FirstOrDefault(s => 
                s.Type == type && s.Title == _selectedCourse.Subject);

            if (special != null)
            {
                SpecBox.Special = special;
            }
            else
            {
                // 새 데이터 생성
                SpecBox.Special = new StudentSpecial
                {
                    StudentID = _selectedStudent.StudentID,
                    Year = Settings.WorkYear.Value,
                    Type = type,
                    Title = _selectedCourse.Subject,
                    SubjectName = _selectedCourse.Subject,
                    CourseNo = _selectedCourse.No,
                    Date = DateTime.Now.ToString("yyyy-MM-dd"),
                    TeacherID = Settings.User.Value,
                    IsFinalized = false
                };
            }

            SpecBox.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonActivityPage] 학생부 기록 로드 실패: {ex.Message}");
            SpecBox.Visibility = Visibility.Collapsed;
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
        if (student == null || _selectedCourse == null) return;

        var logDialog = new StudentLogDialog(
            student,
            Settings.WorkYear.Value,
            Settings.WorkSemester.Value);
        logDialog.Closed += OnLogDialogClosedReload;
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

    #region Helper Methods

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        StudentList.StudentSelected -= OnStudentSelected;
    }

    /// <summary>
    /// InfoBar 표시
    /// </summary>
    private void ShowInfoBar(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        PageInfoBar.Message = message;
        PageInfoBar.Severity = severity;
        PageInfoBar.IsOpen = true;
    }

    #endregion
}
