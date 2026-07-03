using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;

namespace NewSchool.Controls;

/// <summary>
/// 과목(Course) · 강의실(Room) 선택 필터.
///
/// 확정 규칙:
///   - LoadAsync(year, semester, grade=0) 로 과목 목록을 (재)로드.
///     grade=0 이면 학년 구분 없이 현재 교사의 전 과목.
///     YearSemesterPicker 또는 ClassPicker.ClassChanged 에서 호출.
///   - 강의실은 선택된 Course.RoomList 에서 파싱 (DB 조회 없음).
///     강의실이 1개 이상이면 자동 표시, 0개이면 CBoxRoom 숨김.
///   - ShowRoom=false 이면 강의실 콤보 항상 숨김.
///   - 과목/강의실이 확정되면 수강생 조회 후 CourseChangedEventArgs 로 이벤트 발생.
///     강의실이 특정 값으로 선택되면(전체가 아니면) 수업 유형과 무관하게
///     CourseEnrollment.Room 필터로 조회. 강의실 "전체"(또는 미표시)면
///     학급 공통(Class) 유형은 Enrollment 학년 전체, 그 외(Selective/Club)는
///     CourseEnrollment 전체 수강자로 조회 — 기존 LessonActivityPage 로직 유지.
/// </summary>
public sealed partial class CoursePicker : UserControl
{
    // ── 상태 ────────────────────────────────────────────
    private bool _initialized;
    private bool _updating;
    private int _loadedYear;
    private int _loadedSemester;

    // ── 옵션 ────────────────────────────────────────────
    /// <summary>강의실 콤보 표시 여부 (기본 true)</summary>
    public bool ShowRoom { get; set; } = true;

    /// <summary>강의실 목록에 "전체" 항목 포함 여부 (기본 true)</summary>
    public bool IncludeAllRoom { get; set; } = true;

    // ── 현재 선택값 ─────────────────────────────────────
    public Course? SelectedCourse => CBoxCourse.SelectedItem as Course;
    public string? SelectedRoom => (CBoxRoom.SelectedItem as ComboBoxItem)?.Tag as string;

    // ── 이벤트 ──────────────────────────────────────────
    public event EventHandler<CourseChangedEventArgs>? CourseChanged;
    /// <summary>과목/수강생 조회 중 오류 발생 시 호출부가 안내를 표시할 수 있도록 전달</summary>
    public event EventHandler<string>? LoadError;

    // ── 생성자 ──────────────────────────────────────────
    public CoursePicker()
    {
        this.InitializeComponent();
        this.Loaded += OnLoaded;
    }

    // ── 초기화 ──────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        CBoxRoom.Visibility = ShowRoom ? Visibility.Visible : Visibility.Collapsed;
        await LoadAsync(Settings.WorkYear.Value, Settings.WorkSemester.Value);
    }

    /// <summary>
    /// 과목 목록을 (재)로드.
    /// YearSemesterPicker.YearSemesterChanged 또는 ClassPicker.ClassChanged 에서 호출.
    /// grade=0 이면 학년 구분 없이 현재 교사의 전 과목.
    /// </summary>
    public async Task LoadAsync(int year, int semester, int grade = 0)
    {
        _loadedYear = year;
        _loadedSemester = semester;

        _updating = true;
        try
        {
            var courses = await FetchCoursesAsync(year, semester, grade);

            // 이전 선택 과목 유지 시도
            var prev = SelectedCourse;

            CBoxCourse.ItemsSource = courses;

            if (prev != null)
            {
                var match = courses.FirstOrDefault(c => c.No == prev.No);
                CBoxCourse.SelectedItem = match;
            }
            if (CBoxCourse.SelectedItem is null && courses.Count > 0)
                CBoxCourse.SelectedIndex = 0;

            RefreshRoomCombo(SelectedCourse);
            _initialized = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoursePicker] LoadAsync 오류: {ex.Message}");
            LoadError?.Invoke(this, $"과목 목록을 불러오는데 실패했습니다: {ex.Message}");
        }
        finally { _updating = false; }

        await RaiseChangedAsync();
    }

    // ── 과목 목록 조회 ───────────────────────────────────

    private static async Task<List<Course>> FetchCoursesAsync(int year, int semester, int grade)
    {
        try
        {
            using var repo = new CourseRepository(SchoolDatabase.DbPath);
            var all = await repo.GetByTeacherAsync(Settings.User.Value, year, semester);
            return grade > 0 ? all.Where(c => c.Grade == grade).ToList() : all;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoursePicker] 과목 조회 오류: {ex.Message}");
            return new List<Course>();
        }
    }

    // ── 강의실 목록 구성 ─────────────────────────────────

    private void RefreshRoomCombo(Course? course)
    {
        CBoxRoom.Items.Clear();

        if (!ShowRoom || course == null || course.RoomList.Count == 0)
        {
            CBoxRoom.Visibility = Visibility.Collapsed;
            return;
        }

        if (IncludeAllRoom)
            CBoxRoom.Items.Add(new ComboBoxItem { Content = "전체", Tag = (string?)null });

        foreach (var r in course.RoomList)
            CBoxRoom.Items.Add(new ComboBoxItem { Content = r, Tag = r });

        CBoxRoom.Visibility = Visibility.Visible;
        CBoxRoom.SelectedIndex = 0;
    }

    // ── ComboBox 이벤트 ──────────────────────────────────

    private async void CBoxCourse_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updating) return;
        _updating = true;
        try
        {
            RefreshRoomCombo(SelectedCourse);
        }
        finally { _updating = false; }
        await RaiseChangedAsync();
    }

    private async void CBoxRoom_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updating) return;
        await RaiseChangedAsync();
    }

    // ── 수강생 조회 & 이벤트 발생 ───────────────────────

    private async Task RaiseChangedAsync()
    {
        var course = SelectedCourse;
        var students = course != null
            ? await FetchStudentsAsync(course, SelectedRoom)
            : new List<Enrollment>();

        CourseChanged?.Invoke(this, new CourseChangedEventArgs
        {
            Year = _loadedYear,
            Semester = _loadedSemester,
            Course = course,
            Room = SelectedRoom,
            Students = students.AsReadOnly(),
        });
    }

    /// <summary>
    /// 수강생 조회 — 기존 LessonActivityPage 로직 이식:
    ///   1) 특정 강의실 선택 시(전체 아님) → CourseEnrollment.Room 필터 (유형 무관)
    ///   2) 강의실 "전체" + 학급 공통(Class) 유형 → 해당 학년 전체 학생(Enrollment)
    ///   3) 강의실 "전체" + Selective/Club 유형 → CourseEnrollment 전체 수강자
    /// </summary>
    private async Task<List<Enrollment>> FetchStudentsAsync(Course course, string? room)
    {
        try
        {
            if (room != null)
            {
                return await LoadStudentsByRoomFilterAsync(course, room);
            }

            if (course.IsClassType)
            {
                using var enrollmentService = new EnrollmentService();
                return await enrollmentService.GetEnrollmentsAsync(
                    Settings.SchoolCode.Value, _loadedYear, 0, course.Grade);
            }

            return await LoadStudentsByCourseEnrollmentAsync(course);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoursePicker] 수강생 조회 오류: {ex.Message}");
            LoadError?.Invoke(this, $"수강생 목록을 불러오는데 실패했습니다: {ex.Message}");
            return new List<Enrollment>();
        }
    }

    private static async Task<List<Enrollment>> LoadStudentsByCourseEnrollmentAsync(Course course)
    {
        using var enrollmentRepo = new CourseEnrollmentRepository(SchoolDatabase.DbPath);
        var courseEnrollments = await enrollmentRepo.GetByCourseAsync(course.No);
        return await ResolveEnrollmentsAsync(courseEnrollments.Select(ce => ce.StudentID));
    }

    private static async Task<List<Enrollment>> LoadStudentsByRoomFilterAsync(Course course, string room)
    {
        using var enrollmentRepo = new CourseEnrollmentRepository(SchoolDatabase.DbPath);
        var courseEnrollments = await enrollmentRepo.GetByCourseAsync(course.No);

        var filtered = courseEnrollments.Where(ce => ce.Room == room);
        return await ResolveEnrollmentsAsync(filtered.Select(ce => ce.StudentID));
    }

    /// <summary>StudentID 목록을 한 번의 쿼리로 현재 학적으로 변환 (N+1 방지)</summary>
    private static async Task<List<Enrollment>> ResolveEnrollmentsAsync(IEnumerable<string> studentIds)
    {
        var idList = studentIds.Distinct().ToList();
        if (idList.Count == 0) return new List<Enrollment>();

        using var enrollmentService = new EnrollmentService();
        return await enrollmentService.GetCurrentEnrollmentsAsync(idList);
    }
}

/// <summary>CoursePicker 변경 이벤트 인자 — 수강생 목록 포함</summary>
public sealed class CourseChangedEventArgs : EventArgs
{
    public int Year { get; init; }
    public int Semester { get; init; }
    public Course? Course { get; init; }
    public string? Room { get; init; }
    public IReadOnlyList<Enrollment> Students { get; init; } = Array.Empty<Enrollment>();
}
