using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NewSchool.Models;
using NewSchool.Repositories;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace NewSchool.Controls;

/// <summary>
/// 기초 시간표 배치판 — 교사의 수업과 강의실을 펼쳐두고 <b>매주 반복되는</b> 주간 칸에 끌어다 놓는다.
///
/// 저장 대상은 <see cref="Lesson"/>(정기 수업)이다. 수업 홈의 내 시간표·오늘 화면·수업일지·
/// 시수 계산이 읽는 바로 그 표라서, 여기서 배치한 것이 곧 그 화면들에 나타난다.
/// 학급 시간표(ClassTimetable)는 별개 테이블이고 여기서 건드리지 않는다.
///
/// 특정 날짜만 바꾸는 일(휴강·교체·보강·대강)은 <see cref="WeeklyTimetableView"/> 가 맡는다 —
/// 같은 그리드에 두 뜻의 드래그를 얹으면 "그 주만" 바꿀 것을 "매주" 바꾸는 사고가 난다.
///
/// 마우스만 쓰는 화면이 되지 않도록 키보드 조작을 같이 넣었다 —
/// 방향키로 칸을 옮기고, Enter 로 고른 수업을 놓고, Delete 로 지우고, R 로 강의실을 돌린다.
/// 숫자키 1~9 는 왼쪽 수업 목록의 n번째를 고른다.
/// </summary>
public sealed partial class CourseTimetableBoard : UserControl
{
    private const int DayCount = 5;          // 월~금
    private const string DragCourse = "course";
    private const string DragRoom = "room";
    private const string DragSlot = "slot";

    private static readonly string[] DayNames = ["월", "화", "수", "목", "금"];

    private readonly ObservableCollection<Course> _courses = [];
    private readonly ObservableCollection<string> _rooms = [];
    private readonly List<Lesson> _lessons = [];
    private readonly Dictionary<(int Day, int Period), Border> _cells = [];

    private Course? _selectedCourse;
    private string? _selectedRoom;
    private int _year;
    private int _semester;
    private string _teacherId = string.Empty;
    private PeriodCounts _periods = PeriodCounts.Default;
    private int _maxPeriod = 7;

    private (int Day, int Period) _cursor = (1, 1);
    private bool _boardFocused;

    // 드래그 중인 것. WinUI 의 DataPackage 에 객체를 그대로 실을 수 없어서
    // 표식만 문자열로 싣고 실제 payload 는 여기에 둔다(앱 안에서만 끌기 때문에 안전하다).
    private Course? _dragCourse;
    private string? _dragRoom;
    private (int Day, int Period)? _dragFrom;

    /// <summary>배치가 바뀌었다 — 시수 탭처럼 Lesson 을 읽는 화면이 다시 계산해야 한다.</summary>
    public event EventHandler? PlacementChanged;

    /// <summary>왼쪽 팔레트에서 수업을 골랐다 — 페이지의 대상도 따라오게 한다.</summary>
    public event EventHandler<Course>? CourseSelected;

    public CourseTimetableBoard()
    {
        this.InitializeComponent();

        CourseListView.ItemsSource = _courses;
        CourseListView.ContainerContentChanging += OnCourseContainerChanging;
        RoomListView.ItemsSource = _rooms;
    }

    #region 로드

    /// <summary>
    /// 배치판을 채운다.
    /// </summary>
    public async Task LoadAsync(int year, int semester, IReadOnlyList<Course> courses, Course? selected)
    {
        _year = year;
        _semester = semester;
        _teacherId = Settings.User.Value;
        _periods = PeriodCounts.Parse(Settings.PeriodsPerDay.Value);
        _maxPeriod = Math.Max(1, Enumerable.Range(1, DayCount).Max(_periods.ForDay));

        _courses.Clear();
        foreach (var course in courses)
            _courses.Add(course);

        TxtCourseEmpty.Visibility = _courses.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        CourseListView.Visibility = _courses.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        await ReloadLessonsAsync();

        // 선택 유지 — 목록을 새로 채우면 SelectedItem 이 날아간다.
        var target = selected != null ? _courses.FirstOrDefault(c => c.No == selected.No) : null;
        CourseListView.SelectedItem = target ?? _courses.FirstOrDefault();
    }

    /// <summary>바깥(수업 콤보)에서 고른 수업을 배치판에도 맞춘다.</summary>
    public void SelectCourse(Course? course)
    {
        if (course == null)
        {
            CourseListView.SelectedItem = null;
            return;
        }

        var match = _courses.FirstOrDefault(c => c.No == course.No);
        if (match != null && !ReferenceEquals(match, CourseListView.SelectedItem))
            CourseListView.SelectedItem = match;
    }

    private async Task ReloadLessonsAsync()
    {
        _lessons.Clear();

        if (string.IsNullOrEmpty(_teacherId) || _year == 0 || _semester == 0)
        {
            BuildBoard();
            return;
        }

        try
        {
            using var repo = new LessonRepository(SchoolDatabase.DbPath);
            var lessons = await repo.GetTeacherScheduleAsync(_teacherId, _year, _semester);

            // 배치판은 개설된 수업만 보여준다. 지워진 수업의 잔여 배치가 있으면
            // 칸에 정체를 알 수 없는 블록으로 남기 때문에 여기서 걸러 둔다.
            var known = _courses.Select(c => c.No).ToHashSet();
            _lessons.AddRange(lessons.Where(l => known.Contains(l.Course)));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseTimetableBoard] 시간표 로드 실패: {ex.Message}");
            ShowWarning($"시간표를 불러오지 못했습니다.\n{ex.Message}");
        }

        BuildBoard();
        RefreshCourseBadges();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await ReloadLessonsAsync();
        }
        catch (Exception ex)
        {
            await UserErrorReporter.ReportAsync("시간표 다시 읽기", ex);
        }
    }

    #endregion

    #region 배치판 그리기

    private void BuildBoard()
    {
        BoardGrid.Children.Clear();
        BoardGrid.RowDefinitions.Clear();
        BoardGrid.ColumnDefinitions.Clear();
        _cells.Clear();

        BoardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        for (int day = 0; day < DayCount; day++)
            BoardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 108 });

        BoardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        // 과목명 줄 + 강의실 줄이 들어가므로 넉넉히 잡는다 — 낮으면 아래 줄이 잘린다.
        for (int period = 0; period < _maxPeriod; period++)
            BoardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });

        AddLabel("교시", 0, 0);
        for (int day = 1; day <= DayCount; day++)
            AddLabel(DayNames[day - 1], 0, day);

        for (int period = 1; period <= _maxPeriod; period++)
        {
            AddLabel($"{period}", period, 0);

            for (int day = 1; day <= DayCount; day++)
                AddCell(day, period);
        }

        UpdateStatus();
        UpdateCursorVisual();
    }

    private Style CellStyle(string key) => (Style)Resources[key];

    private void AddLabel(string text, int row, int column)
    {
        var border = new Border
        {
            Style = CellStyle("BoardHeaderCellStyle"),
            Child = new TextBlock { Text = text, Style = CellStyle("BoardHeaderTextStyle") }
        };

        Grid.SetRow(border, row);
        Grid.SetColumn(border, column);
        BoardGrid.Children.Add(border);
    }

    private void AddCell(int day, int period)
    {
        bool available = period <= _periods.ForDay(day);

        var border = new Border
        {
            AllowDrop = available,
            Tag = (day, period)
        };

        ApplyCellVisual(border, day, period, available);

        if (available)
        {
            // DragEnter 도 함께 받는다 — 칸 안에 자식(과목명·강의실)이 있으면
            // 포인터가 자식 위로 들어간 순간 DragOver 가 한동안 오지 않을 수 있다.
            border.DragEnter += OnCellDragOver;
            border.DragOver += OnCellDragOver;
            border.Drop += OnCellDrop;
            border.PointerPressed += OnCellPointerPressed;
            border.DragStarting += OnCellDragStarting;
        }

        Grid.SetRow(border, period);
        Grid.SetColumn(border, day);
        BoardGrid.Children.Add(border);
        _cells[(day, period)] = border;
    }

    private void ApplyCellVisual(Border border, int day, int period, bool available)
    {
        if (!available)
        {
            border.Style = CellStyle("BoardBlankCellStyle");
            border.Child = null;
            border.CanDrag = false;
            border.ContextFlyout = null;
            ToolTipService.SetToolTip(border, null);
            return;
        }

        var lesson = FindLesson(day, period);

        if (lesson == null)
        {
            border.Style = CellStyle("BoardEmptyCellStyle");
            border.Child = null;
            border.CanDrag = false;
            border.ContextFlyout = null;
            ToolTipService.SetToolTip(border, null);
            return;
        }

        var course = FindCourse(lesson.Course);

        border.Style = CellStyle("BoardFilledCellStyle");

        var panel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock
        {
            Text = course?.Subject ?? "(삭제된 수업)",
            Style = CellStyle("BoardCellSubjectStyle")
        });
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(lesson.Room) ? "강의실 미지정" : lesson.Room,
            Style = CellStyle("BoardCellRoomStyle")
        });

        border.Child = panel;
        border.CanDrag = true;
        border.ContextFlyout = BuildCellMenu(day, period, lesson, course);

        ToolTipService.SetToolTip(border,
            $"{course?.DisplayName ?? "삭제된 수업"}\n{lesson.ScheduleDisplay}\n{(string.IsNullOrWhiteSpace(lesson.Room) ? "강의실 미지정" : lesson.Room)}");
    }

    private MenuFlyout BuildCellMenu(int day, int period, Lesson lesson, Course? course)
    {
        var menu = new MenuFlyout();

        foreach (var room in course?.RoomList ?? [])
        {
            var item = new MenuFlyoutItem { Text = $"강의실: {room}", Tag = (day, period, room) };
            item.Click += OnCellMenuRoomClick;
            menu.Items.Add(item);
        }

        if (menu.Items.Count > 0)
            menu.Items.Add(new MenuFlyoutSeparator());

        var remove = new MenuFlyoutItem
        {
            Text = "배치 삭제",
            Icon = new FontIcon { Glyph = "" },
            Tag = (day, period)
        };
        remove.Click += OnCellMenuRemoveClick;
        menu.Items.Add(remove);

        return menu;
    }

    /// <summary>
    /// 칸 하나만 다시 그린다. 배치판 전체를 새로 만들면 컨텍스트 메뉴가 열린 채로 사라지고
    /// 키보드 포커스도 함께 날아간다.
    /// </summary>
    private void RefreshCell(int day, int period)
    {
        if (!_cells.TryGetValue((day, period), out var border)) return;

        ApplyCellVisual(border, day, period, period <= _periods.ForDay(day));
        UpdateCursorVisual();
    }

    private void UpdateCursorVisual()
    {
        foreach (var ((day, period), border) in _cells)
        {
            if (period > _periods.ForDay(day)) continue;

            if (_boardFocused && _cursor == (day, period))
            {
                border.BorderBrush = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
                border.BorderThickness = new Thickness(2);
            }
            else
            {
                // 로컬 값을 지워 스타일(ThemeResource)로 되돌린다 — 색을 직접 되돌려 넣으면
                // 테마를 바꿨을 때 이 칸들만 옛 색으로 남는다.
                border.ClearValue(Border.BorderBrushProperty);
                border.ClearValue(Border.BorderThicknessProperty);
            }
        }
    }

    #endregion

    #region 팔레트

    private void OnCourseSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var previous = _selectedCourse;
        _selectedCourse = CourseListView.SelectedItem as Course;

        _rooms.Clear();
        foreach (var room in _selectedCourse?.RoomList ?? [])
            _rooms.Add(room);

        bool hasRooms = _rooms.Count > 0;
        RoomListView.Visibility = hasRooms ? Visibility.Visible : Visibility.Collapsed;
        TxtRoomEmpty.Visibility = hasRooms ? Visibility.Collapsed : Visibility.Visible;
        TxtRoomEmpty.Text = _selectedCourse == null
            ? "수업을 고르면 그 수업의 강의실이 나옵니다"
            : "이 수업에 등록된 강의실이 없습니다 (수업 개설에서 추가)";

        RoomListView.SelectedIndex = hasRooms ? 0 : -1;
        _selectedRoom = hasRooms ? _rooms[0] : null;

        TxtClearBoardConfirm.Text = _selectedCourse == null
            ? "배치를 비울 수업을 먼저 고르세요."
            : $"'{_selectedCourse.Subject}' 의 평소 배치를 모두 지웁니다. 매주 반복되는 시간표가 사라집니다.";

        UpdateStatus();

        if (_selectedCourse != null && previous?.No != _selectedCourse.No)
            CourseSelected?.Invoke(this, _selectedCourse);
    }

    private void OnRoomSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedRoom = RoomListView.SelectedItem as string;
    }

    private void OnCourseContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        if (args.ItemContainer?.ContentTemplateRoot is not Grid grid) return;
        if (args.Item is not Course course) return;

        if (grid.Children.Count > 0 && grid.Children[0] is TextBlock indexText)
            indexText.Text = args.ItemIndex < 9 ? $"{args.ItemIndex + 1}" : "";

        if (grid.Children.Count > 2 && grid.Children[2] is Border badge && badge.Child is TextBlock badgeText)
            badgeText.Text = $"{CountPlaced(course.No)}/{course.Unit}";
    }

    private void RefreshCourseBadges()
    {
        // ContainerContentChanging 은 항목이 새로 그려질 때만 돈다. 배치를 바꾸면
        // 컨테이너를 직접 찾아 뱃지만 고쳐 준다(목록을 다시 채우면 선택이 날아간다).
        foreach (var course in _courses)
        {
            if (CourseListView.ContainerFromItem(course) is not ListViewItem item) continue;
            if (item.ContentTemplateRoot is not Grid grid) continue;
            if (grid.Children.Count > 2 && grid.Children[2] is Border badge && badge.Child is TextBlock badgeText)
                badgeText.Text = $"{CountPlaced(course.No)}/{course.Unit}";
        }

        UpdateStatus();
    }

    #endregion

    #region 드래그

    /// <summary>
    /// 끌기 표식을 싣는다.
    ///
    /// ⚠ <b>강의실 목록은 항목이 <c>string</c> 이라 ListView 가 DataPackage 에 텍스트를 이미 채워 둔다.</b>
    /// 거기에 <c>SetText</c> 를 한 번 더 부르면 예외가 나고, <c>DragItemsStarting</c> 안에서 난 예외는
    /// 끌기를 통째로 취소시킨다 — 강의실만 드롭이 안 먹던 원인이 이것이었다(수업 카드는 커스텀
    /// 객체라 자동으로 채워지는 게 없어서 멀쩡했다). 실제 payload 는 필드에 있으므로,
    /// 표식을 못 실어도 동작에는 지장이 없다.
    /// </summary>
    internal static void TrySetMarker(DataPackage data, string marker)
    {
        try
        {
            data.SetText(marker);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseTimetableBoard] 끌기 표식 생략: {ex.Message}");
        }
    }

    private void OnCourseDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        _dragCourse = e.Items.FirstOrDefault() as Course;
        _dragRoom = null;
        _dragFrom = null;

        if (_dragCourse == null)
        {
            e.Cancel = true;
            return;
        }

        TrySetMarker(e.Data, DragCourse);
        e.Data.RequestedOperation = DataPackageOperation.Copy;
    }

    private void OnRoomDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        _dragRoom = e.Items.FirstOrDefault() as string;
        _dragCourse = null;
        _dragFrom = null;

        if (string.IsNullOrEmpty(_dragRoom))
        {
            e.Cancel = true;
            return;
        }

        TrySetMarker(e.Data, DragRoom);
        e.Data.RequestedOperation = DataPackageOperation.Copy;
    }

    private void OnCellDragStarting(UIElement sender, DragStartingEventArgs e)
    {
        if (sender is not Border border || border.Tag is not ValueTuple<int, int> slot
            || FindLesson(slot.Item1, slot.Item2) == null)
        {
            e.Cancel = true;
            return;
        }

        _dragFrom = slot;
        _dragCourse = null;
        _dragRoom = null;

        TrySetMarker(e.Data, DragSlot);
        e.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void OnCellDragOver(object sender, DragEventArgs e)
    {
        if (sender is not Border border || border.Tag is not ValueTuple<int, int> slot)
            return;

        bool empty = FindLesson(slot.Item1, slot.Item2) == null;

        // 놓을 수 없는 자리에서 커서가 "가능"으로 보이면 놓아 보고서야 거절당한다.
        e.AcceptedOperation = (_dragCourse, _dragRoom, _dragFrom) switch
        {
            ({ }, _, _) => empty ? DataPackageOperation.Copy : DataPackageOperation.None,

            // 빈 칸에 강의실을 놓으면 "고른 수업을 이 강의실로" 로 받는다.
            // 강의실만으로는 못 놓는다고 거절하면, 끌어다 놓은 사람은 왜 안 되는지 알 수 없다.
            (_, { }, _) => !empty || _selectedCourse != null
                ? DataPackageOperation.Copy
                : DataPackageOperation.None,

            (_, _, { } from) => from == slot || empty
                ? DataPackageOperation.Move
                : DataPackageOperation.None,

            _ => DataPackageOperation.None
        };

        e.Handled = true;
    }

    private async void OnCellDrop(object sender, DragEventArgs e)
    {
        if (sender is not Border border || border.Tag is not ValueTuple<int, int> slot)
            return;

        e.Handled = true;
        var (day, period) = slot;

        var course = _dragCourse;
        var room = _dragRoom;
        var from = _dragFrom;

        _dragCourse = null;
        _dragRoom = null;
        _dragFrom = null;

        try
        {
            if (course != null)
            {
                await PlaceAsync(course, day, period, _selectedRoom);
            }
            else if (room != null)
            {
                if (FindLesson(day, period) != null)
                    await ChangeRoomAsync(day, period, room);
                else if (_selectedCourse != null)
                    await PlaceAsync(_selectedCourse, day, period, room);
                else
                    ShowWarning("빈 칸에 강의실만 놓으려면 왼쪽에서 수업을 먼저 고르세요.");
            }
            else if (from.HasValue)
            {
                await MoveAsync(from.Value, (day, period));
            }
        }
        catch (Exception ex)
        {
            await UserErrorReporter.ReportAsync("시간표 배치", ex);
        }
    }

    #endregion

    #region 배치 조작

    private async Task PlaceAsync(Course course, int day, int period, string? room)
    {
        if (period > _periods.ForDay(day))
        {
            ShowWarning($"{DayNames[day - 1]}요일은 {_periods.ForDay(day)}교시까지입니다.");
            return;
        }

        if (FindLesson(day, period) != null)
        {
            ShowWarning("이미 배치된 칸입니다. 먼저 비우거나 다른 칸에 놓으세요.");
            return;
        }

        // 주당 시수를 넘겨 배치하면 시수 탭의 계산이 조용히 부풀어 오른다.
        // 주당 시수를 안 적어 둔 수업(0)은 넘을 기준이 없으므로 막지 않는다.
        if (course.Unit > 0 && CountPlaced(course.No) >= course.Unit)
        {
            ShowWarning(
                $"'{course.Subject}' 는 주당 {course.Unit}시간인데 이미 {CountPlaced(course.No)}시간 배치됐습니다.\n" +
                "더 넣으려면 [수업 개설] 에서 주당 시수를 먼저 늘리세요.");
            return;
        }

        var lesson = new Lesson
        {
            Course = course.No,
            Teacher = _teacherId,
            Year = _year,
            Semester = _semester,
            DayOfWeek = day,
            Period = period,
            Grade = course.Grade,
            Room = room ?? course.RoomList.FirstOrDefault() ?? string.Empty
        };

        using var repo = new LessonRepository(SchoolDatabase.DbPath);
        if (await repo.CreateAsync(lesson) <= 0)
        {
            ShowWarning("배치를 저장하지 못했습니다.");
            return;
        }

        _lessons.Add(lesson);
        _cursor = (day, period);
        RefreshCell(day, period);
        RefreshCourseBadges();
        NotifyChanged();
    }

    private async Task MoveAsync((int Day, int Period) from, (int Day, int Period) to)
    {
        if (from == to) return;

        if (to.Period > _periods.ForDay(to.Day))
        {
            ShowWarning($"{DayNames[to.Day - 1]}요일은 {_periods.ForDay(to.Day)}교시까지입니다.");
            return;
        }

        if (FindLesson(to.Day, to.Period) != null)
        {
            ShowWarning("이미 배치된 칸입니다.");
            return;
        }

        var lesson = FindLesson(from.Day, from.Period);
        if (lesson == null) return;

        int oldDay = lesson.DayOfWeek;
        int oldPeriod = lesson.Period;

        lesson.DayOfWeek = to.Day;
        lesson.Period = to.Period;

        using var repo = new LessonRepository(SchoolDatabase.DbPath);
        if (!await repo.UpdateAsync(lesson))
        {
            // 되돌리지 않으면 화면만 옮겨진 채로 DB 와 어긋난다.
            lesson.DayOfWeek = oldDay;
            lesson.Period = oldPeriod;
            ShowWarning("배치를 옮기지 못했습니다.");
            return;
        }

        _cursor = to;
        RefreshCell(oldDay, oldPeriod);
        RefreshCell(to.Day, to.Period);
        NotifyChanged();
    }

    private async Task ChangeRoomAsync(int day, int period, string room)
    {
        var lesson = FindLesson(day, period);
        if (lesson == null)
        {
            ShowWarning("강의실은 수업이 있는 칸에만 놓을 수 있습니다.");
            return;
        }

        if (lesson.Room == room) return;

        string previous = lesson.Room;
        lesson.Room = room;

        using var repo = new LessonRepository(SchoolDatabase.DbPath);
        if (!await repo.UpdateAsync(lesson))
        {
            lesson.Room = previous;
            ShowWarning("강의실을 바꾸지 못했습니다.");
            return;
        }

        RefreshCell(day, period);
        NotifyChanged();
    }

    private async Task RemoveAsync(int day, int period)
    {
        var lesson = FindLesson(day, period);
        if (lesson == null) return;

        using var repo = new LessonRepository(SchoolDatabase.DbPath);
        if (!await repo.DeleteAsync(lesson.No))
        {
            ShowWarning("배치를 지우지 못했습니다.");
            return;
        }

        _lessons.Remove(lesson);
        RefreshCell(day, period);
        RefreshCourseBadges();
        NotifyChanged();
    }

    #endregion

    #region 메뉴 · 비우기

    private async void OnCellMenuRoomClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not ValueTuple<int, int, string> payload)
            return;

        var (day, period, room) = payload;

        try
        {
            await ChangeRoomAsync(day, period, room);
        }
        catch (Exception ex)
        {
            await UserErrorReporter.ReportAsync("강의실 변경", ex);
        }
    }

    private async void OnCellMenuRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not ValueTuple<int, int> slot)
            return;

        try
        {
            await RemoveAsync(slot.Item1, slot.Item2);
        }
        catch (Exception ex)
        {
            await UserErrorReporter.ReportAsync("배치 삭제", ex);
        }
    }

    private void OnClearBoardCancelClick(object sender, RoutedEventArgs e) => ClearBoardFlyout.Hide();

    private async void OnClearBoardConfirmClick(object sender, RoutedEventArgs e)
    {
        ClearBoardFlyout.Hide();

        if (_selectedCourse == null)
        {
            ShowWarning("배치를 비울 수업을 먼저 고르세요.");
            return;
        }

        try
        {
            using (var repo = new LessonRepository(SchoolDatabase.DbPath))
            {
                await repo.DeleteByCourseAsync(_selectedCourse.No);
            }

            await ReloadLessonsAsync();
            NotifyChanged();
        }
        catch (Exception ex)
        {
            await UserErrorReporter.ReportAsync("배치 비우기", ex);
        }
    }

    #endregion

    #region 키보드 · 포인터

    private void OnCellPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not ValueTuple<int, int> slot)
            return;

        _cursor = slot;
        BoardGrid.Focus(FocusState.Programmatic);
        UpdateCursorVisual();
    }

    private void OnBoardGotFocus(object sender, RoutedEventArgs e)
    {
        _boardFocused = true;
        UpdateCursorVisual();
    }

    private void OnBoardLostFocus(object sender, RoutedEventArgs e)
    {
        _boardFocused = false;
        UpdateCursorVisual();
    }

    private async void OnBoardKeyDown(object sender, KeyRoutedEventArgs e)
    {
        try
        {
            switch (e.Key)
            {
                case VirtualKey.Left:
                    MoveCursor(-1, 0);
                    e.Handled = true;
                    return;

                case VirtualKey.Right:
                    MoveCursor(1, 0);
                    e.Handled = true;
                    return;

                case VirtualKey.Up:
                    MoveCursor(0, -1);
                    e.Handled = true;
                    return;

                case VirtualKey.Down:
                    MoveCursor(0, 1);
                    e.Handled = true;
                    return;

                case VirtualKey.Enter:
                case VirtualKey.Space:
                    e.Handled = true;
                    if (_selectedCourse == null)
                    {
                        ShowWarning("배치할 수업을 왼쪽 목록에서 고르세요 (숫자키 1~9).");
                        return;
                    }
                    await PlaceAsync(_selectedCourse, _cursor.Day, _cursor.Period, _selectedRoom);
                    return;

                case VirtualKey.Delete:
                case VirtualKey.Back:
                    e.Handled = true;
                    await RemoveAsync(_cursor.Day, _cursor.Period);
                    return;

                case VirtualKey.R:
                    e.Handled = true;
                    await CycleRoomAsync();
                    return;
            }

            // 숫자키 1~9 로 수업 고르기
            if (e.Key >= VirtualKey.Number1 && e.Key <= VirtualKey.Number9)
            {
                int index = e.Key - VirtualKey.Number1;
                if (index < _courses.Count)
                {
                    CourseListView.SelectedIndex = index;
                    UpdateStatus();
                }
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            await UserErrorReporter.ReportAsync("시간표 편집", ex);
        }
    }

    private void MoveCursor(int dayDelta, int periodDelta)
    {
        int day = Math.Clamp(_cursor.Day + dayDelta, 1, DayCount);
        int period = Math.Clamp(_cursor.Period + periodDelta, 1, _maxPeriod);

        // 그 요일에 없는 교시로는 넘어가지 않는다 (월요일 7교시 같은 빈칸)
        if (period > _periods.ForDay(day))
        {
            if (periodDelta != 0) return;
            period = Math.Min(period, _periods.ForDay(day));
            if (period < 1) return;
        }

        _cursor = (day, period);
        UpdateCursorVisual();
        UpdateStatus();
    }

    /// <summary>커서 칸의 강의실을 그 수업의 강의실 목록 안에서 다음 것으로 돌린다.</summary>
    private async Task CycleRoomAsync()
    {
        var lesson = FindLesson(_cursor.Day, _cursor.Period);
        if (lesson == null)
        {
            ShowWarning("강의실을 바꿀 수업이 없습니다.");
            return;
        }

        var rooms = FindCourse(lesson.Course)?.RoomList ?? [];
        if (rooms.Count == 0)
        {
            ShowWarning("이 수업에 등록된 강의실이 없습니다 (수업 개설에서 추가).");
            return;
        }

        int current = rooms.IndexOf(lesson.Room);
        await ChangeRoomAsync(_cursor.Day, _cursor.Period, rooms[(current + 1) % rooms.Count]);
    }

    #endregion

    #region Helper

    private Lesson? FindLesson(int day, int period)
        => _lessons.FirstOrDefault(l => l.DayOfWeek == day && l.Period == period);

    private Course? FindCourse(int courseNo)
        => _courses.FirstOrDefault(c => c.No == courseNo);

    private int CountPlaced(int courseNo)
        => _lessons.Count(l => l.Course == courseNo);

    private void UpdateStatus()
    {
        int placed = _lessons.Count;
        int required = _courses.Sum(c => c.Unit);

        var text = $"배치 {placed}시간 / 주당 시수 합계 {required}시간";

        if (_selectedCourse != null)
        {
            int mine = CountPlaced(_selectedCourse.No);
            int diff = mine - _selectedCourse.Unit;
            var state = diff switch
            {
                0 => "맞음",
                < 0 => $"{-diff}시간 부족",
                _ => $"{diff}시간 초과"
            };

            text += $"   ·   {_selectedCourse.Subject}: {mine}/{_selectedCourse.Unit} ({state})";
        }

        text += $"   ·   커서 {DayNames[_cursor.Day - 1]} {_cursor.Period}교시";

        TxtBoardStatus.Text = text;
    }

    private void NotifyChanged()
    {
        UpdateStatus();
        PlacementChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowWarning(string message)
    {
        BoardInfoBar.Message = message;
        BoardInfoBar.IsOpen = true;
    }

    #endregion
}
