using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
public sealed partial class ClubActivityPage : Page
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
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // ClubHomePage에서 전달된 Club 정보
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

            using var enrollmentService = new EnrollmentService();
            var members = new List<Enrollment>();

            foreach (var ce in clubEnrollments)
            {
                var enrollment = await enrollmentService.GetCurrentEnrollmentAsync(ce.StudentID);
                if (enrollment != null)
                {
                    members.Add(enrollment);
                }
            }

            // 정렬: 학년 → 반 → 번호
            var sorted = members
                .OrderBy(s => s.Grade)
                .ThenBy(s => s.Class)
                .ThenBy(s => s.Number)
                .ToList();

            StudentList.LoadStudents(sorted);
            TxtMemberCount.Text = $"{sorted.Count}명";

            // 선택 초기화
            _selectedStudent = null;
            LogList?.Logs?.Clear();
            TxtSelectedStudent.Text = "활동 기록";
            TxtLogCount.Text = "";

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
        if (CBoxClub.SelectedItem is Club club)
        {
            _selectedClub = club;
            await LoadMembersAsync();
        }
    }

    /// <summary>
    /// 학생 선택 변경
    /// </summary>
    private async void OnStudentSelected(object? sender, Enrollment student)
    {
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
            _category = category;
            await LoadLogsAsync();
        }
    }

    #endregion

    #region Event Handlers - Buttons

    /// <summary>
    /// 새로고침
    /// </summary>
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
        dialog.Closed += async (s, args) => await LoadLogsAsync();
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

            foreach (var logVm in selectedLogs)
            {
                var log = logVm.StudentLog;

                if (log.No > 0)
                {
                    await logService.DeleteAsync(log.No);
                }

                LogList.Logs?.Remove(logVm);
            }

            TxtLogCount.Text = $"({LogList.Logs?.Count ?? 0}건)";
            ShowInfoBar($"{selectedLogs.Count}건이 삭제되었습니다.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClubActivityPage] 삭제 실패: {ex.Message}");
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
        if (_selectedStudent == null || _selectedClub == null || SpecBox == null)
        {
            if (SpecBox != null)
                SpecBox.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
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
            Debug.WriteLine($"[ClubActivityPage] 학생부 기록 로드 실패: {ex.Message}");
            SpecBox.Visibility = Visibility.Collapsed;
        }
    }

    #endregion

    #region Helper Methods

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
