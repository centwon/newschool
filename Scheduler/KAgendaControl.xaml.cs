using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using NewSchool.Controls;
using Windows.UI;
using Windows.UI.Text;

namespace NewSchool.Scheduler;

// ─────────────────────────────────────────────
// AgendaItem — 할 일 또는 일정을 통합 표현하는 ViewModel
// ✅ Ktask → KEvent 통합: SourceEvent만 사용, ItemType으로 구분
// ─────────────────────────────────────────────
public sealed partial class AgendaItem : INotifyPropertyChanged
{
    // 원본 참조 (편집/삭제에 사용) — KEvent 단일 모델
    public KEvent? SourceEvent { get; init; }

    public bool IsTask  => SourceEvent?.ItemType == "task";
    public bool IsEvent => SourceEvent != null && SourceEvent.ItemType != "task";

    // ── 표시용 프로퍼티 ──────────────────────────

    public string Title    => SourceEvent?.Title ?? string.Empty;
    public DateTime SortKey => SourceEvent?.Start ?? DateTime.MinValue;

    /// <summary>리스트 그룹핑용 표시 날짜 (다일 일정은 날짜별로 복제)</summary>
    public DateTime DisplayDate { get; init; }

    /// <summary>
    /// 오늘 자리의 항목인가. 목록은 지난 미완료 할 일부터 두 달 뒤 일정까지 한 줄로 이어지므로
    /// 오늘이 어디서 시작하는지가 눈에 띄어야 한다.
    ///
    /// ⚠ 값을 담아 두지 않고 볼 때마다 계산한다(<see cref="DateLabel"/> 과 같은 규칙). 자정을
    /// 넘기면 낡은 값이 되지만, 그때는 화면이 목록을 다시 읽으면서 항목도 새로 만들어진다.
    /// </summary>
    public bool IsToday => DisplayDate.Date == DateTime.Today;

    /// <summary>
    /// 기한이 지난 할 일인가. 이 목록에서 지난 날짜로 남는 것은 <b>미완료 할 일뿐</b>이고
    /// (일정은 오늘부터 담는다) 그게 목록에서 가장 급한 항목이다.
    ///
    /// 완료 토글을 누르면 그 자리에서 지연이 풀려야 하므로 <see cref="IsTaskDone"/> 이
    /// 바뀔 때 함께 알린다.
    /// </summary>
    public bool IsOverdue => IsTask && !IsTaskDone && DisplayDate.Date < DateTime.Today;

    /// <summary>목록 항목의 날짜 열에 표시할 라벨 (예: "오늘 7/3(금)")</summary>
    public string DateLabel
    {
        get
        {
            string rel = (DisplayDate.Date - DateTime.Today).Days switch
            {
                -1 => "어제 ",
                0 => "오늘 ",
                1 => "내일 ",
                _ => string.Empty
            };
            return $"{rel}{DisplayDate:M월d일}({DisplayDate:ddd})";
        }
    }

    public string TimeLabel
    {
        get
        {
            if (SourceEvent == null) return string.Empty;
            if (IsTask)
                return SourceEvent.IsAllday ? "종일" : SourceEvent.Start.ToString("HH:mm");
            if (SourceEvent.IsAllday) return "종일";
            return $"{SourceEvent.Start:HH:mm}~{SourceEvent.End:HH:mm}";
        }
    }

    /// <summary>분류/캘린더 이름 (배지 표시용)</summary>
    public string CategoryName  { get; init; } = string.Empty;
    /// <summary>배지 배경색 HEX</summary>
    public string BadgeBackground { get; init; } = "#9E9E9E";

    // ── 할 일 전용 ───────────────────────────────

    private bool _isTaskDone;
    public bool IsTaskDone
    {
        get => _isTaskDone;
        set
        {
            if (_isTaskDone == value) return;
            _isTaskDone = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Decorations));
            OnPropertyChanged(nameof(TitleOpacity));
            OnPropertyChanged(nameof(DoneLabel));
            OnPropertyChanged(nameof(IsOverdue));   // 완료하면 그 자리에서 지연 표시가 풀린다
        }
    }

    public string         DoneLabel    => IsTaskDone ? "완료" : "진행";

    /// <summary>
    /// 목록에서 이 줄을 <b>귀로 들었을 때</b> 나올 말 — 54차(키보드만으로 쓸 때).
    ///
    /// <para>⚠ 이 값을 항목 템플릿의 <c>AutomationProperties.Name</c> 에 물리지 않으면
    /// <c>ListView</c> 가 항목 객체의 <c>ToString()</c> 을 읽어, 낭독기가
    /// "NewSchool.Scheduler.AgendaItem" 이라고 소리 내어 읽는다(실측).</para>
    /// </summary>
    public string AccessibleText
    {
        get
        {
            string when = string.IsNullOrEmpty(TimeLabel) ? DateLabel : $"{DateLabel} {TimeLabel}";
            string what = IsTask ? $"할 일, {DoneLabel}" : "일정";
            string cat = string.IsNullOrEmpty(CategoryName) ? string.Empty : $", {CategoryName}";
            return $"{when}{cat}, {Title}, {what}";
        }
    }
    public TextDecorations Decorations  => IsTaskDone ? TextDecorations.Strikethrough : TextDecorations.None;
    public double          TitleOpacity => IsTaskDone ? 0.45 : 1.0;

    // ── 정적 팩토리 ──────────────────────────────

    /// <summary>KEvent(ItemType="task")에서 AgendaItem 생성</summary>
    public static AgendaItem FromTask(KEvent taskEvent, string categoryName, string badgeColor, DateTime? displayDate = null) => new()
    {
        SourceEvent     = taskEvent,
        CategoryName    = categoryName,
        BadgeBackground = badgeColor,
        DisplayDate     = displayDate ?? taskEvent.Start.Date,
        _isTaskDone     = taskEvent.IsDone
    };

    /// <summary>KEvent(ItemType="event")에서 AgendaItem 생성</summary>
    public static AgendaItem FromEvent(KEvent ev, string calendarName, string calendarColor, DateTime? displayDate = null)
    {
        string hex = !string.IsNullOrEmpty(ev.ColorId)
            ? KEvent.ColorIdToHex(ev.ColorId)
            : calendarColor;
        if (string.IsNullOrEmpty(hex)) hex = calendarColor;

        return new AgendaItem
        {
            SourceEvent     = ev,
            CategoryName    = calendarName,
            BadgeBackground = hex,
            DisplayDate     = displayDate ?? ev.Start.Date
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// ─────────────────────────────────────────────
// KAgendaControl — 통합 할 일 + 일정 컨트롤
// ✅ Ktask → KEvent 통합: KCalendarList 기반 필터
// ─────────────────────────────────────────────
public sealed partial class KAgendaControl : UserControl
{
    private List<KCalendarList> _calendars  = new();
    private List<AgendaItem>    _allItems   = new();
    private bool _filterInitialized = false;
    private int  _selectedCalendarId = 0;
    private bool _showTasks  = true;
    private bool _showEvents = true;

    /// <summary>새 항목 추가 시 기본 CalendarId</summary>
    public int DefaultCalendarId { get; set; }

    /// <summary>필터 UI(캘린더 ComboBox, 할일/일정 토글) 표시 여부</summary>
    public bool ShowFilter { get; set; } = true;

    /// <summary>캘린더(카테고리) 이름 고정 (설정 시 해당 캘린더만 표시, 필터 자동 숨김)</summary>
    public string? FixedCalendarName { get; set; }

    public KAgendaControl()
    {
        InitializeComponent();
    }

    // ─────────────────────────────────────────────
    // x:Bind용 정적 헬퍼
    // ─────────────────────────────────────────────

    /// <summary>HEX 문자열 → SolidColorBrush (x:Bind에서 직접 호출)</summary>
    public static SolidColorBrush HexToBrush(string hex)
    {
        try
        {
            hex = (hex ?? "#9E9E9E").TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex[0..2], 16);
                byte g = Convert.ToByte(hex[2..4], 16);
                byte b = Convert.ToByte(hex[4..6], 16);
                return new SolidColorBrush(Color.FromArgb(255, r, g, b));
            }
        }
        catch { /* 파싱 실패 시 기본 */ }
        return new SolidColorBrush(Colors.Gray);
    }

    // ── 지연 · 오늘 강조 ─────────────────────────
    //
    // 이 목록은 지난 미완료 할 일부터 두 달 뒤 일정까지 한 줄로 이어진다. 눈이 먼저 가야 할
    // 자리는 둘이다 — 기한이 지난 할 일(가장 급하다)과 오늘(기준점).
    //
    // 배경 하나만으로는 약하다. 이 줄에는 이미 분류 배지라는 짙은 색이 있어서 배경까지 칠하면
    // "이 항목의 색"으로 오해되기 쉽고, 목록이 ListView 라 선택·마우스오버도 배경으로 표시된다.
    // 그래서 ①날짜 라벨을 색·굵기로 세우고 ②배경은 그 위에 옅게만 깐다.
    // 배경은 반투명 계열 테마 브러시라 선택 표시가 그 아래로 비쳐 보인다.
    //
    // 지연과 오늘은 겹치지 않는다(지연은 오늘보다 앞선 날짜다). 그래도 지연을 먼저 본다.
    //
    // ⚠ 브러시를 지금 한 번 꺼내 오므로 설정에서 밝게/어둡게를 바꿔도 이미 그려진 줄은
    // 옛 색을 유지한다(목록을 다시 읽으면 맞춰진다). TimetableControl 의 변경 칸도 같은 방식이다.

    private static readonly SolidColorBrush Transparent = new(Colors.Transparent);

    /// <summary>
    /// 테마 브러시를 이름으로 꺼낸다. 없는 이름이면 예외 대신 대체색을 준다 —
    /// 목록 한 줄의 색 때문에 화면이 통째로 못 그려지면 곤란하다.
    /// </summary>
    private static Brush ThemeBrush(string key, Brush fallback)
        => Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : fallback;

    /// <summary>줄 배경 — 지연은 경고 틴트, 오늘은 강조 틴트, 나머지는 투명.</summary>
    public static Brush RowBackground(bool isOverdue, bool isToday)
    {
        if (isOverdue) return ThemeBrush("SystemFillColorCautionBackgroundBrush", Transparent);
        if (isToday) return ThemeBrush("SystemFillColorAttentionBackgroundBrush", Transparent);
        return Transparent;
    }

    /// <summary>날짜 라벨 색 — 지연은 경고색, 오늘은 강조색, 나머지는 보조 텍스트색.</summary>
    public static Brush DateLabelBrush(bool isOverdue, bool isToday)
    {
        var normal = ThemeBrush("TextFillColorTertiaryBrush", Transparent);
        if (isOverdue) return ThemeBrush("SystemFillColorCautionBrush", normal);
        if (isToday) return ThemeBrush("AccentTextFillColorPrimaryBrush", normal);
        return normal;
    }

    /// <summary>날짜 라벨 굵기 — 지연·오늘만 굵게.</summary>
    // FontWeight 는 Windows.UI.Text, FontWeights 는 Microsoft.UI.Text — 짝이 갈려 있어 한정한다.
    public static FontWeight DateLabelWeight(bool isOverdue, bool isToday)
        => isOverdue || isToday
            ? Microsoft.UI.Text.FontWeights.SemiBold
            : Microsoft.UI.Text.FontWeights.Normal;

    // ─────────────────────────────────────────────
    // 공개 로드 메서드
    // ─────────────────────────────────────────────

    /// <summary>
    /// 마지막으로 부른 로드 방법. 항목을 추가·수정·삭제한 뒤 DB 에서 다시 읽어 오는 데 쓴다
    /// (<see cref="ReloadAsync"/>). 화면마다 로드 방법과 범위가 달라서 기억해 둔다.
    /// </summary>
    private Func<Task>? _reload;

    /// <summary>
    /// 목록을 DB 에서 다시 읽는다.
    ///
    /// 대화상자가 만든 결과를 목록에 손으로 끼워 넣는 방식은 <b>1건 대 N건</b>에서 어긋났다.
    /// 반복 할 일은 한 번에 여러 건이 저장되는데 대화상자는 대표 1건만 돌려주고, "이후 반복
    /// 항목 모두 삭제"는 여러 건을 지우는데 화면에서는 누른 1건만 사라졌다(남은 줄을 다시
    /// 누르면 이미 없는 행을 편집하게 된다). 저장·삭제 뒤에는 그냥 다시 읽는다.
    /// </summary>
    private async Task ReloadAsync()
    {
        if (_reload != null) await _reload();
        else ApplyFilter();
    }

    /// <summary>오늘 기준 미완료 + 미래 60일 로드 (TodayPage용)</summary>
    public async Task LoadPendingAndFutureAsync()
    {
        _reload = () => LoadPendingAndFutureAsync();
        try
        {
            await EnsureFiltersAsync();
            using var svc = Scheduler.CreateService();

            // task 항목: 과거 미완료 + 미래 전체
            var tasks = await svc.GetPendingAndFutureTasksAsync();
            // event 항목: 60일 범위 (task·학사일정 제외 — ExcludedFromAgenda 참고)
            var events = await svc.GetEventsByDateAsync(DateTime.Today, 60);
            var calendarEvents = events.Where(e => !ExcludedFromAgenda(e)).ToList();

            var allItems = new List<KEvent>(tasks.Count + calendarEvents.Count);
            allItems.AddRange(tasks);
            allItems.AddRange(calendarEvents);

            // 일정은 오늘부터 60일까지만 펼친다(지난 날짜로 남는 것은 미완료 할 일뿐이어야 한다)
            BuildAllItems(allItems, DateTime.Today, DateTime.Today.AddDays(59));
            ApplyFilter();
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Error("KAgendaControl", "할 일·일정 목록을 읽지 못했다 — 비어 보인다", ex);
        }
    }

    /// <summary>
    /// 아젠다 목록에서 뺄 항목인가.
    ///
    /// · task — 이 목록은 event 와 task 를 따로 담으므로 event 쪽에서 중복 제외한다.
    /// · schoolschedule — 학사일정은 이 컨트롤을 쓰는 화면마다 이미 제 자리가 있다
    ///   (오늘 화면의 학사일정 카드·헤더, 달력의 날짜 옆 DateName). 여기서 또 뿌리면
    ///   같은 일정이 한 화면에 두 번 나온다. <c>DayCell</c> 과 같은 규칙을 쓴다.
    /// </summary>
    private static bool ExcludedFromAgenda(KEvent e)
        => e.ItemType == "task" || e.ItemType == "schoolschedule";

    /// <summary>날짜 범위 지정 로드</summary>
    public async Task LoadByDateRangeAsync(DateTime start, int days = 30, bool showCompleted = true)
    {
        _reload = () => LoadByDateRangeAsync(start, days, showCompleted);
        try
        {
            await EnsureFiltersAsync();
            using var svc = Scheduler.CreateService();

            // task 항목: 범위 내 (ItemType="task"만)
            var tasks = await svc.GetTasksByDateAsync(start, days, showCompleted);
            // event 항목: 범위 내 (task·학사일정 제외 — ExcludedFromAgenda 참고)
            var events = await svc.GetEventsByDateAsync(start, days);
            var calendarEvents = events.Where(e => !ExcludedFromAgenda(e)).ToList();

            var allItems = new List<KEvent>(tasks.Count + calendarEvents.Count);
            allItems.AddRange(tasks);
            allItems.AddRange(calendarEvents);

            BuildAllItems(allItems, start.Date, start.Date.AddDays(Math.Max(days, 1) - 1));
            ApplyFilter();
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Error("KAgendaControl", "기간 일정을 읽지 못했다 — 비어 보인다", ex);
        }
    }

    // ─────────────────────────────────────────────
    // 내부: 필터 ComboBox 초기화 (1회)
    // ─────────────────────────────────────────────
    private async Task EnsureFiltersAsync()
    {
        if (_filterInitialized) return;
        try
        {
            using var svc = Scheduler.CreateService();
            _calendars = await svc.GetAllCalendarsAsync();

            var names = new List<string> { "전체" };
            names.AddRange(_calendars.Select(c => c.Title));
            CBoxFilter.ItemsSource   = names;
            CBoxFilter.SelectedIndex = 0;
            TbTask.IsChecked  = true;
            TbEvent.IsChecked = true;
            _filterInitialized = true;

            // FixedCalendarName이 설정되면 해당 캘린더로 고정
            if (!string.IsNullOrEmpty(FixedCalendarName))
            {
                var fixedCal = _calendars.FirstOrDefault(c => c.Title == FixedCalendarName);
                if (fixedCal != null)
                {
                    _selectedCalendarId = fixedCal.No;
                    DefaultCalendarId   = fixedCal.No;
                }
                // 고정 시 필터 자동 숨김
                CBoxFilter.Visibility = Visibility.Collapsed;
                TbTask.Visibility     = Visibility.Collapsed;
                TbEvent.Visibility    = Visibility.Collapsed;
            }
            else if (!ShowFilter)
            {
                CBoxFilter.Visibility = Visibility.Collapsed;
                TbTask.Visibility     = Visibility.Collapsed;
                TbEvent.Visibility    = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Error("KAgendaControl", "달력 목록(필터)을 준비하지 못했다", ex);
        }
    }

    // ─────────────────────────────────────────────
    // 내부: 원시 데이터 → AgendaItem 변환
    // ─────────────────────────────────────────────
    /// <summary>
    /// 조회한 KEvent 를 목록 항목으로 바꾼다.
    /// </summary>
    /// <param name="windowStart">
    /// 표시 창의 첫날. 다일 일정을 날짜별로 펼칠 때 <b>이 날 앞으로는 펼치지 않는다</b>.
    /// 예전에는 자르지 않아서, 지난주에 시작해 다음 주에 끝나는 일정이 있으면 "오늘 이후"
    /// 목록에 지난주 날짜 행이 함께 생기고 날짜순 정렬 탓에 맨 위에 왔다.
    /// (달력 쪽 <c>Kcalendar.UpdateCellsDisplayAsync</c> 는 이미 창으로 자른다.)
    /// </param>
    /// <param name="windowEnd">표시 창의 마지막 날(포함).</param>
    private void BuildAllItems(List<KEvent> allEvents, DateTime windowStart, DateTime windowEnd)
    {
        _allItems = new List<AgendaItem>(allEvents.Count);

        foreach (var ev in allEvents)
        {
            var cal = _calendars.FirstOrDefault(c => c.No == ev.CalendarId);
            // 캘린더 조회 실패(CalendarId=0 등 레거시 데이터)시에도 배지가 빈 이름 때문에
            // 통째로 숨겨지지 않도록 항상 표시 가능한 이름/색을 보장
            string name  = string.IsNullOrEmpty(cal?.Title) ? "기타" : cal.Title;
            string color = string.IsNullOrEmpty(cal?.Color) ? "#9E9E9E" : cal.Color;

            if (ev.ItemType == "task")
            {
                _allItems.Add(AgendaItem.FromTask(ev, name, color));
            }
            else
            {
                // 다일 일정: 각 날짜별로 AgendaItem 생성 (End는 inclusive).
                // 표시 창 밖으로는 펼치지 않는다 — 창 앞뒤로 걸친 일정이 목록에 넘쳐 나온다.
                var first = ev.Start.Date < windowStart ? windowStart : ev.Start.Date;
                var last  = ev.End.Date   > windowEnd   ? windowEnd   : ev.End.Date;
                if (last < first) last = first;

                for (var day = first; day <= last; day = day.AddDays(1))
                {
                    _allItems.Add(AgendaItem.FromEvent(ev, name, color, displayDate: day));
                }
            }
        }
    }

    // ─────────────────────────────────────────────
    // 내부: 필터 적용 + 목록 바인딩
    // ─────────────────────────────────────────────
    private void ApplyFilter()
    {
        var filtered = _allItems.AsEnumerable();

        if (!_showTasks)  filtered = filtered.Where(i => !i.IsTask);
        if (!_showEvents) filtered = filtered.Where(i => !i.IsEvent);

        // 캘린더 필터 — CalendarId 기준
        if (_selectedCalendarId > 0)
        {
            filtered = filtered.Where(i =>
                i.SourceEvent != null && i.SourceEvent.CalendarId == _selectedCalendarId);
        }

        // 단일 평탄 리스트 — 각 행에 날짜 열이 있으므로 날짜별 헤더로 나눌 필요 없음
        AgendaListView.ItemsSource = filtered
            .OrderBy(i => i.DisplayDate)
            .ThenBy(i => i.SortKey)
            .ToList();
    }

    // ─────────────────────────────────────────────
    // 이벤트 핸들러
    // ─────────────────────────────────────────────

    private void CBoxFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filterInitialized) return;
        int idx = CBoxFilter.SelectedIndex;
        _selectedCalendarId = (idx <= 0) ? 0 : _calendars[idx - 1].No;
        DefaultCalendarId   = _selectedCalendarId;
        ApplyFilter();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_filterInitialized) return;  // XAML 초기화 중 발생하는 Checked 이벤트 무시
        _showTasks  = TbTask.IsChecked  == true;
        _showEvents = TbEvent.IsChecked == true;
        ApplyFilter();
    }

    private async void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new UnifiedItemDialog(DateTime.Today) { XamlRoot = XamlRoot };
            var result = await MessageBox.ShowDialogAsync(dialog);

            // 반복 할 일은 대화상자가 여러 건을 저장하고 대표 1건만 돌려준다 — 다시 읽는다
            if (result == ContentDialogResult.Primary && dialog.ResultEvent != null)
                await ReloadAsync();
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Error("KAgendaControl", "새 일정 창을 열지 못했다 — 눌러도 아무 일이 없어 보인다", ex);
        }
    }

    private async void AgendaListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not AgendaItem item || item.SourceEvent == null) return;
        try
        {
            var dialog = new UnifiedItemDialog(item.SourceEvent) { XamlRoot = XamlRoot };
            var result = await MessageBox.ShowDialogAsync(dialog);

            // 삭제(Secondary)는 "이후 반복 항목 모두"를 지울 수 있어 지운 건수가 1건이 아니다.
            // 수정도 날짜가 바뀌면 표시 위치가 달라지므로, 둘 다 DB 에서 다시 읽는다.
            if ((result == ContentDialogResult.Primary && dialog.ResultEvent != null)
                || result == ContentDialogResult.Secondary)
            {
                await ReloadAsync();
            }
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Error("KAgendaControl", "일정 편집 창을 열지 못했다 — 눌러도 아무 일이 없어 보인다", ex);
        }
    }

    private async void TaskToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.Controls.Primitives.ToggleButton btn ||
            btn.Tag is not AgendaItem item || item.SourceEvent == null || !item.IsTask)
            return;
        var taskEvent = item.SourceEvent;
        bool prevIsDone = taskEvent.IsDone;
        string prevUpdated = taskEvent.Updated;
        string prevCompleted = taskEvent.Completed;

        try
        {
            taskEvent.IsDone  = btn.IsChecked == true;
            taskEvent.Updated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            taskEvent.Completed = taskEvent.IsDone ? taskEvent.Updated : string.Empty;
            item.IsTaskDone = taskEvent.IsDone;

            using var svc = Scheduler.CreateService();

            // 반영 여부를 확인한다. 예전에는 결과도 예외도 보지 않아, 화면은 완료인데
            // DB 는 미완료로 갈라진 채 다음 새로고침에 소리 없이 되돌아갔다.
            // (달력 셀은 20차에 고쳤는데 목록 보기만 남아 있었다.)
            if (!await svc.UpdateTaskAsync(taskEvent))
                throw new InvalidOperationException("변경된 항목이 없습니다. 이미 지워진 할 일일 수 있습니다.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KAgendaControl] TaskToggle_Click 오류: {ex}");

            taskEvent.IsDone = prevIsDone;
            taskEvent.Updated = prevUpdated;
            taskEvent.Completed = prevCompleted;
            item.IsTaskDone = prevIsDone;
            btn.IsChecked = prevIsDone;

            await Controls.UserErrorReporter.ReportAsync("할 일 완료 표시", ex);
        }
    }
}
