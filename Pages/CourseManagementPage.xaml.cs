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

namespace NewSchool.Pages;

/// <summary>
/// 수업 관리 — 수업 개설 · 단원 · 시수 · 진도 · 수업시간표(기초) · 주별 시간표를 한 페이지의 6개 탭으로 묶었다.
///
/// 여섯 탭은 같은 대상(학년도·학기·학년·수업)을 두고 서로 이어진다:
/// 수업을 개설하고 → 단원을 넣고 → 시간표에 배치하면 → 그 배치가 시수가 되고 → 진도를 단원별로 남긴다.
///
/// 필터는 <see cref="CourseScopeBar"/> 를 탭마다 하나씩 두고 페이지가 값을 맞춘다.
/// 페이지 헤더에 하나만 두던 때는 탭이 쓰는 컨트롤이 탭보다 위에 있어 위아래 순서가 뒤집혀 보였다.
///
/// 탭 내용은 처음 열릴 때 채운다(<see cref="_dirty"/>) — 여섯 화면을 페이지 진입 때 모두 읽으면
/// 쓰지도 않을 탭 때문에 첫 진입이 느려진다.
/// </summary>
public sealed partial class CourseManagementPage : Page
{
    private const int TabCourses = 0;
    private const int TabSections = 1;
    private const int TabHours = 2;
    private const int TabProgress = 3;
    private const int TabTimetable = 4;
    private const int TabWeekly = 5;
    private const int TabCount = 6;

    /// <summary>학년 필터를 거치지 않은, 그 학년도·학기의 모든 수업</summary>
    private List<Course> _allCourses = [];

    /// <summary>
    /// 학년 필터를 적용한 목록 — <b>수업 개설 탭의 카드 목록에만</b> 쓴다.
    ///
    /// ⚠ 다른 탭에 이 목록을 주면 안 된다. 배치판·주별 표는 받은 목록으로 <c>Lesson</c> 을 거르는데,
    /// 학년이 걸려 있으면 <b>다른 학년의 배치가 화면에서 사라진다</b>. 빈 것처럼 보이는 칸에
    /// 수업을 놓으면 같은 교시에 두 수업이 겹쳐 들어간다.
    /// </summary>
    private readonly ObservableCollection<Course> _courses = [];

    private Course? _selectedCourse;

    private CourseScopeBar[] _bars = [];

    /// <summary>탭별 "다시 채워야 함" 표시</summary>
    private readonly bool[] _dirty = new bool[TabCount];

    /// <summary>바·목록·탭이 서로를 다시 부르는 것을 막는다</summary>
    private bool _syncing;

    private bool _initialized;

    /// <summary>지금 <see cref="_allCourses"/> 에 담긴 학년도·학기</summary>
    private int _loadedYear;
    private int _loadedSemester;

    public CourseManagementPage()
    {
        this.InitializeComponent();

        CourseListView.ItemsSource = _courses;

        _bars = [ScopeCourses, ScopeSections, ScopeHours, ScopeProgress, ScopeTimetable, ScopeWeekly];
        foreach (var bar in _bars)
        {
            bar.ScopeChanged += OnScopeChanged;
            bar.CourseChanged += OnBarCourseChanged;
        }

        // 시간표 배치가 바뀌면 주차별 시수가 같이 틀어진다.
        TimetableBoard.PlacementChanged += OnPlacementChanged;
        // 배치판 팔레트에서 고른 수업도 페이지의 대상으로 삼는다.
        TimetableBoard.CourseSelected += OnTabCourseSelected;

        Loaded += OnPageLoaded;
    }

    #region 초기화 · 수업 목록

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        await InitializeScopeAsync();
        await LoadCoursesAsync();
    }

    /// <summary>
    /// 학년도 목록을 <b>한 번만</b> 읽어 모든 바에 나눠 준다.
    /// 바마다 스스로 읽게 하면 탭 수만큼 같은 질의가 돌고, 바들끼리 목록이 어긋날 수도 있다.
    /// </summary>
    private async Task InitializeScopeAsync()
    {
        var years = new List<int>();

        try
        {
            using var service = new CourseService();
            years = await service.GetDistinctCourseYearsAsync(Settings.User.Value);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseManagementPage] 학년도 조회 실패: {ex.Message}");
        }

        int workYear = Settings.WorkYear.Value > 0 ? Settings.WorkYear.Value : DateTime.Today.Year;
        if (!years.Contains(workYear))
            years.Add(workYear);

        years = years.OrderByDescending(y => y).ToList();

        int semester = Settings.WorkSemester.Value > 0 ? Settings.WorkSemester.Value : 1;

        foreach (var bar in _bars)
        {
            bar.SetYears(years, workYear);
            bar.SetSemester(semester);
            bar.SetGrades([], 0);
        }
    }

    private async Task LoadCoursesAsync()
    {
        int year = ScopeCourses.Year;
        int semester = ScopeCourses.Semester;

        if (year == 0 || semester == 0) return;

        string teacherId = Settings.User.Value;
        if (string.IsNullOrEmpty(teacherId))
        {
            await MessageBox.ShowAsync("교사 정보를 찾을 수 없습니다.", "오류");
            ShowEmptyState();
            return;
        }

        try
        {
            ShowLoadingState();

            using var repo = new CourseRepository(SchoolDatabase.DbPath);
            _allCourses = await repo.GetByTeacherAsync(teacherId, year, semester);

            _loadedYear = year;
            _loadedSemester = semester;
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"수업 목록 조회 중 오류가 발생했습니다.\n{ex.Message}", "오류");
            ShowEmptyState();
            return;
        }

        // 학년 목록은 실제로 개설된 수업에서 뽑는다 — 학생 명부의 학년이 아니라
        // "내가 이 학기에 들어가는 학년" 이 이 화면의 관심사다.
        var grades = _allCourses.Select(c => c.Grade).Distinct().OrderBy(g => g).ToList();
        int grade = grades.Contains(ScopeCourses.Grade) ? ScopeCourses.Grade : 0;

        ApplyGradeFilter(grades, grade);
        UpdateUI();

        InvalidateTabs();
        await ActivateCurrentTabAsync();
    }

    /// <summary>
    /// 학년 필터를 적용해 탭들이 볼 목록을 만들고, 모든 바를 그 상태로 맞춘다.
    /// </summary>
    private void ApplyGradeFilter(IReadOnlyList<int> grades, int grade)
    {
        var filtered = grade > 0
            ? _allCourses.Where(c => c.Grade == grade).ToList()
            : _allCourses;

        int previous = _selectedCourse?.No ?? -1;

        _syncing = true;
        try
        {
            _courses.Clear();
            foreach (var course in filtered)
                _courses.Add(course);

            // 목록을 새로 채우면 선택이 날아간다 — 같은 수업이 남아 있으면 되돌려 놓는다.
            // 대상은 전체에서 고른다: 학년을 걸어도 다른 탭의 대상까지 바뀌면 안 된다.
            _selectedCourse = _allCourses.FirstOrDefault(c => c.No == previous) ?? _allCourses.FirstOrDefault();
            CourseListView.SelectedItem = _courses.FirstOrDefault(c => c.No == _selectedCourse?.No);

            // 수업 콤보에는 거르지 않은 전체를 준다 — 학년 필터는 개설 탭의 카드 목록만 좁힌다.
            foreach (var bar in _bars)
            {
                bar.SetGrades(grades, grade);
                bar.SetCourses(_allCourses, _selectedCourse);
            }
        }
        finally { _syncing = false; }
    }

    #endregion

    #region 필터 바

    private async void OnScopeChanged(object? sender, EventArgs e)
    {
        if (_syncing) return;
        if (sender is not CourseScopeBar source) return;

        int year = source.Year;
        int semester = source.Semester;
        int grade = source.Grade;

        // 고른 바가 아닌 나머지를 먼저 맞춘다 — 그래야 어느 탭으로 넘어가도 같은 값이 보인다.
        // 학년도 목록은 모든 바가 같은 것을 들고 있으므로 선택만 옮기면 된다.
        _syncing = true;
        try
        {
            foreach (var bar in _bars)
            {
                if (ReferenceEquals(bar, source)) continue;
                bar.SelectYear(year);
                bar.SetSemester(semester);
            }
        }
        finally { _syncing = false; }

        // 학년만 바뀐 경우는 이미 읽어 둔 목록을 거르기만 하면 된다 — DB 를 다시 칠 이유가 없다.
        if (year == _loadedYear && semester == _loadedSemester)
        {
            var grades = _allCourses.Select(c => c.Grade).Distinct().OrderBy(g => g).ToList();
            ApplyGradeFilter(grades, grade);
            UpdateUI();
            InvalidateTabs();
            await ActivateCurrentTabAsync();
            return;
        }

        await LoadCoursesAsync();
    }

    private void OnBarCourseChanged(object? sender, Course course) => OnTabCourseSelected(sender, course);

    /// <summary>
    /// 어느 탭에서 수업을 고르든 페이지의 대상을 옮긴다.
    /// 고른 탭 자신은 이미 그 수업을 그리고 있으므로 다시 채우지 않는다.
    /// </summary>
    private async void OnTabCourseSelected(object? sender, Course course)
    {
        if (_syncing) return;

        SetSelectedCourse(course);

        if (sender is CourseTimetableBoard)
            _dirty[TabTimetable] = false;

        await ActivateCurrentTabAsync();
    }

    #endregion

    #region 탭 전환

    private async void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        // Pivot 은 항목이 붙는 순간(=InitializeComponent 도중) 첫 선택을 알린다.
        // 그 시점엔 아직 만들어지지 않은 이름이 있을 수 있다.
        if (!_initialized) return;

        await ActivateCurrentTabAsync();
    }

    private void OnCourseListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        if (CourseListView.SelectedItem is not Course course) return;

        SetSelectedCourse(course);
    }

    /// <summary>
    /// 대상 수업을 바꾸고, 이 수업에 매달린 탭들을 "다시 채워야 함" 으로 표시한다.
    /// </summary>
    private void SetSelectedCourse(Course course)
    {
        if (_selectedCourse != null && _selectedCourse.No == course.No)
            return;

        _selectedCourse = course;

        _syncing = true;
        try
        {
            CourseListView.SelectedItem = course;
            foreach (var bar in _bars)
                bar.SelectCourse(course);
        }
        finally { _syncing = false; }

        _dirty[TabSections] = true;
        _dirty[TabHours] = true;
        _dirty[TabProgress] = true;
        _dirty[TabTimetable] = true;
        _dirty[TabWeekly] = true;
    }

    private void InvalidateTabs()
    {
        for (int i = 0; i < TabCount; i++)
            _dirty[i] = true;
    }

    /// <summary>
    /// 지금 보이는 탭이 낡았으면 채운다.
    /// </summary>
    private async Task ActivateCurrentTabAsync()
    {
        int index = TabsPivot.SelectedIndex;
        if (index < 0 || index >= TabCount) return;
        if (!_dirty[index]) return;

        _dirty[index] = false;

        switch (index)
        {
            case TabSections:
                await SectionView.LoadAsync(_selectedCourse);
                break;

            case TabHours:
                await HoursView.LoadAsync(ScopeHours.Year, ScopeHours.Semester, _selectedCourse);
                break;

            case TabProgress:
                await ProgressView.LoadAsync(_selectedCourse);
                break;

            case TabTimetable:
                await TimetableBoard.LoadAsync(
                    ScopeTimetable.Year, ScopeTimetable.Semester, _allCourses, _selectedCourse);
                break;

            case TabWeekly:
                await WeeklyView.LoadAsync(
                    ScopeWeekly.Year, ScopeWeekly.Semester, _allCourses, _selectedCourse);
                break;
        }
    }

    /// <summary>
    /// 시간표 배치가 바뀌면 시수 탭의 숫자가 낡는다. 지금 시수 탭을 보고 있으면 바로 고치고,
    /// 아니면 다음에 열 때 다시 계산하도록 표시만 해 둔다.
    /// </summary>
    private async void OnPlacementChanged(object? sender, EventArgs e)
    {
        if (TabsPivot.SelectedIndex == TabHours)
            await HoursView.RefreshAsync();
        else
            _dirty[TabHours] = true;
    }

    #endregion

    #region 수업 개설 탭

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (ScopeCourses.Year == 0 || ScopeCourses.Semester == 0)
        {
            await MessageBox.ShowAsync("학년도와 학기를 먼저 선택해주세요.", "알림");
            return;
        }

        var dialog = new CourseEditDialog(
            Settings.SchoolCode.Value,
            Settings.User.Value,
            ScopeCourses.Year,
            ScopeCourses.Semester)
        {
            XamlRoot = this.XamlRoot
        };

        if (await MessageBox.ShowDialogAsync(dialog) == ContentDialogResult.Primary)
            await LoadCoursesAsync();
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not Course course) return;

        var dialog = new CourseEditDialog(course) { XamlRoot = this.XamlRoot };

        if (await MessageBox.ShowDialogAsync(dialog) == ContentDialogResult.Primary)
            await LoadCoursesAsync();
    }

    private async void OnEnrollClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not Course course) return;

        var dialog = new CourseEnrollmentDialog(course) { XamlRoot = this.XamlRoot };
        await MessageBox.ShowDialogAsync(dialog);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not Course course) return;

        var confirmed = await MessageBox.ShowConfirmAsync(
            $"'{course.Subject}' 수업을 삭제하시겠습니까?\n연결된 시간표 배치·단원·진도도 함께 삭제됩니다.",
            "수업 삭제", "삭제", "취소");
        if (!confirmed) return;

        try
        {
            using var repo = new CourseRepository(SchoolDatabase.DbPath);
            bool success = await repo.DeleteAsync(course.No);

            if (!success)
            {
                await MessageBox.ShowAsync("수업 삭제에 실패했습니다.", "오류");
                return;
            }

            // 지워진 수업이 다른 탭의 대상으로 남아 있으면 없는 수업을 계속 읽는다.
            if (_selectedCourse?.No == course.No)
                _selectedCourse = null;

            await MessageBox.ShowAsync("수업이 삭제되었습니다.", "완료");
            await LoadCoursesAsync();
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"수업 삭제 중 오류가 발생했습니다.\n{ex.Message}", "오류");
        }
    }

    #endregion

    #region UI 상태 관리

    private void UpdateUI()
    {
        bool hasCourses = _courses.Count > 0;

        LoadingState.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = hasCourses ? Visibility.Collapsed : Visibility.Visible;
        CourseListContainer.Visibility = hasCourses ? Visibility.Visible : Visibility.Collapsed;

        TxtCourseCount.Text = $"총 {_courses.Count}개 수업";
    }

    private void ShowLoadingState()
    {
        LoadingState.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        CourseListContainer.Visibility = Visibility.Collapsed;
    }

    private void ShowEmptyState()
    {
        LoadingState.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Visible;
        CourseListContainer.Visibility = Visibility.Collapsed;
    }

    #endregion
}
