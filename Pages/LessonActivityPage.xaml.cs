using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Dialogs;
using NewSchool.Models;
using NewSchool.Repositories;
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

    /// <summary>과목 목록</summary>
    public ObservableCollection<Course> Courses { get; } = new();

    /// <summary>강의실 목록</summary>
    public ObservableCollection<string> Rooms { get; } = new();

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

        // ComboBox 바인딩
        CBoxCourse.ItemsSource = Courses;
        CBoxRoom.ItemsSource = Rooms;

        // LogListViewer 초기 설정 — 교과활동 모드
        LogList.Category = LogCategory.교과활동;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadCoursesAsync();
    }

    #endregion

    #region Data Loading

    /// <summary>
    /// 내 수업 목록 로드
    /// </summary>
    private async Task LoadCoursesAsync()
    {
        try
        {
            using var courseService = new CourseService();
            var courses = await courseService.GetMyCoursesAsync();

            Courses.Clear();
            foreach (var course in courses)
            {
                Courses.Add(course);
            }

            // 첫 번째 과목 자동 선택
            if (Courses.Count > 0)
            {
                CBoxCourse.SelectedIndex = 0;
            }
            else
            {
                ShowInfoBar("등록된 수업이 없습니다.", InfoBarSeverity.Warning);
            }

            Debug.WriteLine($"[LessonActivityPage] 과목 로드 완료: {Courses.Count}개");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonActivityPage] 과목 로드 실패: {ex.Message}");
            ShowInfoBar($"과목 목록을 불러오는데 실패했습니다: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    /// <summary>
    /// 강의실 목록 로드
    /// </summary>
    private void LoadRooms()
    {
        Rooms.Clear();
        Rooms.Add("전체"); // 기본 옵션

        if (_selectedCourse != null && !string.IsNullOrEmpty(_selectedCourse.Rooms))
        {
            foreach (var room in _selectedCourse.RoomList)
            {
                Rooms.Add(room);
            }
        }

        CBoxRoom.SelectedIndex = 0;
    }

    /// <summary>
    /// 수강생 목록 로드
    /// </summary>
    private async Task LoadStudentsAsync()
    {
        if (_selectedCourse == null)
        {
            StudentList.ClearStudents();
            TxtStudentCount.Text = "0명";
            return;
        }

        try
        {
            List<Enrollment> students;

            if (_selectedRoom != null && _selectedRoom != "전체")
            {
                // 특정 강의실 선택: CourseEnrollment.Room으로 필터
                students = await LoadStudentsByRoomFilterAsync(_selectedRoom);
            }
            else if (_selectedCourse.IsClassType)
            {
                // 학급 공통 + 전체: 해당 학년 전체 학생
                using var enrollmentService = new EnrollmentService();
                students = await enrollmentService.GetEnrollmentsAsync(
                    Settings.SchoolCode.Value,
                    Settings.WorkYear.Value,
                    0,
                    _selectedCourse.Grade);
            }
            else
            {
                // Selective/Club + 전체: CourseEnrollment 기반
                students = await LoadStudentsByCourseEnrollmentAsync();
            }

            // 정렬: 학년 → 반 → 번호
            var sorted = students
                .OrderBy(s => s.Grade)
                .ThenBy(s => s.Class)
                .ThenBy(s => s.Number)
                .ToList();

            StudentList.LoadStudents(sorted);
            TxtStudentCount.Text = $"{sorted.Count}명";

            // 선택 초기화
            _selectedStudent = null;
            LogList?.Logs?.Clear();

            Debug.WriteLine($"[LessonActivityPage] 학생 로드 완료: {sorted.Count}명");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonActivityPage] 학생 로드 실패: {ex.Message}");
            ShowInfoBar($"학생 목록을 불러오는데 실패했습니다: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    /// <summary>
    /// CourseEnrollment 기반 학생 로드
    /// </summary>
    private async Task<List<Enrollment>> LoadStudentsByCourseEnrollmentAsync()
    {
        if (_selectedCourse == null) return new List<Enrollment>();

        using var enrollmentRepo = new CourseEnrollmentRepository(SchoolDatabase.DbPath);
        var courseEnrollments = await enrollmentRepo.GetByCourseAsync(_selectedCourse.No);

        if (courseEnrollments.Count == 0)
            return new List<Enrollment>();

        using var enrollmentService = new EnrollmentService();
        var students = new List<Enrollment>();

        foreach (var ce in courseEnrollments)
        {
            var enrollment = await enrollmentService.GetCurrentEnrollmentAsync(ce.StudentID);
            if (enrollment != null)
            {
                students.Add(enrollment);
            }
        }

        return students;
    }

    /// <summary>
    /// 강의실(Room)별 학생 로드 — CourseEnrollment.Room 필터
    /// </summary>
    private async Task<List<Enrollment>> LoadStudentsByRoomFilterAsync(string room)
    {
        if (_selectedCourse == null) return new List<Enrollment>();

        using var enrollmentRepo = new CourseEnrollmentRepository(SchoolDatabase.DbPath);
        var courseEnrollments = await enrollmentRepo.GetByCourseAsync(_selectedCourse.No);

        // Room 필터 적용
        var filtered = courseEnrollments
            .Where(ce => ce.Room == room)
            .ToList();

        if (filtered.Count == 0)
            return new List<Enrollment>();

        using var enrollmentService = new EnrollmentService();
        var students = new List<Enrollment>();

        foreach (var ce in filtered)
        {
            var enrollment = await enrollmentService.GetCurrentEnrollmentAsync(ce.StudentID);
            if (enrollment != null)
            {
                students.Add(enrollment);
            }
        }

        return students;
    }

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
    /// 수업 선택 변경
    /// </summary>
    private async void CBoxCourse_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxCourse.SelectedItem is Course course)
        {
            _selectedCourse = course;
            LoadRooms();
            await LoadStudentsAsync();
        }
    }

    /// <summary>
    /// 강의실 선택 변경
    /// </summary>
    private async void CBoxRoom_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxRoom.SelectedItem is string room)
        {
            _selectedRoom = room;
            await LoadStudentsAsync();
        }
    }

    /// <summary>
    /// 학생 선택 변경
    /// </summary>
    private async void OnStudentSelected(object? sender, Enrollment student)
    {
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
        await LoadCoursesAsync();
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

        dialog.Closed += async (s, args) =>
        {
            if (dialog.IsSuccess)
            {
                await LoadLogsAsync();
                ShowInfoBar($"{dialog.SavedLogs.Count}건이 일괄 저장되었습니다.", InfoBarSeverity.Success);
            }
        };
        dialog.Activate();
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
