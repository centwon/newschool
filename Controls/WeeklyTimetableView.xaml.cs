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
public sealed partial class WeeklyTimetableView : UserControl, ILessonSlotMenuHost
{
    private const int DayCount = 5;          // 월~금
    private const string DragSlot = "weekslot";

    private static readonly string[] DayNames = ["월", "화", "수", "목", "금"];

    /// <summary>기초·변경·학사일정·일지 표시 — 칸 풀기와 변경 저장도 여기서 한다.</summary>
    private readonly LessonSlotBook _book = new();

    /// <summary>칸 메뉴 (수업 홈 · 오늘 화면과 같은 메뉴)</summary>
    private readonly LessonSlotMenu _menu;

    /// <summary>표에 그린 칸 — (날짜, 교시) → Border</summary>
    private readonly Dictionary<(DateTime Date, int Period), Border> _cells = [];

    /// <summary>표에 그린 날짜들 (왼쪽에서 오른쪽 순서)</summary>
    private readonly List<DateTime> _dates = [];

    private PeriodCounts _periods = PeriodCounts.Default;
    private int _maxPeriod = 7;
    private int _futureChangeCount;

    /// <summary>보고 있는 구간의 첫 월요일</summary>
    private DateTime _firstMonday;

    private (DateTime Date, int Period)? _cursor;
    private bool _focused;
    private (DateTime Date, int Period)? _dragFrom;

    /// <summary>
    /// 간단 모드 — 수업 홈의 "내 시간표" 카드. <b>1주</b>만 보이고, 도구 모음·주 띠·상태 줄이 없고,
    /// 열이 카드 폭을 채운다. 칸을 누르면 바로 메뉴가 뜨고, 칸끼리 끌어 맞바꾸기(교체)는 꺼진다 —
    /// 홈은 훑어보는 화면이라 실수로 끌어 시간표가 바뀌면 안 된다(교체는 메뉴로만).
    /// 일지를 써 둔 칸에는 공책 표시가 붙는다. XAML 에서 한 번 정한다.
    /// </summary>
    public bool Compact { get; set; }

    /// <summary>한 번에 보이는 주 수 — 수업 관리는 3주, 간단 모드는 1주.</summary>
    private int WeekCount => Compact ? 1 : 3;

    /// <summary>보고 있는 주에 수업이 한 칸이라도 있는가 — 빈 시간표에 안내를 얹을지 정할 때 쓴다.</summary>
    public bool HasAnyLesson => _book.Lessons.Count > 0 || _book.Changes.Values.Any(c => c.HasCourse);

    /// <summary>칸 메뉴에서 수업 일지를 쓰거나 진도를 표시했다 — 홈의 목록을 다시 읽을 때 쓴다.</summary>
    public event EventHandler? LessonRecordChanged;

    public WeeklyTimetableView()
    {
        this.InitializeComponent();
        _menu = new LessonSlotMenu(_book, this);
        Loaded += (_, _) => ApplyMode();
    }

    /// <summary>간단 모드면 도구 모음·상태 줄·카드 테두리를 걷고 가로 스크롤을 끈다.</summary>
    private void ApplyMode()
    {
        if (!Compact) return;

        ToolbarGrid.Visibility = Visibility.Collapsed;
        TxtStatus.Visibility = Visibility.Collapsed;
        TableCard.BorderThickness = new Thickness(0);
        TableCard.Background = null;
        TableInner.Margin = new Thickness(0);
        WeekScroll.HorizontalScrollMode = ScrollMode.Disabled;
        WeekScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        OuterScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    #region 로드

    /// <summary>
    /// 학년도·학기와 수업 목록을 받는다.
    ///
    /// <para>수업 <b>하나</b>는 받지 않는다 — 이 표가 보는 대상은 날짜이고, 그 날 있는 수업은
    /// 모두 보여야 한다. 예전에는 고른 수업을 받아 Enter 로 그 수업을 넣었는데, 정작 이 탭에는
    /// 무엇이 골라져 있는지 보이지 않았다. 지금 Enter 는 <b>그 칸의 메뉴를 연다</b>.</para>
    /// </summary>
    /// <param name="firstDate">이 날이 든 주부터 보여 준다. null 이면 이번 주(학년도·학기가 바뀌었을 때) 또는 보던 주.</param>
    public async Task LoadAsync(int year, int semester, IReadOnlyList<Course> courses, DateTime? firstDate = null)
    {
        _periods = PeriodCounts.Parse(Settings.PeriodsPerDay.Value);
        _maxPeriod = Math.Max(1, Enumerable.Range(1, DayCount).Max(_periods.ForDay));

        bool scopeChanged = await _book.SetScopeAsync(year, semester, courses);

        if (firstDate != null)
            _firstMonday = MondayOf(firstDate.Value);
        else if (scopeChanged || _firstMonday == default)
            _firstMonday = MondayOf(DateTime.Today);

        await ReloadAsync();
    }

    private static DateTime MondayOf(DateTime date) => DateTimeHelper.MondayOf(date);

    private async Task ReloadAsync()
    {
        try
        {
            await _book.LoadRangeAsync(_firstMonday, _firstMonday.AddDays(WeekCount * 7 - 1), withJournals: Compact);

            if (_book.HasScope)
                await RefreshFutureCountAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WeeklyTimetableView] 로드 실패: {ex.Message}");
            ShowWarning($"주별 시간표를 불러오지 못했습니다.\n{ex.Message}");
        }

        BuildTable();
    }

    /// <summary>보고 있는 주를 다시 읽는다(수업 일지를 쓰고 돌아왔을 때 등).</summary>
    public Task RefreshAsync() => ReloadAsync();

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
        var dialog = new Dialogs.LessonChangeDialog(_book.Year, _book.Semester) { XamlRoot = this.XamlRoot };
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

    /// <summary>간단 모드(홈 카드)의 칸 높이 — 두 줄이 겨우 들어가는 만큼.</summary>
    private const double CompactSlotRowHeight = 46;

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

        ApplyMode();

        // ── 행: [주 띠] [날짜] [1교시] … [n교시] ─ 두 표가 같은 높이를 쓴다.
        //    간단 모드는 1주뿐이라 주 띠를 두지 않는다(높이 0) — 범위는 홈 카드 머리에 있다.
        foreach (var grid in new[] { PeriodGrid, WeekGrid })
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Compact ? 0 : BandRowHeight) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(DateRowHeight) });
            for (int period = 0; period < _maxPeriod; period++)
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Compact ? CompactSlotRowHeight : SlotRowHeight) });
        }

        // ── 고정 열(교시)
        PeriodGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Compact ? 32 : PeriodColumnWidth) });
        if (!Compact) AddHeader(PeriodGrid, string.Empty, 0, 0);       // 주 띠와 마주 보는 빈 모서리
        AddHeader(PeriodGrid, Compact ? "" : "교시", 1, 0);
        for (int period = 1; period <= _maxPeriod; period++)
            AddHeader(PeriodGrid, $"{period}", period + 1, 0);

        // ── 날짜 열들 — 간단 모드는 카드 폭을 나눠 채운다(가로 스크롤 없음)
        for (int i = 0; i < WeekCount * DayCount; i++)
            WeekGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = Compact ? new GridLength(1, GridUnitType.Star) : new GridLength(DateColumnWidth)
            });

        for (int week = 0; week < WeekCount; week++)
        {
            var monday = _firstMonday.AddDays(week * 7);
            if (!Compact) AddWeekBand(monday, week * DayCount);

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
        int count = _book.Changes.Keys.Count(k => k.Date >= monday && k.Date <= monday.AddDays(4));

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
        string? off = _book.OffDayReason(date);
        string? note = off == null ? _book.GradeEventNote(date) : null;
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

    private void AddSlotCell(DateTime date, int period, int column)
    {
        int day = SchoolCalendar.ToLessonDayOfWeek(date);
        bool available = period <= _periods.ForDay(day);

        var border = new Border
        {
            // 간단 모드는 끌어 맞바꾸기를 끈다 — 교체는 메뉴로만
            AllowDrop = available && !Compact,
            Tag = (date, period)
        };

        ApplySlotVisual(border, date, period, available);

        if (available)
        {
            if (!Compact)
            {
                border.DragEnter += OnSlotDragOver;
                border.DragOver += OnSlotDragOver;
                border.Drop += OnSlotDrop;
                border.DragStarting += OnSlotDragStarting;
            }
            border.PointerPressed += OnSlotPointerPressed;

            // 메뉴는 열 때 만든다 — 수업 일지·진도 항목이 그 순간의 기록을 봐야 한다.
            border.ContextRequested += OnSlotContextRequested;
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
            ToolTipService.SetToolTip(border, null);
            return;
        }

        var slot = _book.Resolve(date, period);

        if (slot.IsBlank)
        {
            // 학교 휴업일이면 그 열의 빈 칸을 옅게 칠해 휴일임을 드러낸다(날짜 머리와 같은 색).
            // 그 학년만 빠지는 행사 날은 다른 학년 수업이 있으므로 칠하지 않는다.
            var off = _book.OffDayReason(date);
            border.Style = CellStyle(off != null ? "WeekOffSlotStyle" : "WeekEmptySlotStyle");
            border.Child = null;
            border.CanDrag = false;
            ToolTipService.SetToolTip(border, off);
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

        bool journal = _book.Journals.ContainsKey((date.Date, period));

        if (!string.IsNullOrWhiteSpace(slot.Room) || journal)
        {
            // 강의실 줄 — 일지를 써 둔 칸이면 공책 표시를 옆에 붙인다
            var roomLine = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 3
            };
            if (!string.IsNullOrWhiteSpace(slot.Room))
                roomLine.Children.Add(new TextBlock { Text = slot.Room, Style = CellStyle("WeekRoomTextStyle") });
            if (journal)
                roomLine.Children.Add(new FontIcon
                {
                    Glyph = "",
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
                });
            panel.Children.Add(roomLine);
        }

        border.Child = panel;
        border.CanDrag = slot.Movable && !Compact;

        var memo = _book.MemoOf(date, period);
        var tip = $"{date:M월 d일} {period}교시\n{slot.Subject}";
        if (!string.IsNullOrWhiteSpace(slot.Room)) tip += $" · {slot.Room}";
        if (slot.Kind != LessonChangeKind.None) tip += $"\n[{LessonChangeLabels.Name(slot.Kind)}]";
        if (!string.IsNullOrWhiteSpace(memo)) tip += $" {memo}";
        if (journal) tip += "\n수업 일지 씀";

        ToolTipService.SetToolTip(border, tip);
    }


    private async void OnSlotContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border border || border.Tag is not ValueTuple<DateTime, int> tag) return;

        _cursor = (tag.Item1.Date, tag.Item2);
        UpdateCursorVisual();
        await ShowSlotMenuAsync(tag.Item1, tag.Item2, e.TryGetPosition(border, out var p) ? p : null);
    }

    // 칸 하나만 다시 칠하던 RefreshSlot 은 칸 메뉴를 LessonSlotMenu 로 떼어 낸 뒤(31ba67a)
    // 부르는 곳이 없어 지웠다(2026-09-28).

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

    #region 앞으로의 변경 수

    private async Task RefreshFutureCountAsync()
    {
        try
        {
            var (_, end) = WeeklyHoursCalculator.DefaultSemesterRange(_book.Year, _book.Semester);
            using var repo = new LessonChangeRepository(SchoolDatabase.DbPath);
            _futureChangeCount = (await repo.GetRangeAsync(_book.TeacherId, DateTime.Today, end)).Count;
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Warning("WeeklyTimetableView", $"앞으로의 변경 수를 세지 못했다: {ex.Message}");
        }
    }

    #endregion

    #region 드래그 (맞바꾸기)

    private void OnSlotDragStarting(UIElement sender, DragStartingEventArgs e)
    {
        if (sender is not Border border || border.Tag is not ValueTuple<DateTime, int> tag)
        {
            e.Cancel = true;
            return;
        }

        if (!_book.Resolve(tag.Item1, tag.Item2).Movable)
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

    /// <summary>두 칸을 맞바꾼다 — 한 트랜잭션으로 함께 저장한다(<see cref="LessonSlotBook.SwapAsync"/>).</summary>
    private async Task SwapAsync((DateTime Date, int Period) a, (DateTime Date, int Period) b)
    {
        if (!await _book.SwapAsync(a, b))
        {
            ShowWarning("맞바꾸지 못했습니다. 시간표는 그대로 둡니다.");
            return;
        }

        _cursor = b;
        await RefreshFutureCountAsync();
        BuildTable();
    }

    #endregion

    #region 키보드 · 포인터

    private async void OnSlotPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not ValueTuple<DateTime, int> tag)
            return;

        _cursor = (tag.Item1.Date, tag.Item2);
        WeekGrid.Focus(FocusState.Programmatic);
        UpdateCursorVisual();
        UpdateStatus();

        // 간단 모드(홈)는 누르면 바로 메뉴 — 오른쪽 단추를 찾지 않아도 된다.
        var point = e.GetCurrentPoint(border);
        if (Compact && point.Properties.IsLeftButtonPressed)
        {
            e.Handled = true;
            await ShowSlotMenuAsync(tag.Item1, tag.Item2, point.Position);
        }
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
                await ShowSlotMenuAsync(_cursor.Value.Date, _cursor.Value.Period, null);
                return;

            case VirtualKey.Delete:
            case VirtualKey.Back:
                e.Handled = true;
                var slot = _book.Resolve(_cursor.Value.Date, _cursor.Value.Period);

                // 보강·대강은 "휴강" 이 아니라 그냥 되돌리는 게 맞다 — 평소에 없던 수업이다.
                if (slot.Kind is LessonChangeKind.Added or LessonChangeKind.Substitute)
                    await _menu.RevertAsync(_cursor.Value.Date, _cursor.Value.Period);
                else if (!slot.IsBlank && slot.Kind != LessonChangeKind.Cancelled)
                    await _menu.CancelAsync(_cursor.Value.Date, _cursor.Value.Period);
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

    /// <summary>그 칸의 메뉴를 연다 — 오른쪽 클릭·Enter·(간단 모드의) 클릭이 모두 여기로 온다.</summary>
    private async Task ShowSlotMenuAsync(DateTime date, int period, Windows.Foundation.Point? at)
    {
        if (!_cells.TryGetValue((date.Date, period), out var border)) return;

        int day = SchoolCalendar.ToLessonDayOfWeek(date);
        if (period > _periods.ForDay(day)) return;

        var menu = await _menu.BuildAsync(date, period);
        if (menu.Items.Count == 0) return;

        if (at is { } point)
            menu.ShowAt(border, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = point });
        else
            menu.ShowAt(border);
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

    #region 칸 메뉴의 뒷일 (ILessonSlotMenuHost)

    XamlRoot? ILessonSlotMenuHost.XamlRoot => XamlRoot;

    /// <summary>메뉴로 그 칸을 바꿨다 — 커서를 그 칸에 두고 다시 그린다.</summary>
    async Task ILessonSlotMenuHost.OnSlotChangedAsync(DateTime date, int period)
    {
        _cursor = (date.Date, period);
        await RefreshFutureCountAsync();
        BuildTable();
    }

    /// <summary>일지를 쓰거나 진도를 표시했다 — 공책 표시를 다시 읽고 바깥에 알린다.</summary>
    async Task ILessonSlotMenuHost.OnRecordChangedAsync()
    {
        if (Compact)
        {
            await _book.LoadJournalMarksAsync();
            BuildTable();
        }
        LessonRecordChanged?.Invoke(this, EventArgs.Empty);
    }

    void ILessonSlotMenuHost.ShowInfo(string message) => ShowInfo(message);
    void ILessonSlotMenuHost.ShowWarning(string message) => ShowWarning(message);

    #endregion

    #region Helper

    private void UpdateStatus()
    {
        int inRange = _book.Changes.Count;

        var text = $"보는 구간 변경 {inRange}건 · 앞으로 등록된 변경 {_futureChangeCount}건";

        if (_cursor != null)
            text += $"   ·   커서 {_cursor.Value.Date:M/d} {_cursor.Value.Period}교시";

        TxtStatus.Text = text;
    }

    private void ShowWarning(string message)
    {
        WeekInfoBar.Severity = InfoBarSeverity.Warning;
        WeekInfoBar.Message = message;
        WeekInfoBar.IsOpen = true;
    }

    /// <summary>한 일이 눈에 보이지 않는 동작(진도 표시)의 확인.</summary>
    private void ShowInfo(string message)
    {
        WeekInfoBar.Severity = InfoBarSeverity.Success;
        WeekInfoBar.Message = message;
        WeekInfoBar.IsOpen = true;
    }

    #endregion
}
