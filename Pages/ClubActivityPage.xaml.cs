using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using NewSchool.Controls;
using NewSchool.Dialogs;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;
using NewSchool.ViewModels;

namespace NewSchool.Pages;

/// <summary>
/// 동아리 활동 기록 페이지
/// 담당 동아리 부원의 활동 기록 관리
/// </summary>
public sealed partial class ClubActivityPage : Page, NewSchool.Controls.IAsksBeforeLeaving
{
    #region Fields

    private Club? _selectedClub;
    private Enrollment? _selectedStudent;
    private LogCategory _category = LogCategory.동아리활동;

    /// <summary>동아리 목록</summary>
    public ObservableCollection<Club> Clubs { get; } = new();

    #endregion

    #region Constructor

    public ClubActivityPage()
    {
        this.InitializeComponent();

        InitializeControls();
    }

    #endregion

    #region Initialization

    private void InitializeControls()
    {
        // 카테고리 콤보박스 설정
        var categories = new List<LogCategory>
        {
            LogCategory.동아리활동,
            LogCategory.전체
        };
        CBoxCategory.ItemsSource = categories;
        CBoxCategory.SelectedIndex = 0;

        // StudentList 이벤트 연결
        StudentList.StudentSelected += OnStudentSelected;

        // ComboBox 바인딩
        CBoxClub.ItemsSource = Clubs;

        SetupStudentContextMenu();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // 파라미터로 동아리를 받으면 그것을 고른 채로 연다.
        // 지금 호출부(메인 네비게이션)는 파라미터 없이 열고, 그때는 콤보에서 고른다.
        if (e.Parameter is Club club)
        {
            _selectedClub = club;
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadClubsAsync();

        // 전달받은 동아리가 있으면 선택
        if (_selectedClub != null)
        {
            var found = Clubs.FirstOrDefault(c => c.No == _selectedClub.No);
            if (found != null)
            {
                CBoxClub.SelectedItem = found;
            }
        }
    }

    #endregion

    #region Data Loading

    /// <summary>
    /// 내 동아리 목록 로드
    /// </summary>
    private async Task LoadClubsAsync()
    {
        try
        {
            string teacherId = Settings.User.Value;
            int year = Settings.WorkYear.Value;

            using var repo = new ClubRepository(SchoolDatabase.DbPath);
            var clubs = await repo.GetByTeacherAsync(teacherId, year);

            Clubs.Clear();
            foreach (var club in clubs)
            {
                Clubs.Add(club);
            }

            // 첫 번째 동아리 자동 선택
            if (Clubs.Count > 0 && CBoxClub.SelectedItem == null)
            {
                CBoxClub.SelectedIndex = 0;
            }
            else if (Clubs.Count == 0)
            {
                ShowInfoBar("등록된 동아리가 없습니다.", InfoBarSeverity.Warning);
            }

            Debug.WriteLine($"[ClubActivityPage] 동아리 로드 완료: {Clubs.Count}개");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClubActivityPage] 동아리 로드 실패: {ex.Message}");
            ShowInfoBar($"동아리 목록을 불러오는데 실패했습니다: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    /// <summary>
    /// 부원 목록 로드
    /// </summary>
    private async Task LoadMembersAsync()
    {
        // 부원을 다시 읽기 전에 — 학생을 바꿀 때(OnStudentSelected)처럼 편집 중인 학생부를 먼저
        // 묻고, 선택한 학생을 비운다(학생 정보 화면의 LoadStudentListAsync 와 같은 규칙).
        //
        // 예전에는 선택 초기화가 부원을 다 읽은 "끝" 에만 있어서, 부원이 없는 동아리로 바꾸면
        // 일찍 return 하느라 앞 동아리 학생이 선택된 채 남았다. 그 상태로 [새 기록] 을 누르면
        // 그 학생의 기록이 **그 학생이 속하지도 않은 새 동아리** 로 만들어졌다.
        // 동아리는 이미 바뀌었으니 멈추지 않는다 — 저장하지 못했으면 버리고 알린다(LeaveAsync).
        await CheckUnSavedAsync();
        if (SpecBox != null) await SpecBox.LeaveAsync();
        ClearSelectedStudent();

        if (_selectedClub == null)
        {
            StudentList.ClearStudents();
            TxtMemberCount.Text = "0명";
            return;
        }

        try
        {
            using var enrollmentRepo = new ClubEnrollmentRepository(SchoolDatabase.DbPath);
            var clubEnrollments = await enrollmentRepo.GetByClubAsync(_selectedClub.No);

            if (clubEnrollments.Count == 0)
            {
                StudentList.ClearStudents();
                TxtMemberCount.Text = "0명";
                return;
            }

            // 부원 학적을 IN 쿼리로 일괄 조회 (부원 수만큼 쿼리하던 N+1 제거)
            using var enrollmentService = new EnrollmentService();
            var studentIds = clubEnrollments.Select(ce => ce.StudentID).ToList();
            var members = await enrollmentService.GetCurrentEnrollmentsAsync(studentIds);

            // 정렬: 학년 → 반 → 번호
            var sorted = members
                .OrderBy(s => s.Grade)
                .ThenBy(s => s.Class)
                .ThenBy(s => s.Number)
                .ToList();

            StudentList.LoadStudents(sorted);
            TxtMemberCount.Text = $"{sorted.Count}명";

            Debug.WriteLine($"[ClubActivityPage] 부원 로드 완료: {sorted.Count}명");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClubActivityPage] 부원 로드 실패: {ex.Message}");
            ShowInfoBar($"부원 목록을 불러오는데 실패했습니다: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    /// <summary>
    /// 활동 기록 로드
    /// </summary>
    private async Task LoadLogsAsync()
    {
        if (_selectedStudent == null || _selectedClub == null || LogList == null)
        {
            LogList?.Logs?.Clear();
            TxtLogCount.Text = "";
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

            // 필터링: 해당 동아리 또는 전체
            if (_category == LogCategory.동아리활동)
            {
                logs = logs.Where(l =>
                    l.Category == LogCategory.동아리활동 &&
                    l.ClubNo == _selectedClub.No
                ).ToList();
            }
            else if (_category != LogCategory.전체)
            {
                logs = logs.Where(l => l.Category == _category).ToList();
            }

            // 날짜순 정렬
            logs = logs.OrderByDescending(l => l.Date).ToList();

            // ViewModel 변환
            LogList.Logs.Clear();
            foreach (var log in logs)
            {
                LogList.Logs.Add(new StudentLogViewModel(log));
            }

            // 동아리별 보기이므로 학년/반/번호/이름 표시, 동아리명은 동일하므로 숨김
            LogList.StudentInfoMode = Models.StudentInfoMode.GradeClassNumName;
            LogList.Category = Models.LogCategory.동아리활동;

            TxtLogCount.Text = $"({logs.Count}건)";

            Debug.WriteLine($"[ClubActivityPage] 로그 로드 완료: {logs.Count}건");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClubActivityPage] 로그 로드 실패: {ex.Message}");
            ShowInfoBar($"활동 기록을 불러오는데 실패했습니다: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    #endregion

    #region Event Handlers - Selection

    /// <summary>
    /// 동아리 선택 변경
    /// </summary>
    private async void CBoxClub_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 선택이 풀린 경우(새로고침으로 동아리 목록이 비는 등)도 같은 길로 보낸다 —
        // 그래야 앞 동아리의 부원 선택이 남지 않는다.
        _selectedClub = CBoxClub.SelectedItem as Club;
        await LoadMembersAsync();
    }

    /// <summary>
    /// 학생 선택 변경
    /// </summary>
    private async void OnStudentSelected(object? sender, Enrollment student)
    {
        await CheckUnSavedAsync();
        if (SpecBox != null && !await SpecBox.ConfirmLeaveAsync())
            return;

        _selectedStudent = student;

        // 헤더 업데이트
        TxtSelectedStudent.Text = $"{student.Name} ({student.Grade}-{student.Class} {student.Number}번)";

        await LoadLogsAsync();
        await LoadSpecAsync();
    }

    /// <summary>
    /// 카테고리 변경
    /// </summary>
    private async void CBoxCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxCategory.SelectedItem is LogCategory category)
        {
            await CheckUnSavedAsync();
            _category = category;
            await LoadLogsAsync();
        }
    }

    #endregion

    #region Event Handlers - Buttons

    /// <summary>
    /// 새로고침
    /// </summary>
    private void OnFontSizeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            FlyoutBase.ShowAttachedFlyout(button);
        }
    }

    /// <summary>
    /// 글자 크기 슬라이더 — 기록 내용 칸과 학생부 기록 박스에만 적용한다.
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

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadClubsAsync();
    }

    /// <summary>
    /// 활동 기록 추가
    /// </summary>
    private void BtnAddLog_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStudent == null)
        {
            ShowInfoBar("부원을 선택해주세요.", InfoBarSeverity.Warning);
            return;
        }

        if (_selectedClub == null)
        {
            ShowInfoBar("동아리를 선택해주세요.", InfoBarSeverity.Warning);
            return;
        }

        // 새 로그 생성
        var newLog = new StudentLog
        {
            Category = LogCategory.동아리활동,
            TeacherID = Settings.User.Value,
            Year = Settings.WorkYear.Value,
            Semester = Settings.WorkSemester.Value,
            StudentID = _selectedStudent.StudentID,
            Date = DateTime.Now,
            ClubNo = _selectedClub.No,
            ClubName = _selectedClub.ClubName
        };

        var dialog = new StudentLogDialog(newLog);
        dialog.Closed += OnLogDialogClosedReload;
        dialog.Activate();
    }

    // 다이얼로그 Closed → 로그 재로드 공용 핸들러 (자기 이벤트 해제)
    private async void OnLogDialogClosedReload(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
    {
        if (sender is Window w) w.Closed -= OnLogDialogClosedReload;
        if (!IsLoaded) return;   // 창을 연 채 이 화면을 떠났다 — 닫힌 서비스로 읽지 않는다(PageStudentLog 주석)
        await LoadLogsAsync();
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

            // 반영된 건수를 실제로 센다. 예전에는 결과를 버리고 선택 건수를 그대로 "저장됨"
            // 으로 알려서, 0행 갱신(이미 지워진 기록 등)도 성공으로 보고했다.
            int saved = 0;
            foreach (var logVm in selectedLogs)
            {
                var log = logVm.StudentLog;
                bool ok;

                if (log.No > 0)
                {
                    ok = await logService.UpdateAsync(log);
                }
                else
                {
                    var newNo = await logService.InsertAsync(log);
                    log.No = newNo;
                    ok = newNo > 0;
                }

                // 실패한 항목은 선택 상태로 남겨 다시 저장할 수 있게 한다
                if (ok)
                {
                    saved++;
                    logVm.IsSelected = false;
                }
            }

            if (saved == selectedLogs.Count)
                ShowInfoBar($"{saved}건이 저장되었습니다.", InfoBarSeverity.Success);
            else
                ShowInfoBar($"{selectedLogs.Count}건 중 {saved}건만 저장되었습니다. 남은 항목은 선택된 채로 두었습니다.",
                            InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClubActivityPage] 저장 실패: {ex.Message}");
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

            // DB 에서 실제로 지워진 것만 화면에서 내린다. 예전에는 결과를 버리고 무조건
            // 목록에서 제거해서, 삭제가 실패해도 "삭제됨"으로 보이고 새로고침하면 되살아났다.
            int deleted = 0;
            foreach (var logVm in selectedLogs)
            {
                var log = logVm.StudentLog;

                bool ok = log.No <= 0 || await logService.DeleteAsync(log.No);
                if (!ok) continue;

                deleted++;
                LogList.Logs?.Remove(logVm);
            }

            TxtLogCount.Text = $"({LogList.Logs?.Count ?? 0}건)";

            if (deleted == selectedLogs.Count)
                ShowInfoBar($"{deleted}건이 삭제되었습니다.", InfoBarSeverity.Success);
            else
                ShowInfoBar($"{selectedLogs.Count}건 중 {deleted}건만 삭제되었습니다.", InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClubActivityPage] 삭제 실패: {ex.Message}");
            ShowInfoBar($"삭제 중 오류가 발생했습니다: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    #endregion

    #region StudentSpec 로드

    /// <summary>선택한 학생과 그 학생의 기록·학생부 표시를 비운다.</summary>
    private void ClearSelectedStudent()
    {
        _selectedStudent = null;
        LogList?.Logs?.Clear();
        TxtSelectedStudent.Text = "활동 기록";
        TxtLogCount.Text = "";
        if (SpecBox != null) SpecBox.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 학생부 기록 로드
    /// </summary>
    private async Task LoadSpecAsync()
    {
        if (_selectedStudent == null || _selectedClub == null || SpecBox == null)
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

            // 동아리활동 타입 + 동아리명으로 검색
            string type = "동아리활동";
            var special = specials.FirstOrDefault(s => 
                s.Type == type && s.Title == _selectedClub.ClubName);

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
                    Title = _selectedClub.ClubName,
                    Date = DateTime.Now.ToString("yyyy-MM-dd"),
                    TeacherID = Settings.User.Value,
                    IsFinalized = false
                };
            }

            SpecBox.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Error("ClubActivityPage", "학생부 기록을 읽지 못해 칸을 감춘다 — 기록이 없는 것처럼 보인다", ex);
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
        if (student == null || _selectedClub == null) return;

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

        await MessageBox.ShowDialogAsync(dialog);
    }

    #endregion

    #region Helper Methods

    /// <summary>앱을 닫기 전에 부른다 — 닫을 때는 아래 Unloaded 가 창이 닫힌 뒤라 물을 수 없다.</summary>
    public async Task AskBeforeLeavingAsync()
    {
        await AskUnsavedOnLeaveAsync();
        LogList.ClearSelection();   // 저장하지 않은 것은 버린 것 — 닫히는 창에서 또 묻지 않게
    }

    /// <summary>
    /// 떠날 때 고친 누가기록과 <b>학생부 상자</b>를 묻는다. 학생부 상자는 학생을 바꿀 때만 물어서,
    /// 고친 채 다른 메뉴로 가거나 앱을 닫으면 말없이 버렸다(2026-09-30). 묻고 나면 저장했거나
    /// 버린 것이라 닫히는 창의 Unloaded 가 또 묻지 않는다.
    /// </summary>
    private async Task AskUnsavedOnLeaveAsync()
    {
        await CheckUnSavedAsync();
        if (SpecBox != null) await SpecBox.LeaveAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        // 화면을 떠날 때도 누가기록 화면(LogList_Unloaded)처럼 고친 기록을 묻는다.
        _ = AskUnsavedOnLeaveAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
                Debug.WriteLine($"[ClubActivityPage] {t.Exception?.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);

        StudentList.StudentSelected -= OnStudentSelected;
    }

    /// <summary>
    /// 목록을 다시 읽기 전에 고친 채 저장하지 않은 기록을 묻는다 — 누가기록 화면과 같은 한 벌
    /// (<see cref="LogListViewer.AskSaveModifiedAsync"/>). 예전에는 학생·영역·동아리를 바꾸면
    /// 고친 기록이 묻지 않고 사라졌다(학생부 상자만 물었다).
    /// </summary>
    private async Task CheckUnSavedAsync()
    {
        if (_selectedStudent == null || LogList == null) return;

        try
        {
            var s = _selectedStudent;
            await LogList.AskSaveModifiedAsync($"{s.Grade}학년 {s.Class}반 {s.Number}번 {s.Name}");
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"저장 확인 중 오류가 발생했습니다: {ex.Message}", "오류");
        }
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
