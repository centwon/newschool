using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Input;
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
/// 주별 시간표 — <b>교시가 행, 날짜가 열</b>인 표. 한 번에 3주치(15일)를 가로로 보여 준다.
///
/// <para>예전에는 날짜를 세로로 세웠다. 기초 시간표(<see cref="CourseTimetableBoard"/>)와
/// 행·열을 반대로 두면 "그 주만" 바꿀 것을 "매주" 바꾸는 사고를 막을 수 있다고 봤기 때문이다.
/// 실제로 써 보니 <b>날짜가 15줄로 아래로 흘러</b> 한 주를 한눈에 보기 어려웠고, 시간표를
/// 시간표답게 읽지 못했다. 지금은 가로로 세우고, 헷갈릴 위험은 <b>모양이 아니라 표시</b>로
/// 막는다 — 주 구분 띠, 날짜(요일)·오늘 표시, 변경 칸의 (휴)(교)(보)(대) 표식, 그리고
/// 도구 모음의 "평소 시간표는 그대로입니다" 안내.</para>
///
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

    /// <summary>표에 그린 날짜들 (왼쪽에서 오른쪽 순서)</summary>
    private readonly List<DateTime> _dates = [];

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
    ///
    /// <para>수업 <b>하나</b>는 받지 않는다 — 이 표가 보는 대상은 날짜이고, 그 날 있는 수업은
    /// 모두 보여야 한다. 예전에는 고른 수업을 받아 Enter 로 그 수업을 넣었는데, 정작 이 탭에는
    /// 무엇이 골라져 있는지 보이지 않았다. 지금 Enter 는 <b>그 칸의 메뉴를 연다</b>.</para>
    /// </summary>
    public async Task LoadAsync(int year, int semester, IReadOnlyList<Course> courses)
    {
        bool scopeChanged = _year != year || _semester != semester;

        _year = year;
        _semester = semester;
        _teacherId = Settings.User.Value;
        _periods = PeriodCounts.Parse(Settings.PeriodsPerDay.Value);
        _maxPeriod = Math.Max(1, Enumerable.Range(1, DayCount).Max(_periods.ForDay));

        _courses.Clear();
        _courses.AddRange(courses);

        if (scopeChanged || _firstMonday == default)
            _firstMonday = MondayOf(DateTime.Today);

        if (scopeChanged || _schedules.Count == 0)
            await LoadSchedulesAsync();

        await ReloadAsync();
    }

    private static DateTime MondayOf(DateTime date) => DateTimeHelper.MondayOf(date);

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

    /// <summary>고정 교시 열의 너비</summary>
    private const double PeriodColumnWidth = 52;

    /// <summary>
    /// 날짜 한 열의 너비. 고정이라야 15일이 가로로 흘러 스크롤이 생긴다.
    ///
    /// <para>"8/31(월)" 한 줄이 들어갈 만큼만 잡는다 — 한 화면에 하루라도 더 보이는 편이
    /// 낫기 때문이다. "오늘"·휴업 사유는 <b>아랫줄</b>로 내리고, 과목명이 길면 칸에서는
    /// 줄임표로 자른다(전문은 툴팁에 있다).</para>
    /// </summary>
    private const double DateColumnWidth = 88;

    private const double BandRowHeight = 26;
    private const double DateRowHeight = 44;

    /// <summary>과목명 줄 + 강의실 줄이 들어가므로 넉넉히 잡는다 — 낮으면 아래 줄이 잘린다.</summary>
    private const double SlotRowHeight = 62;

    private void BuildTable()
    {
        WeekGrid.Children.Clear();
        WeekGrid.RowDefinitions.Clear();
        WeekGrid.ColumnDefinitions.Clear();
        PeriodGrid.Children.Clear();
        PeriodGrid.RowDefinitions.Clear();
        PeriodGrid.ColumnDefinitions.Clear();
        _cells.Clear();
        _dates.Clear();

        // ── 행: [주 띠] [날짜] [1교시] … [n교시] ─ 두 표가 같은 높이를 쓴다.
        foreach (var grid in new[] { PeriodGrid, WeekGrid })
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BandRowHeight) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(DateRowHeight) });
            for (int period = 0; period < _maxPeriod; period++)
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SlotRowHeight) });
        }

        // ── 고정 열(교시)
        PeriodGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PeriodColumnWidth) });
        AddHeader(PeriodGrid, string.Empty, 0, 0);       // 주 띠와 마주 보는 빈 모서리
        AddHeader(PeriodGrid, "교시", 1, 0);
        for (int period = 1; period <= _maxPeriod; period++)
            AddHeader(PeriodGrid, $"{period}", period + 1, 0);

        // ── 날짜 열들
        for (int i = 0; i < WeekCount * DayCount; i++)
            WeekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DateColumnWidth) });

        for (int week = 0; week < WeekCount; week++)
        {
            var monday = _firstMonday.AddDays(week * 7);
            AddWeekBand(monday, week * DayCount);

            for (int day = 0; day < DayCount; day++)
            {
                var date = monday.AddDays(day);
                int column = week * DayCount + day;

                AddDateCell(date, column);

                for (int period = 1; period <= _maxPeriod; period++)
                    AddSlotCell(date, period, column);

                _dates.Add(date);
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

    private void AddHeader(Grid grid, string text, int row, int column)
    {
        var border = new Border
        {
            Style = CellStyle("WeekHeaderCellStyle"),
            Child = new TextBlock { Text = text, Style = CellStyle("WeekHeaderTextStyle") }
        };

        Grid.SetRow(border, row);
        Grid.SetColumn(border, column);
        grid.Children.Add(border);
    }

    private void AddWeekBand(DateTime monday, int startColumn)
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

        // 다섯 열을 합쳐도 좁으면 줄임표가 붙으므로 전문을 툴팁에 남긴다.
        ToolTipService.SetToolTip(border, text);

        Grid.SetRow(border, 0);
        Grid.SetColumn(border, startColumn);
        Grid.SetColumnSpan(border, DayCount);
        WeekGrid.Children.Add(border);
    }

    private void AddDateCell(DateTime date, int column)
    {
        string? off = OffDayReason(date);
        string? note = off == null ? GradeEventNote(date) : null;
        bool today = date == DateTime.Today;

        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        // 윗줄은 날짜만 — 열이 좁아 "· 오늘"을 붙이면 그만큼 폭을 더 잡아먹는다.
        // 오늘은 색으로, 사유는 아랫줄로 뺀다.
        panel.Children.Add(new TextBlock
        {
            Text = $"{date:M/d}({DayNames[SchoolCalendar.ToLessonDayOfWeek(date) - 1]})",
            Style = CellStyle(today ? "WeekTodayDateTextStyle" : "WeekDateTextStyle")
        });

        var sub = string.Join(" · ", new[] { today ? "오늘" : null, off ?? note }.Where(s => s != null));
        if (sub.Length > 0)
            panel.Children.Add(new TextBlock { Text = sub, Style = CellStyle("WeekReasonTextStyle") });

        var border = new Border
        {
            // 학교 전체 휴업만 흐리게 만든다. 일부 학년 행사는 다른 학년 수업이 그대로 있으므로
            // 사유만 적고 칸은 살려 둔다.
            Style = CellStyle(off != null ? "WeekOffDateCellStyle" : "WeekDateCellStyle"),
            Child = panel
        };

        // 잘려 보일 수 있으므로 전문은 툴팁에 남긴다.
        var tip = string.Join(" · ",
            new[] { $"{date:M월 d일}({DayNames[SchoolCalendar.ToLessonDayOfWeek(date) - 1]})", sub.Length > 0 ? sub : null }
                .Where(s => s != null));
        ToolTipService.SetToolTip(border, tip);

        Grid.SetRow(border, 1);
        Grid.SetColumn(border, column);
        WeekGrid.Children.Add(border);
    }

    /// <summary>그 날 학교가 통째로 쉬는 사유 (없으면 null)</summary>
    private string? OffDayReason(DateTime date)
    {
        foreach (var schedule in _schedules)
        {
            if (schedule == null || schedule.IsDeleted) continue;
            if (schedule.AA_YMD.Date != date.Date) continue;

            if (SchoolCalendar.IsNonTeachingDay(schedule))
                return string.IsNullOrWhiteSpace(schedule.EVENT_NM) ? schedule.SBTR_DD_SC_NM : schedule.EVENT_NM;
        }

        return null;
    }

    /// <summary>
    /// 그 날 <b>일부 학년만</b> 걸리는 행사 (없으면 null).
    ///
    /// <para>예전에는 "위에서 고른 수업의 학년" 으로 판정했다. 이 탭에서 수업 선택을 걷어내면서
    /// <b>내가 가르치는 학년들</b> 기준으로 바꿨다 — 어느 수업을 골랐는지와 무관하게 내 시간표에
    /// 걸리는 행사면 알려야 한다.</para>
    /// </summary>
    private string? GradeEventNote(DateTime date)
    {
        var grades = _courses.Select(c => c.Grade).Where(g => g > 0).Distinct().OrderBy(g => g).ToList();
        if (grades.Count == 0) return null;

        foreach (var schedule in _schedules)
        {
            if (schedule == null || schedule.IsDeleted) continue;
            if (schedule.AA_YMD.Date != date.Date) continue;

            foreach (int grade in grades)
            {
                if (SchoolCalendar.IsGradeOnlyEvent(schedule, grade, _gradeCount))
                    return $"{grade}학년 {schedule.EVENT_NM}";
            }
        }

        return null;
    }

    private void AddSlotCell(DateTime date, int period, int column)
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

        Grid.SetRow(border, period + 1);
        Grid.SetColumn(border, column);
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
        var plan = PlanSlot(date, period, course, subjectText, room, memo);

        using var repo = new LessonChangeRepository(SchoolDatabase.DbPath);
        if (!await ApplyPlanAsync(repo, plan))
        {
            ShowWarning(plan.Change != null
                ? "변경을 저장하지 못했습니다."
                : "변경을 되돌리지 못했습니다.");
            return;
        }

        RememberPlan(plan);

        _cursor = (plan.Date, plan.Period);
        RefreshSlot(plan.Date, plan.Period);
        await RefreshFutureCountAsync();
        BuildTable();
    }

    /// <summary>
    /// 한 칸에 무엇을 쓸지 정한 결과. DB 를 건드리기 <b>전에</b> 계산해 둔다 —
    /// 맞바꾸기는 두 칸의 계획을 먼저 세운 뒤 한 트랜잭션으로 함께 적용한다.
    /// </summary>
    /// <param name="Change">쓸 내용. null 이면 "평소대로" 라서 기존 변경 행을 지운다.</param>
    /// <param name="DeleteNo">지울 변경 행 번호(0 이면 지울 것이 없다).</param>
    private readonly record struct SlotPlan(
        DateTime Date, int Period, LessonChange? Change, int DeleteNo);

    /// <summary>그 칸의 최종 내용을 정한다(DB 는 건드리지 않는다).</summary>
    private SlotPlan PlanSlot(
        DateTime date, int period, Course? course, string subjectText, string room, string? memo)
    {
        int day = SchoolCalendar.ToLessonDayOfWeek(date);
        var lesson = _lessons.FirstOrDefault(l => l.DayOfWeek == day && l.Period == period);

        bool cancelling = course == null && string.IsNullOrWhiteSpace(subjectText);
        bool sameAsUsual = cancelling
            ? lesson == null
            : course != null && lesson != null && lesson.Course == course.No && lesson.Room == room;

        _changes.TryGetValue((date.Date, period), out var existing);

        // 결과가 평소와 같아지면 변경 행을 지운다 — 남겨 두면 나중에 기초 시간표를 고쳤을 때
        // 옛 내용이 그 날에만 고정으로 버틴다.
        if (sameAsUsual)
            return new SlotPlan(date.Date, period, null, existing?.No ?? 0);

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
            Memo = memo ?? existing?.Memo ?? string.Empty,
            CourseSubject = course?.Subject ?? string.Empty
        };

        return new SlotPlan(date.Date, period, change, 0);
    }

    /// <summary>계획 한 건을 DB 에 적용한다. 지울 것도 쓸 것도 없으면 성공으로 본다.</summary>
    private static async Task<bool> ApplyPlanAsync(LessonChangeRepository repo, SlotPlan plan)
    {
        if (plan.Change != null) return await repo.UpsertAsync(plan.Change);
        if (plan.DeleteNo > 0) return await repo.DeleteAsync(plan.DeleteNo);
        return true;
    }

    /// <summary>DB 반영이 끝난 계획을 화면 쪽 _changes 에도 반영한다.</summary>
    private void RememberPlan(SlotPlan plan)
    {
        if (plan.Change != null) _changes[(plan.Date, plan.Period)] = plan.Change;
        else _changes.Remove((plan.Date, plan.Period));
    }

    /// <summary>그 칸의 변경을 지워 평소대로 되돌린다.</summary>
    private async Task RevertSlotAsync(DateTime date, int period)
    {
        if (!_changes.TryGetValue((date.Date, period), out var change)) return;

        using var repo = new LessonChangeRepository(SchoolDatabase.DbPath);
        // ⚠ 결과를 봐야 한다. 예전에는 삭제 결과를 버리고 화면의 _changes 에서만 지웠다 —
        // 지우지 못했는데도 칸은 평소 수업으로 돌아가 보이고, 다시 열면 변경이 되살아났다.
        // 바로 위 SetSlotAsync 는 UpsertAsync 결과를 확인한다. 같은 기준을 맞춘다.
        if (!await repo.DeleteAsync(change.No))
        {
            ShowWarning("변경을 되돌리지 못했습니다.");
            return;
        }

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

    /// <summary>
    /// 두 칸을 맞바꾼다 — 그 날들의 변경 두 줄로 표현된다.
    ///
    /// <para>⚠ 두 줄은 <b>한 트랜잭션</b>으로 함께 들어가야 한다. 예전에는 <c>SetSlotAsync</c> 를
    /// 두 번 불러 각자 연결·각자 저장이었고, 두 번째가 실패하면 첫 칸만 바뀐 채로 남아
    /// <b>같은 수업이 두 칸에</b> 보였다(원래 자리는 그대로, 옮긴 자리에도 하나).</para>
    ///
    /// <para>계획(<see cref="SlotPlan"/>)은 둘 다 DB 를 건드리기 전에 세운다 — 먼저 쓴 내용이
    /// 두 번째 계획의 "평소와 같은가" 판정을 흔들지 않도록.</para>
    /// </summary>
    private async Task SwapAsync((DateTime Date, int Period) a, (DateTime Date, int Period) b)
    {
        var slotA = Resolve(a.Date, a.Period);
        var slotB = Resolve(b.Date, b.Period);

        var planB = PlanFor(b, slotA);
        var planA = PlanFor(a, slotB);

        using var repo = new LessonChangeRepository(SchoolDatabase.DbPath);
        repo.BeginTransaction();
        try
        {
            if (!await ApplyPlanAsync(repo, planB) || !await ApplyPlanAsync(repo, planA))
            {
                repo.Rollback();
                ShowWarning("맞바꾸지 못했습니다. 시간표는 그대로 둡니다.");
                return;
            }

            repo.Commit();
        }
        catch
        {
            repo.Rollback();
            throw;
        }

        RememberPlan(planB);
        RememberPlan(planA);

        _cursor = b;
        await RefreshFutureCountAsync();
        BuildTable();

        SlotPlan PlanFor((DateTime Date, int Period) target, SlotView content)
        {
            if (!content.Movable)
                return PlanSlot(target.Date, target.Period, null, string.Empty, string.Empty, null);

            var course = FindCourse(content.CourseNo);
            return PlanSlot(
                target.Date, target.Period,
                course,
                course == null ? content.Subject : string.Empty,
                content.Room,
                null);
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
            // 표가 가로로 서 있으므로 ←→ 는 날짜, ↑↓ 는 교시다.
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

            // 넣을 것을 고르는 자리는 칸의 메뉴다 — 화면에 보이지 않는 "고른 수업" 에
            // 기대지 않는다(오른쪽 클릭과 같은 메뉴가 열린다).
            case VirtualKey.Enter:
            case VirtualKey.Space:
                e.Handled = true;
                ShowSlotMenu(_cursor.Value.Date, _cursor.Value.Period);
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
        EnsureCursorVisible();
    }

    /// <summary>커서가 가로 스크롤 밖으로 나가면 그 열이 보이도록 민다.</summary>
    private void EnsureCursorVisible()
    {
        if (_cursor == null) return;

        int index = _dates.IndexOf(_cursor.Value.Date);
        if (index < 0) return;

        double left = index * DateColumnWidth;
        double right = left + DateColumnWidth;
        double viewport = WeekScroll.ViewportWidth;
        double offset = WeekScroll.HorizontalOffset;

        if (left < offset)
            WeekScroll.ChangeView(left, null, null, true);
        else if (right > offset + viewport)
            WeekScroll.ChangeView(right - viewport, null, null, true);
    }

    /// <summary>그 칸의 메뉴를 연다 (오른쪽 클릭과 같은 것).</summary>
    private void ShowSlotMenu(DateTime date, int period)
    {
        if (!_cells.TryGetValue((date.Date, period), out var border)) return;
        border.ContextFlyout?.ShowAt(border);
    }

    /// <summary>
    /// 마우스 휠 → <b>가로</b>. 3주치 15일이 가로로 늘어서므로 실제로 넘길 축은 가로다.
    /// 위아래(교시)는 Shift+휠로 남긴다 — 교시가 화면보다 많을 때만 움직인다.
    ///
    /// <para>⚠ 디스플레이 배율이 100% 가 아니면 WinUI 가 휠을 커서 아래가 아닌 다른 요소로
    /// hit-test 하는 프레임워크 버그가 있다(microsoft-ui-xaml#7008 계열). 그 배율에서는 이
    /// 처리도 함께 빗나간다 — 앱 코드로 고칠 수 있는 문제가 아니다.</para>
    /// </summary>
    private void OnTableWheel(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint((UIElement)sender).Properties.MouseWheelDelta;
        if (delta == 0) return;

        bool shift = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (shift)
            OuterScroll.ChangeView(null, OuterScroll.VerticalOffset - delta, null, true);
        else
            WeekScroll.ChangeView(WeekScroll.HorizontalOffset - delta, null, null, true);

        e.Handled = true;
    }

    #endregion

    #region Helper

    private Course? FindCourse(int courseNo)
        => courseNo <= 0 ? null : _courses.FirstOrDefault(c => c.No == courseNo);

    private void UpdateStatus()
    {
        int inRange = _changes.Count;

        var text = $"보는 구간 변경 {inRange}건 · 앞으로 등록된 변경 {_futureChangeCount}건";

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
