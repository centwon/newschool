using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NewSchool.Helpers;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace NewSchool.Controls;

/// <summary>
/// 주별 시간표 — <b>날짜가 행, 교시가 열</b>인 표. 한 번에 3주치를 보여 준다.
///
/// 기초 시간표(<see cref="CourseTimetableBoard"/>)와 일부러 행·열을 반대로 뒀다.
/// 같은 모양이면 "그 주만" 바꿀 것을 "매주" 바꾸는 사고가 나기 때문이다.
/// 여기서 손대는 것은 <see cref="LessonChange"/> 뿐이고 <see cref="Lesson"/> 은 그대로 있으므로,
/// 시수 계산과 교사 시간표의 "평소" 기준은 흔들리지 않는다.
///
/// ⚠ 이 표는 <c>기초 + 그 날 변경</c> 으로 <b>볼 때마다 계산</b>한다. 나중에 기초를 고치면
/// 지난 주를 열어도 새 기초로 그려진다. 주마다 사본을 뜨면 막을 수 있지만 그건 연간계획이
/// 죽은 패턴(매주 입력 요구·보상은 나중)이라 택하지 않았다 — 지나간 날 실제로 무엇을 했는지는
/// 수업일지가 답한다. 그래서 기본으로 <b>이번 주부터 앞으로</b> 3주를 보여 준다.
/// </summary>
public sealed partial class WeeklyTimetableView : UserControl
{
    private const int WeekCount = 3;
    private const int DayCount = 5;          // 월~금
    private const string DragSlot = "weekslot";

    private static readonly string[] DayNames = ["월", "화", "수", "목", "금"];

    private readonly List<Course> _courses = [];
    private readonly List<Lesson> _lessons = [];
    private List<SchoolSchedule> _schedules = [];

    /// <summary>학교의 학년 수 (0 = 모름 → 학사일정 판정이 종전 기준으로 돈다)</summary>
    private int _gradeCount;

    /// <summary>(날짜, 교시) → 변경</summary>
    private readonly Dictionary<(DateTime Date, int Period), LessonChange> _changes = [];

    /// <summary>표에 그린 칸 — (날짜, 교시) → Border</summary>
    private readonly Dictionary<(DateTime Date, int Period), Border> _cells = [];

    /// <summary>표에 그린 날짜들 (위에서 아래 순서)</summary>
    private readonly List<DateTime> _dates = [];

    private Course? _selectedCourse;
    private int _year;
    private int _semester;
    private string _teacherId = string.Empty;
    private PeriodCounts _periods = PeriodCounts.Default;
    private int _maxPeriod = 7;
    private int _futureChangeCount;

    /// <summary>보고 있는 구간의 첫 월요일</summary>
    private DateTime _firstMonday;

    private (DateTime Date, int Period)? _cursor;
    private bool _focused;
    private (DateTime Date, int Period)? _dragFrom;

    public WeeklyTimetableView()
    {
        this.InitializeComponent();
    }

    #region 로드

    /// <summary>
    /// 학년도·학기와 수업 목록을 받는다.
    /// </summary>
    public async Task LoadAsync(int year, int semester, IReadOnlyList<Course> courses, Course? selected)
    {
        bool scopeChanged = _year != year || _semester != semester;

        _year = year;
        _semester = semester;
        _teacherId = Settings.User.Value;
        _periods = PeriodCounts.Parse(Settings.PeriodsPerDay.Value);
        _maxPeriod = Math.Max(1, Enumerable.Range(1, DayCount).Max(_periods.ForDay));

        _courses.Clear();
        _courses.AddRange(courses);
        _selectedCourse = selected;

        if (scopeChanged || _firstMonday == default)
            _firstMonday = MondayOf(DateTime.Today);

        if (scopeChanged || _schedules.Count == 0)
            await LoadSchedulesAsync();

        await ReloadAsync();
    }

    private static DateTime MondayOf(DateTime date)
    {
        var monday = date.Date;
        while (monday.DayOfWeek != DayOfWeek.Monday)
            monday = monday.AddDays(-1);
        return monday;
    }

    private async Task LoadSchedulesAsync()
    {
        _schedules = [];

        var schoolCode = Settings.SchoolCode.Value;
        if (string.IsNullOrEmpty(schoolCode) || _year == 0) return;

        try
        {
            using var repo = new SchoolScheduleRepository(SchoolDatabase.DbPath);
            _schedules = await repo.GetBySchoolYearAsync(schoolCode, _year);

            // 학년 수를 알아야 "1·2학년만 수련회" 같은 날을 그 학년의 휴강 사유로 잡는다.
            _gradeCount = await SchoolProfile.GetGradeCountAsync();
        }
        catch (Exception ex)
        {
            // 학사일정이 없으면 휴업일 표시만 빠질 뿐 표는 그대로 쓸 수 있다.
            Debug.WriteLine($"[WeeklyTimetableView] 학사일정 로드 실패: {ex.Message}");
        }
    }

    private async Task ReloadAsync()
    {
        _lessons.Clear();
        _changes.Clear();

        if (string.IsNullOrEmpty(_teacherId) || _year == 0 || _semester == 0)
        {
            BuildTable();
            return;
        }

        try
        {
            using (var repo = new LessonRepository(SchoolDatabase.DbPath))
            {
                var lessons = await repo.GetTeacherScheduleAsync(_teacherId, _year, _semester);
                var known = _courses.Select(c => c.No).ToHashSet();
                _lessons.AddRange(lessons.Where(l => known.Contains(l.Course)));
            }

            using (var repo = new LessonChangeRepository(SchoolDatabase.DbPath))
            {
                var last = _firstMonday.AddDays(WeekCount * 7 - 1);
                foreach (var change in await repo.GetRangeAsync(_teacherId, _firstMonday, last))
                    _changes[(change.Date.Date, change.Period)] = change;

                var (_, end) = WeeklyHoursCalculator.DefaultSemesterRange(_year, _semester);
                _futureChangeCount = (await repo.GetRangeAsync(_teacherId, DateTime.Today, end)).Count;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WeeklyTimetableView] 로드 실패: {ex.Message}");
            ShowWarning($"주별 시간표를 불러오지 못했습니다.\n{ex.Message}");
        }

        BuildTable();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
        => await RunAsync(ReloadAsync, "다시 읽기");

    private async void OnPreviousClick(object sender, RoutedEventArgs e)
    {
        _firstMonday = _firstMonday.AddDays(-7);
        await RunAsync(ReloadAsync, "주 이동");
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        _firstMonday = _firstMonday.AddDays(7);
        await RunAsync(ReloadAsync, "주 이동");
    }

    private async void OnTodayClick(object sender, RoutedEventArgs e)
    {
        _firstMonday = MondayOf(DateTime.Today);
        await RunAsync(ReloadAsync, "이번 주로");
    }

    private async void OnChangeListClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Dialogs.LessonChangeDialog(_year, _semester) { XamlRoot = this.XamlRoot };
        await MessageBox.ShowDialogAsync(dialog);

        // 창에서 되돌린 변경이 표에도 반영돼야 한다.
        await RunAsync(ReloadAsync, "변경 목록 반영");
    }

    private async Task RunAsync(Func<Task> action, string context)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await UserErrorReporter.ReportAsync(context, ex);
        }
    }

    #endregion

    #region 칸 내용 풀기

    /// <summary>한 칸이 그 날 실제로 무엇인가 — 기초 위에 그 날 변경을 얹은 결과.</summary>
    private readonly record struct SlotView(string Subject, string Room, LessonChangeKind Kind, int CourseNo)
    {
        public bool IsBlank => string.IsNullOrEmpty(Subject);

        /// <summary>맞바꾸기에서 옮겨 갈 내용 (휴강은 "없음"으로 친다)</summary>
        public bool Movable => !IsBlank && Kind != LessonChangeKind.Cancelled;
    }

    private SlotView Resolve(DateTime date, int period)
    {
        int day = SchoolCalendar.ToLessonDayOfWeek(date);
        var lesson = _lessons.FirstOrDefault(l => l.DayOfWeek == day && l.Period == period);
        var baseCourse = lesson != null ? FindCourse(lesson.Course) : null;

        if (!_changes.TryGetValue((date.Date, period), out var change))
        {
            return new SlotView(
                baseCourse?.Subject ?? string.Empty,
                lesson?.Room ?? string.Empty,
                LessonChangeKind.None,
                lesson?.Course ?? 0);
        }

        if (change.IsCancellation)
        {
            // 휴강은 무엇이 빠졌는지 보이도록 원래 수업을 그대로 들고 있는다.
            return new SlotView(
                baseCourse?.Subject ?? string.Empty,
                lesson?.Room ?? string.Empty,
                LessonChangeKind.Cancelled,
                lesson?.Course ?? 0);
        }

        var kind = change.IsSubstitute
            ? LessonChangeKind.Substitute
            : lesson != null ? LessonChangeKind.Replaced : LessonChangeKind.Added;

        return new SlotView(change.Subject, change.Room ?? string.Empty, kind, change.CourseNo ?? 0);
    }

    #endregion

    #region 표 그리기

    private void BuildTable()
    {
        WeekGrid.Children.Clear();
        WeekGrid.RowDefinitions.Clear();
        WeekGrid.ColumnDefinitions.Clear();
        _cells.Clear();
        _dates.Clear();

        WeekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        for (int period = 0; period < _maxPeriod; period++)
            WeekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 104 });

        // 머리: 날짜 | 1 | 2 | … | 7
        WeekGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddHeader("날짜", 0, 0);
        for (int period = 1; period <= _maxPeriod; period++)
            AddHeader($"{period}", 0, period);

        int row = 1;

        for (int week = 0; week < WeekCount; week++)
        {
            var monday = _firstMonday.AddDays(week * 7);

            WeekGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddWeekBand(monday, row++);

            for (int day = 0; day < DayCount; day++)
            {
                var date = monday.AddDays(day);

                // 과목명 줄 + 강의실 줄이 들어가므로 넉넉히 잡는다 — 낮으면 아래 줄이 잘린다.
                WeekGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });
                AddDateCell(date, row);

                for (int period = 1; period <= _maxPeriod; period++)
                    AddSlotCell(date, period, row);

                _dates.Add(date);
                row++;
            }
        }

        _cursor ??= _dates.FirstOrDefault(d => d >= DateTime.Today) is var d0 && d0 != default
            ? (d0, 1)
            : null;

        TxtRange.Text = $"{_firstMonday:M/d} ~ {_firstMonday.AddDays(WeekCount * 7 - 3):M/d}";

        UpdateCursorVisual();
        UpdateStatus();
    }

    private Style CellStyle(string key) => (Style)Resources[key];

    private void AddHeader(string text, int row, int column)
    {
        var border = new Border
        {
            Style = CellStyle("WeekHeaderCellStyle"),
            Child = new TextBlock { Text = text, Style = CellStyle("WeekHeaderTextStyle") }
        };

        Grid.SetRow(border, row);
        Grid.SetColumn(border, column);
        WeekGrid.Children.Add(border);
    }

    private void AddWeekBand(DateTime monday, int row)
    {
        int count = _changes.Keys.Count(k => k.Date >= monday && k.Date <= monday.AddDays(4));

        var text = $"{monday:M/d}(월) ~ {monday.AddDays(4):M/d}(금)";
        if (count > 0) text += $"   ·   변경 {count}건";
        if (monday == MondayOf(DateTime.Today)) text += "   ·   이번 주";

        var border = new Border
        {
            Style = CellStyle("WeekBandStyle"),
            Child = new TextBlock { Text = text, Style = CellStyle("WeekBandTextStyle") }
        };

        Grid.SetRow(border, row);
        Grid.SetColumn(border, 0);
        Grid.SetColumnSpan(border, _maxPeriod + 1);
        WeekGrid.Children.Add(border);
    }

    private void AddDateCell(DateTime date, int row)
    {
        string? reason = OffDayReason(date);
        bool off = reason != null;

        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var label = $"{date:M/d}({DayNames[SchoolCalendar.ToLessonDayOfWeek(date) - 1]})";
        if (date == DateTime.Today) label += " · 오늘";

        panel.Children.Add(new TextBlock { Text = label, Style = CellStyle("WeekDateTextStyle") });

        if (off)
            panel.Children.Add(new TextBlock { Text = reason, Style = CellStyle("WeekReasonTextStyle") });

        var border = new Border
        {
            Style = CellStyle(off ? "WeekOffDateCellStyle" : "WeekDateCellStyle"),
            Child = panel
        };

        if (off) ToolTipService.SetToolTip(border, reason);

        Grid.SetRow(border, row);
        Grid.SetColumn(border, 0);
        WeekGrid.Children.Add(border);
    }

    /// <summary>그 날 수업이 없는 사유 (없으면 null)</summary>
    private string? OffDayReason(DateTime date)
    {
        foreach (var schedule in _schedules)
        {
            if (schedule == null || schedule.IsDeleted) continue;
            if (schedule.AA_YMD.Date != date.Date) continue;

            if (SchoolCalendar.IsNonTeachingDay(schedule))
                return string.IsNullOrWhiteSpace(schedule.EVENT_NM) ? schedule.SBTR_DD_SC_NM : schedule.EVENT_NM;

            if (SchoolCalendar.IsGradeOnlyEvent(schedule, _selectedCourse?.Grade ?? 0, _gradeCount))
                return schedule.EVENT_NM;
        }

        return null;
    }

    private void AddSlotCell(DateTime date, int period, int row)
    {
        int day = SchoolCalendar.ToLessonDayOfWeek(date);
        bool available = period <= _periods.ForDay(day);

        var border = new Border
        {
            AllowDrop = available,
            Tag = (date, period)
        };

        ApplySlotVisual(border, date, period, available);

        if (available)
        {
            border.DragEnter += OnSlotDragOver;
            border.DragOver += OnSlotDragOver;
            border.Drop += OnSlotDrop;
            border.DragStarting += OnSlotDragStarting;
            border.PointerPressed += OnSlotPointerPressed;
        }

        Grid.SetRow(border, row);
        Grid.SetColumn(border, period);
        WeekGrid.Children.Add(border);
        _cells[(date.Date, period)] = border;
    }

    private void ApplySlotVisual(Border border, DateTime date, int period, bool available)
    {
        if (!available)
        {
            border.Style = null;
            border.Child = null;
            border.CanDrag = false;
            border.ContextFlyout = null;
            ToolTipService.SetToolTip(border, null);
            return;
        }

        var slot = Resolve(date, period);

        if (slot.IsBlank)
        {
            border.Style = CellStyle("WeekEmptySlotStyle");
            border.Child = null;
            border.CanDrag = false;
            border.ContextFlyout = BuildSlotMenu(date, period, slot);
            ToolTipService.SetToolTip(border, null);
            return;
        }

        border.Style = CellStyle(slot.Kind switch
        {
            LessonChangeKind.Cancelled => "WeekCancelledSlotStyle",
            LessonChangeKind.None => "WeekUsualSlotStyle",
            _ => "WeekChangedSlotStyle"
        });

        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        // 표식을 과목명 앞에 붙인다 — (교)교체 · (보)보강 · (대)대강 · (휴)휴강
        panel.Children.Add(new TextBlock
        {
            Text = LessonChangeLabels.WithPrefix(slot.Kind, slot.Subject),
            Style = CellStyle(slot.Kind == LessonChangeKind.Cancelled
                ? "WeekSubjectStruckStyle"
                : "WeekSubjectStyle")
        });

        if (!string.IsNullOrWhiteSpace(slot.Room))
            panel.Children.Add(new TextBlock { Text = slot.Room, Style = CellStyle("WeekRoomTextStyle") });

        border.Child = panel;
        border.CanDrag = slot.Movable;
        border.ContextFlyout = BuildSlotMenu(date, period, slot);

        var memo = _changes.TryGetValue((date.Date, period), out var change) ? change.Memo : string.Empty;
        var tip = $"{date:M월 d일} {period}교시\n{slot.Subject}";
        if (!string.IsNullOrWhiteSpace(slot.Room)) tip += $" · {slot.Room}";
        if (slot.Kind != LessonChangeKind.None) tip += $"\n[{LessonChangeLabels.Name(slot.Kind)}]";
        if (!string.IsNullOrWhiteSpace(memo)) tip += $" {memo}";

        ToolTipService.SetToolTip(border, tip);
    }

    private MenuFlyout BuildSlotMenu(DateTime date, int period, SlotView slot)
    {
        var menu = new MenuFlyout();

        if (slot.Kind != LessonChangeKind.Cancelled && !slot.IsBlank)
        {
            var cancel = new MenuFlyoutItem
            {
                Text = "이 날 휴강",
                Icon = new FontIcon { Glyph = "" },
                Tag = (date, period)
            };
            cancel.Click += OnMenuCancelClick;
            menu.Items.Add(cancel);
        }

        // 내 수업 넣기 — 수업과 강의실을 함께 고른다.
        // 강의실을 자동으로 첫 번째로 골라 주면, 학급이 여럿인 수업에서 엉뚱한 반이 들어간다.
        if (_courses.Count > 0)
        {
            var sub = new MenuFlyoutSubItem { Text = "내 수업 넣기" };

            foreach (var course in _courses)
            {
                var rooms = course.RoomList;

                if (rooms.Count == 0)
                {
                    // 강의실이 등록되지 않은 수업은 과목만 넣는다
                    var plain = new MenuFlyoutItem
                    {
                        Text = course.DisplayName,
                        Tag = (date, period, course, string.Empty)
                    };
                    plain.Click += OnMenuPutCourseClick;
                    sub.Items.Add(plain);
                    continue;
                }

                var byCourse = new MenuFlyoutSubItem { Text = course.DisplayName };
                foreach (var room in rooms)
                {
                    var item = new MenuFlyoutItem { Text = room, Tag = (date, period, course, room) };
                    item.Click += OnMenuPutCourseClick;
                    byCourse.Items.Add(item);
                }

                sub.Items.Add(byCourse);
            }

            menu.Items.Add(sub);
        }

        var substitute = new MenuFlyoutItem
        {
            Text = "대강 입력…",
            Icon = new FontIcon { Glyph = "" },
            Tag = (date, period)
        };
        substitute.Click += OnMenuSubstituteClick;
        menu.Items.Add(substitute);

        // 강의실 바꾸기 — 내 수업일 때만 후보를 낼 수 있다
        var course2 = FindCourse(slot.CourseNo);
        if (course2 != null && slot.Kind != LessonChangeKind.Cancelled)
        {
            var rooms = course2.RoomList;
            if (rooms.Count > 0)
            {
                var sub = new MenuFlyoutSubItem { Text = "강의실" };
                foreach (var room in rooms)
                {
                    var item = new MenuFlyoutItem { Text = room, Tag = (date, period, room) };
                    item.Click += OnMenuRoomClick;
                    sub.Items.Add(item);
                }
                menu.Items.Add(sub);
            }
        }

        if (_changes.ContainsKey((date.Date, period)))
        {
            menu.Items.Add(new MenuFlyoutSeparator());

            var revert = new MenuFlyoutItem
            {
                Text = "평소대로 되돌리기",
                Icon = new FontIcon { Glyph = "" },
                Tag = (date, period)
            };
            revert.Click += OnMenuRevertClick;
            menu.Items.Add(revert);
        }

        return menu;
    }

    private void RefreshSlot(DateTime date, int period)
    {
        if (!_cells.TryGetValue((date.Date, period), out var border)) return;

        int day = SchoolCalendar.ToLessonDayOfWeek(date);
        ApplySlotVisual(border, date, period, period <= _periods.ForDay(day));
        UpdateCursorVisual();
    }

    private void UpdateCursorVisual()
    {
        foreach (var (key, border) in _cells)
        {
            int day = SchoolCalendar.ToLessonDayOfWeek(key.Date);
            if (key.Period > _periods.ForDay(day)) continue;

            if (_focused && _cursor.HasValue && _cursor.Value.Date == key.Date && _cursor.Value.Period == key.Period)
            {
                border.BorderBrush = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
                border.BorderThickness = new Thickness(2);
            }
            else
            {
                // 로컬 값을 지워 스타일(ThemeResource)로 되돌린다.
                border.ClearValue(Border.BorderBrushProperty);
                border.ClearValue(Border.BorderThicknessProperty);
            }
        }
    }

    #endregion

    #region 변경 저장

    /// <summary>
    /// 그 날 한 칸의 최종 내용을 정한다.
    ///
    /// 결과가 평소와 같아지면 <b>변경 행을 지운다</b> — 남겨 두면 나중에 기초 시간표를 고쳤을 때
    /// 옛 내용이 그 날에만 고정으로 버틴다.
    /// </summary>
    private async Task SetSlotAsync(
        DateTime date, int period, Course? course, string subjectText, string room, string? memo = null)
    {
        int day = SchoolCalendar.ToLessonDayOfWeek(date);
        var lesson = _lessons.FirstOrDefault(l => l.DayOfWeek == day && l.Period == period);

        bool cancelling = course == null && string.IsNullOrWhiteSpace(subjectText);
        bool sameAsUsual = cancelling
            ? lesson == null
            : course != null && lesson != null && lesson.Course == course.No && lesson.Room == room;

        if (sameAsUsual)
        {
            await RevertSlotAsync(date, period);
            return;
        }

        _changes.TryGetValue((date.Date, period), out var existing);

        var change = new LessonChange
        {
            TeacherID = _teacherId,
            Year = _year,
            Semester = _semester,
            Date = date.Date,
            Period = period,
            CourseNo = course?.No,
            SubjectText = course == null ? subjectText : string.Empty,
            Room = cancelling ? string.Empty : room,
            Memo = memo ?? existing?.Memo ?? string.Empty
        };

        using var repo = new LessonChangeRepository(SchoolDatabase.DbPath);
        if (!await repo.UpsertAsync(change))
        {
            ShowWarning("변경을 저장하지 못했습니다.");
            return;
        }

        change.CourseSubject = course?.Subject ?? string.Empty;
        _changes[(date.Date, period)] = change;

        _cursor = (date.Date, period);
        RefreshSlot(date, period);
        await RefreshFutureCountAsync();
        BuildTable();
    }

    /// <summary>그 칸의 변경을 지워 평소대로 되돌린다.</summary>
    private async Task RevertSlotAsync(DateTime date, int period)
    {
        if (!_changes.TryGetValue((date.Date, period), out var change)) return;

        using var repo = new LessonChangeRepository(SchoolDatabase.DbPath);
        await repo.DeleteAsync(change.No);

        _changes.Remove((date.Date, period));

        _cursor = (date.Date, period);
        RefreshSlot(date, period);
        await RefreshFutureCountAsync();
        BuildTable();
    }

    private async Task RefreshFutureCountAsync()
    {
        try
        {
            var (_, end) = WeeklyHoursCalculator.DefaultSemesterRange(_year, _semester);
            using var repo = new LessonChangeRepository(SchoolDatabase.DbPath);
            _futureChangeCount = (await repo.GetRangeAsync(_teacherId, DateTime.Today, end)).Count;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WeeklyTimetableView] 변경 수 집계 실패: {ex.Message}");
        }
    }

    #endregion

    #region 메뉴

    private async void OnMenuCancelClick(object sender, RoutedEventArgs e)
    {
        if (Slot(sender) is not var (date, period)) return;
        await RunAsync(() => SetSlotAsync(date, period, null, string.Empty, string.Empty), "휴강 처리");
    }

    private async void OnMenuRevertClick(object sender, RoutedEventArgs e)
    {
        if (Slot(sender) is not var (date, period)) return;
        await RunAsync(() => RevertSlotAsync(date, period), "되돌리기");
    }

    private async void OnMenuPutCourseClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item
            || item.Tag is not ValueTuple<DateTime, int, Course, string> payload)
            return;

        var (date, period, course, room) = payload;

        await RunAsync(() => SetSlotAsync(date, period, course, string.Empty, room), "수업 넣기");
    }

    private async void OnMenuRoomClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not ValueTuple<DateTime, int, string> payload)
            return;

        var (date, period, room) = payload;
        var slot = Resolve(date, period);
        var course = FindCourse(slot.CourseNo);

        await RunAsync(() => SetSlotAsync(date, period, course, slot.Subject, room), "강의실 변경");
    }

    private async void OnMenuSubstituteClick(object sender, RoutedEventArgs e)
    {
        if (Slot(sender) is not var (date, period)) return;

        _changes.TryGetValue((date.Date, period), out var existing);

        var dialog = new Dialogs.SubstituteInputDialog(
            date, period,
            existing?.SubjectText, existing?.Room, existing?.Memo)
        {
            XamlRoot = this.XamlRoot
        };

        if (await MessageBox.ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;

        await RunAsync(
            () => SetSlotAsync(date, period, null, dialog.Subject, dialog.Room, dialog.Memo),
            "대강 입력");
    }

    private static (DateTime Date, int Period)? Slot(object sender)
        => sender is MenuFlyoutItem item && item.Tag is ValueTuple<DateTime, int> tag
            ? (tag.Item1, tag.Item2)
            : null;

    #endregion

    #region 드래그 (맞바꾸기)

    private void OnSlotDragStarting(UIElement sender, DragStartingEventArgs e)
    {
        if (sender is not Border border || border.Tag is not ValueTuple<DateTime, int> tag)
        {
            e.Cancel = true;
            return;
        }

        if (!Resolve(tag.Item1, tag.Item2).Movable)
        {
            e.Cancel = true;
            return;
        }

        _dragFrom = (tag.Item1.Date, tag.Item2);
        CourseTimetableBoard.TrySetMarker(e.Data, DragSlot);
        e.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void OnSlotDragOver(object sender, DragEventArgs e)
    {
        if (sender is not Border || _dragFrom == null) return;

        e.AcceptedOperation = DataPackageOperation.Move;
        e.Handled = true;
    }

    private async void OnSlotDrop(object sender, DragEventArgs e)
    {
        if (sender is not Border border || border.Tag is not ValueTuple<DateTime, int> tag)
            return;

        e.Handled = true;

        var from = _dragFrom;
        _dragFrom = null;

        if (from == null) return;

        var to = (Date: tag.Item1.Date, Period: tag.Item2);
        if (from.Value == to) return;

        await RunAsync(() => SwapAsync(from.Value, to), "맞바꾸기");
    }

    /// <summary>두 칸을 맞바꾼다 — 그 날들의 변경 두 줄로 표현된다.</summary>
    private async Task SwapAsync((DateTime Date, int Period) a, (DateTime Date, int Period) b)
    {
        var slotA = Resolve(a.Date, a.Period);
        var slotB = Resolve(b.Date, b.Period);

        await PutAsync(b, slotA);
        await PutAsync(a, slotB);

        _cursor = b;
        BuildTable();

        async Task PutAsync((DateTime Date, int Period) target, SlotView content)
        {
            if (!content.Movable)
            {
                await SetSlotAsync(target.Date, target.Period, null, string.Empty, string.Empty);
                return;
            }

            var course = FindCourse(content.CourseNo);
            await SetSlotAsync(
                target.Date, target.Period,
                course,
                course == null ? content.Subject : string.Empty,
                content.Room);
        }
    }

    #endregion

    #region 키보드 · 포인터

    private void OnSlotPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not ValueTuple<DateTime, int> tag)
            return;

        _cursor = (tag.Item1.Date, tag.Item2);
        WeekGrid.Focus(FocusState.Programmatic);
        UpdateCursorVisual();
        UpdateStatus();
    }

    private void OnGridGotFocus(object sender, RoutedEventArgs e)
    {
        _focused = true;
        UpdateCursorVisual();
    }

    private void OnGridLostFocus(object sender, RoutedEventArgs e)
    {
        _focused = false;
        UpdateCursorVisual();
    }

    private async void OnGridKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_cursor == null || _dates.Count == 0) return;

        switch (e.Key)
        {
            case VirtualKey.Left:
                MoveCursor(0, -1);
                e.Handled = true;
                return;

            case VirtualKey.Right:
                MoveCursor(0, 1);
                e.Handled = true;
                return;

            case VirtualKey.Up:
                MoveCursor(-1, 0);
                e.Handled = true;
                return;

            case VirtualKey.Down:
                MoveCursor(1, 0);
                e.Handled = true;
                return;

            case VirtualKey.Enter:
            case VirtualKey.Space:
                e.Handled = true;
                if (_selectedCourse == null)
                {
                    ShowWarning("넣을 수업을 위쪽 [수업] 에서 먼저 고르세요.");
                    return;
                }
                await RunAsync(
                    () => SetSlotAsync(
                        _cursor.Value.Date, _cursor.Value.Period,
                        _selectedCourse, string.Empty,
                        _selectedCourse.RoomList.FirstOrDefault() ?? string.Empty),
                    "수업 넣기");
                return;

            case VirtualKey.Delete:
            case VirtualKey.Back:
                e.Handled = true;
                var slot = Resolve(_cursor.Value.Date, _cursor.Value.Period);

                // 보강·대강은 "휴강" 이 아니라 그냥 되돌리는 게 맞다 — 평소에 없던 수업이다.
                if (slot.Kind is LessonChangeKind.Added or LessonChangeKind.Substitute)
                    await RunAsync(() => RevertSlotAsync(_cursor.Value.Date, _cursor.Value.Period), "되돌리기");
                else if (!slot.IsBlank && slot.Kind != LessonChangeKind.Cancelled)
                    await RunAsync(
                        () => SetSlotAsync(_cursor.Value.Date, _cursor.Value.Period, null, string.Empty, string.Empty),
                        "휴강 처리");
                return;
        }
    }

    private void MoveCursor(int dateDelta, int periodDelta)
    {
        if (_cursor == null) return;

        int index = _dates.IndexOf(_cursor.Value.Date);
        if (index < 0) index = 0;

        index = Math.Clamp(index + dateDelta, 0, _dates.Count - 1);
        var date = _dates[index];

        int max = _periods.ForDay(SchoolCalendar.ToLessonDayOfWeek(date));
        int period = Math.Clamp(_cursor.Value.Period + periodDelta, 1, Math.Max(1, max));

        _cursor = (date, period);
        UpdateCursorVisual();
        UpdateStatus();
    }

    #endregion

    #region Helper

    private Course? FindCourse(int courseNo)
        => courseNo <= 0 ? null : _courses.FirstOrDefault(c => c.No == courseNo);

    private void UpdateStatus()
    {
        int inRange = _changes.Count;

        var text = $"보는 구간 변경 {inRange}건 · 앞으로 등록된 변경 {_futureChangeCount}건";

        if (_selectedCourse != null)
            text += $"   ·   Enter 로 넣을 수업: {_selectedCourse.DisplayName}";

        if (_cursor != null)
            text += $"   ·   커서 {_cursor.Value.Date:M/d} {_cursor.Value.Period}교시";

        TxtStatus.Text = text;
    }

    private void ShowWarning(string message)
    {
        WeekInfoBar.Message = message;
        WeekInfoBar.IsOpen = true;
    }

    #endregion
}
