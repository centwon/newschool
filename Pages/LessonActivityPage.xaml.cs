using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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

    // 필터(CoursePicker)에서 고른 학년도·학기. Settings.WorkYear/WorkSemester 를 직접 쓰면
    // 필터로 다른 학기를 골라도 현재 학기 기준으로 조회·저장돼 기록이 엉뚱한 학기에 들어간다.
    private int _selectedYear = Settings.WorkYear.Value;
    private int _selectedSemester = Settings.WorkSemester.Value;

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
                _selectedYear,
                _selectedSemester
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
        _selectedYear = e.Year;
        _selectedSemester = e.Semester;

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
        // 미저장 학생부 편집을 저장/폐기 확인 — 저장 실패(false) 시 학생 전환 중단
        if (SpecBox != null && !await SpecBox.ConfirmLeaveAsync())
            return;

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
        await CoursePickerCtl.LoadAsync(CoursePickerCtl.SelectedYear, CoursePickerCtl.SelectedSemester);
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
            LogCategory.교과활동,
            _selectedYear,
            _selectedSemester,
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
            Year = _selectedYear,
            Semester = _selectedSemester,
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

            // 학교를 떠난 학생에게 그 뒤 날짜로 기록을 남기려는 것이면 먼저 알린다
            // (저장 경로마다 따로 적지 않고 EnrollmentGuard 한 곳에서 판단한다).
            if (!await EnrollmentGuard.ConfirmRecordsAfterLeavingAsync(
                    selectedLogs.Select(v => ((string?)v.StudentLog.StudentID, v.StudentLog.Year, v.StudentLog.Date))))
                return;

            using var logService = new StudentLogService();

            int saved = 0;
            foreach (var logVm in selectedLogs)
            {
                var log = logVm.StudentLog;

                // 반영된 것만 센다 — 예전에는 결과를 버리고 무조건 "N건 저장"이라 알려,
                // 한 건도 저장되지 않아도 선택이 풀리며 저장된 것처럼 보였다.
                bool ok = log.No > 0
                    ? await logService.UpdateAsync(log)
                    : (log.No = await logService.InsertAsync(log)) > 0;

                if (!ok) continue;

                logVm.IsSelected = false;
                saved++;
            }

            if (saved == selectedLogs.Count)
                ShowInfoBar($"{saved}건이 저장되었습니다.", InfoBarSeverity.Success);
            else
                ShowInfoBar(
                    $"{selectedLogs.Count}건 중 {saved}건만 저장됐습니다. 저장되지 않은 기록은 선택된 채로 남아 있습니다.",
                    InfoBarSeverity.Warning);
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

            int deleted = 0;
            foreach (var logVm in selectedLogs)
            {
                var log = logVm.StudentLog;

                // DB 에서 지워진 것만 목록에서 뺀다 — 예전에는 결과와 무관하게 화면에서
                // 지우고 성공을 알려, 새로 고치면 기록이 되살아났다.
                if (log.No > 0 && !await logService.DeleteAsync(log.No))
                    continue;

                LogList.Logs?.Remove(logVm);
                deleted++;
            }

            if (deleted == selectedLogs.Count)
                ShowInfoBar($"{deleted}건이 삭제되었습니다.", InfoBarSeverity.Success);
            else
                ShowInfoBar(
                    $"{selectedLogs.Count}건 중 {deleted}건만 삭제됐습니다.",
                    InfoBarSeverity.Warning);
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
            var specials = await service.GetByStudentAsync(_selectedStudent.StudentID, _selectedYear);

            // 교과 세특은 학기별이고 교과목(Course)도 학기별로 등록되므로 CourseNo 로 찾는다.
            // 과목명(Title)으로 찾던 예전 코드는 학년도 전체를 훑기 때문에, 1·2학기에 같은 과목을
            // 가르치면 다른 학기 기록을 집어와 덮어썼다. CourseNo 없는 구 기록만 과목명으로 보조 매칭.
            string type = "교과활동";
            var special = specials.FirstOrDefault(s => s.Type == type && s.CourseNo == _selectedCourse.No)
                ?? specials.FirstOrDefault(s => s.Type == type && s.CourseNo == 0
                                                && s.Title == _selectedCourse.Subject);

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
                    Year = _selectedYear,
                    // 교과 세특은 학기별 — 교과목의 학기를 저장(CourseNo 가 지워져도 학기는 남는다)
                    Semester = Helpers.NeisHelper.IsSemesterScoped(type) ? _selectedCourse.Semester : 0,
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

    private void OnFontSizeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            FlyoutBase.ShowAttachedFlyout(button);
        }
    }

    /// <summary>
    /// 글자 크기 슬라이더 — <b>기록 내용 칸에만</b> 적용한다.
    /// (LogList.FontSize 를 쓰면 행의 각 칸에 크기가 명시돼 있어 헤더 라벨만 커진다)
    /// </summary>
    private void OnFontSizeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (LogList == null) return;
        LogList.ContentFontSize = e.NewValue;

        // 학생부 기록 박스가 함께 떠 있으므로 같이 키운다(나란히 보며 옮겨 적는 화면)
        if (SpecBox != null) SpecBox.ContentFontSize = e.NewValue;

        if (TxtFontSize != null) TxtFontSize.Text = $"{e.NewValue:F0}";
    }

    private async void ContextMenu_AddLog_Click(object sender, RoutedEventArgs e)
    {
        var student = StudentList.SelectedStudent;
        if (student == null || _selectedCourse == null) return;

        var logDialog = new StudentLogDialog(
            student,
            _selectedYear,
            _selectedSemester);
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

        await MessageBox.ShowDialogAsync(dialog);
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
