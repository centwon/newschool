using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Board.Pages;
using NewSchool.Controls;
using NewSchool.Dialogs;
using NewSchool.Models;
using NewSchool.Services;

namespace NewSchool.Pages;

/// <summary>
/// 수업 홈 페이지
/// - 좌측: 과목/강의실 필터 + 학생 명렬
/// - 상단: 수업 기록 리스트 + 시간표
/// - 하단: 수업 자료실 (PostListPage 내장)
/// </summary>
public sealed partial class LessonHomePage : Page
{
    #region Fields

    private List<Course> _courses = [];
    private Course? _selectedCourse;
    private string _selectedRoom = string.Empty;

    // 자료실 (PostListPage 내장)
    private const string MaterialCategory = "수업";  // ← "수업자료"에서 "수업"으로 변경
    private PostListPage? _postListPage;

    #endregion

    #region Constructor

    public LessonHomePage()
    {
        InitializeComponent();
        Loaded += LessonHomePage_Loaded;
    }

    #endregion

    #region Page Events

    private async void LessonHomePage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadCoursesAsync();
        await LoadTimetableAsync();
        await LoadLessonTasksAsync();
        InitMaterialFrame();
    }

    /// <summary>
    /// 자료실 Frame 초기화 (PostListPage 내장)
    /// </summary>
    private void InitMaterialFrame()
    {
        MaterialFrame.Navigate(typeof(PostListPage), new PostListPageParameter
        {
            Category = MaterialCategory,
            Subject = _selectedCourse?.Subject ?? string.Empty,
            ViewMode = Board.Models.BoardViewMode.Card,
            AllowCategoryChange = false,
            AllowViewModeChange = true,
            IsEmbedded = true
        });
        _postListPage = MaterialFrame.Content as PostListPage;
    }

    #endregion

    #region 과목/강의실/학생 로드

    /// <summary>
    /// 교사의 과목 목록 로드
    /// </summary>
    private async Task LoadCoursesAsync()
    {
        try
        {
            using var courseService = new CourseService();
            _courses = await courseService.GetMyCoursesAsync();

            // "전체" 옵션 추가
            var allCourse = new Course { No = 0, Subject = "전체" };
            var items = new List<Course> { allCourse };
            items.AddRange(_courses);

            CBoxSubject.ItemsSource = items;
            CBoxSubject.SelectedIndex = 0;

            Debug.WriteLine($"[LessonHomePage] 과목 로드 완료: {_courses.Count}개");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonHomePage] 과목 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 과목 선택 변경
    /// </summary>
    private async void CBoxSubject_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxSubject.SelectedItem is not Course course) return;

        _selectedCourse = course.No > 0 ? course : null;

        // 강의실 목록 로드
        LoadRooms();

        // 수업 기록 로드
        await LoadLessonLogsAsync();

        // 자료실 Subject 변경
        await UpdateMaterialSubjectAsync();
    }

    /// <summary>
    /// 자료실 Subject 변경
    /// </summary>
    private async Task UpdateMaterialSubjectAsync()
    {
        if (_postListPage != null)
        {
            var subject = _selectedCourse?.Subject ?? string.Empty;
            await _postListPage.SetSubjectAsync(subject);
        }
    }

    /// <summary>
    /// 강의실(학급) 목록 로드
    /// </summary>
    private void LoadRooms()
    {
        if (_selectedCourse == null || _selectedCourse.RoomList.Count == 0)
        {
            CBoxRoom.ItemsSource = null;
            _selectedRoom = string.Empty;
            TxtStudentCount.Text = string.Empty;
            return;
        }

        var rooms = new List<string> { "전체" };
        rooms.AddRange(_selectedCourse.RoomList);
        CBoxRoom.ItemsSource = rooms;
        CBoxRoom.SelectedIndex = 0;
    }

    /// <summary>
    /// 강의실 선택 변경
    /// </summary>
    private async void CBoxRoom_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxRoom.SelectedItem is not string room) return;

        _selectedRoom = room == "전체" ? string.Empty : room;

        // 학생 로드 (학급 형식인 경우만)
        await LoadStudentsAsync();

        // 수업 기록 로드
        await LoadLessonLogsAsync();
    }

    /// <summary>
    /// 학생 목록 로드
    /// </summary>
    private async Task LoadStudentsAsync()
    {
        if (string.IsNullOrEmpty(_selectedRoom))
        {
            TxtStudentCount.Text = string.Empty;
            return;
        }

        // "학년-반" 형식 파싱
        var parts = _selectedRoom.Split('-');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var grade) ||
            !int.TryParse(parts[1], out var classNum))
        {
            TxtStudentCount.Text = string.Empty;
            return;
        }

        try
        {
            using var enrollmentService = new EnrollmentService();
            var enrollments = await enrollmentService.GetClassRosterAsync(
                Settings.SchoolCode.Value,
                Settings.WorkYear.Value,
                grade,
                classNum
            );

            TxtStudentCount.Text = $"({enrollments.Count}명)";
            Debug.WriteLine($"[LessonHomePage] 학생 로드: {grade}-{classNum}, {enrollments.Count}명");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonHomePage] 학생 로드 실패: {ex.Message}");
            TxtStudentCount.Text = string.Empty;
        }
    }

    #endregion

    #region 시간표 로드

    /// <summary>
    /// 시간표 로드
    /// </summary>
    private async Task LoadTimetableAsync()
    {
        try
        {
            await Timetable.LoadMyScheduleAsync();
            Debug.WriteLine("[LessonHomePage] 시간표 로드 완료");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonHomePage] 시간표 로드 실패: {ex.Message}");
        }
    }

    #endregion

    #region 할일 목록

    /// <summary>
    /// 수업 카테고리 할일 로드
    /// </summary>
    private async Task LoadLessonTasksAsync()
    {
        try
        {
            await LessonTaskList.LoadPendingAndFutureAsync();
            Debug.WriteLine("[LessonHomePage] 수업 할일 로드 완료");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonHomePage] 수업 할일 로드 실패: {ex.Message}");
        }
    }

    #endregion

    #region 수업 기록

    /// <summary>
    /// 수업 기록 로드
    /// </summary>
    private async Task LoadLessonLogsAsync()
    {
        var subject = _selectedCourse?.Subject ?? string.Empty;
        var room = _selectedRoom;

        await LessonLogList.LoadAsync(subject, room);
    }

    /// <summary>
    /// 수업 기록 선택됨 - Dialog로 편집
    /// </summary>
    private async void LessonLogList_LessonSelected(object sender, LessonLog lessonLog)
    {
        var dialog = new LessonLogEditDialog(lessonLog)
        {
            XamlRoot = XamlRoot
        };
        _ = await dialog.ShowAsync();

        // 삭제된 경우 또는 저장된 경우 새로고침
        if (dialog.IsDeleted || dialog.ResultLog != null)
        {
            await LoadLessonLogsAsync();
        }
    }

    /// <summary>
    /// 수업 기록 추가 요청 - Dialog로 추가
    /// </summary>
    private async void LessonLogList_AddRequested(object sender, EventArgs e)
    {
        if (_selectedCourse == null || _selectedCourse.No == 0)
        {
            await MessageBox.ShowAsync("과목을 먼저 선택해주세요.", "알림");
            return;
        }

        var dialog = new LessonLogEditDialog(
            Settings.User.Value,
            _selectedCourse.Subject,
            _selectedRoom
        )
        {
            XamlRoot = XamlRoot
        };
        _ = await dialog.ShowAsync();

        if (dialog.ResultLog != null)
        {
            await LoadLessonLogsAsync();
        }
    }

    #endregion
}
