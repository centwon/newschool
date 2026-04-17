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
using Windows.UI;
using Windows.UI.Text;

namespace NewSchool.Scheduler;

// ─────────────────────────────────────────────
// AgendaItem — 할 일 또는 일정을 통합 표현하는 ViewModel
// ✅ Ktask → KEvent 통합: SourceEvent만 사용, ItemType으로 구분
// ─────────────────────────────────────────────
[WinRT.GeneratedBindableCustomProperty]
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

    public string TypeIcon  => IsTask ? (IsTaskDone ? "●" : "○") : "▶";
    public string AccentColor => IsTask
        ? (IsTaskDone ? "#AAAAAA" : "#1565C0")
        : (string.IsNullOrEmpty(_accentHex) ? "#4285F4" : _accentHex);

    private string _accentHex = string.Empty;

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
            OnPropertyChanged(nameof(TypeIcon));
            OnPropertyChanged(nameof(AccentColor));
            OnPropertyChanged(nameof(Decorations));
            OnPropertyChanged(nameof(TitleOpacity));
            OnPropertyChanged(nameof(DoneLabel));
        }
    }

    public string         DoneLabel    => IsTaskDone ? "완료" : "진행";
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
            DisplayDate     = displayDate ?? ev.Start.Date,
            _accentHex      = hex
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// ─────────────────────────────────────────────
// AgendaHeader — 날짜 헤더 (평탄 리스트용)
// ─────────────────────────────────────────────
[WinRT.GeneratedBindableCustomProperty]
public sealed partial class AgendaHeader
{
    public string DateHeader { get; init; } = string.Empty;
    public string CountLabel { get; init; } = string.Empty;

    public static (AgendaHeader header, List<AgendaItem> items) Create(DateTime date, List<AgendaItem> items)
    {
        int dayDiff = (date.Date - DateTime.Today).Days;
        string rel = dayDiff switch
        {
            -2 => "그제",
            -1 => "어제",
             0 => "오늘",
             1 => "내일",
             2 => "모레",
             _ => string.Empty
        };
        string dow     = date.ToString("ddd");
        string dateStr = date.ToString("M월 d일");
        string header  = string.IsNullOrEmpty(rel)
            ? $"{dateStr} ({dow})"
            : $"{rel} — {dateStr} ({dow})";

        int taskCnt  = items.Count(i => i.IsTask);
        int eventCnt = items.Count(i => i.IsEvent);
        var parts = new List<string>();
        if (taskCnt  > 0) parts.Add($"할 일 {taskCnt}");
        if (eventCnt > 0) parts.Add($"일정 {eventCnt}");
        string countLabel = parts.Count > 0 ? $"({string.Join(", ", parts)})" : string.Empty;

        return (new AgendaHeader { DateHeader = header, CountLabel = countLabel }, items);
    }
}

// ─────────────────────────────────────────────
// AgendaTemplateSelector — Native AOT 안전한 템플릿 선택
// ─────────────────────────────────────────────
public sealed partial class AgendaTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HeaderTemplate { get; set; }
    public DataTemplate? ItemTemplate   { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
        => item is AgendaHeader ? HeaderTemplate : ItemTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
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

    // ─────────────────────────────────────────────
    // 공개 로드 메서드
    // ─────────────────────────────────────────────

    /// <summary>오늘 기준 미완료 + 미래 60일 로드 (TodayPage용)</summary>
    public async Task LoadPendingAndFutureAsync()
    {
        try
        {
            await EnsureFiltersAsync();
            using var svc = Scheduler.CreateService();

            // task 항목: 과거 미완료 + 미래 전체
            var tasks = await svc.GetPendingAndFutureTasksAsync();
            // event 항목: 60일 범위 (task 제외)
            var events = await svc.GetEventsByDateAsync(DateTime.Today, 60);
            var calendarEvents = events.Where(e => e.ItemType != "task").ToList();

            var allItems = new List<KEvent>(tasks.Count + calendarEvents.Count);
            allItems.AddRange(tasks);
            allItems.AddRange(calendarEvents);

            BuildAllItems(allItems);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KAgendaControl] LoadPendingAndFutureAsync 오류: {ex.Message}");
        }
    }

    /// <summary>날짜 범위 지정 로드</summary>
    public async Task LoadByDateRangeAsync(DateTime start, int days = 30, bool showCompleted = true)
    {
        try
        {
            await EnsureFiltersAsync();
            using var svc = Scheduler.CreateService();

            // task 항목: 범위 내 (ItemType="task"만)
            var tasks = await svc.GetTasksByDateAsync(start, days, showCompleted);
            // event 항목: 범위 내 (task 제외)
            var events = await svc.GetEventsByDateAsync(start, days);
            var calendarEvents = events.Where(e => e.ItemType != "task").ToList();

            var allItems = new List<KEvent>(tasks.Count + calendarEvents.Count);
            allItems.AddRange(tasks);
            allItems.AddRange(calendarEvents);

            BuildAllItems(allItems);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KAgendaControl] LoadByDateRangeAsync 오류: {ex.Message}");
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
            Debug.WriteLine($"[KAgendaControl] EnsureFiltersAsync 오류: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────
    // 내부: 원시 데이터 → AgendaItem 변환
    // ─────────────────────────────────────────────
    private void BuildAllItems(List<KEvent> allEvents)
    {
        _allItems = new List<AgendaItem>(allEvents.Count);

        foreach (var ev in allEvents)
        {
            var cal = _calendars.FirstOrDefault(c => c.No == ev.CalendarId);
            string name  = cal?.Title ?? string.Empty;
            string color = cal?.Color ?? "#4285F4";

            if (ev.ItemType == "task")
            {
                _allItems.Add(AgendaItem.FromTask(ev, name, color));
            }
            else
            {
                // 다일 일정: 각 날짜별로 AgendaItem 생성 (End는 inclusive)
                int days = (ev.End.Date - ev.Start.Date).Days;
                if (days < 0) days = 0;

                for (int d = 0; d <= days; d++)
                {
                    _allItems.Add(AgendaItem.FromEvent(ev, name, color,
                        displayDate: ev.Start.Date.AddDays(d)));
                }
            }
        }
    }

    // ─────────────────────────────────────────────
    // 내부: 필터 적용 + GroupedView 바인딩
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

        // 평탄 리스트: 날짜 헤더 + 항목 교차 배치 (Native AOT 안전)
        var flatList = new List<object>();
        foreach (var g in filtered.OrderBy(i => i.DisplayDate).ThenBy(i => i.SortKey).GroupBy(i => i.DisplayDate))
        {
            var items = g.ToList();
            var (header, _) = AgendaHeader.Create(g.Key, items);
            flatList.Add(header);
            flatList.AddRange(items);
        }
        AgendaListView.ItemsSource = flatList;
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
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && dialog.ResultEvent != null)
            {
                var ev  = dialog.ResultEvent;
                var cal = _calendars.FirstOrDefault(c => c.No == ev.CalendarId);
                string name  = cal?.Title ?? string.Empty;
                string color = cal?.Color ?? "#4285F4";

                if (ev.ItemType == "task")
                {
                    _allItems.Add(AgendaItem.FromTask(ev, name, color));
                }
                else
                {
                    int days = (ev.End.Date - ev.Start.Date).Days;
                    if (days <= 0) days = 0;
                    for (int d = 0; d <= days; d++)
                        _allItems.Add(AgendaItem.FromEvent(ev, name, color,
                            displayDate: ev.Start.Date.AddDays(d)));
                }

                ApplyFilter();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KAgendaControl] BtnAdd_Click 오류: {ex.Message}");
        }
    }

    private async void AgendaListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not AgendaItem item || item.SourceEvent == null) return;
        try
        {
            var dialog = new UnifiedItemDialog(item.SourceEvent) { XamlRoot = XamlRoot };
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && dialog.ResultEvent != null)
            {
                var ev  = dialog.ResultEvent;
                var cal = _calendars.FirstOrDefault(c => c.No == ev.CalendarId);
                string name  = cal?.Title ?? string.Empty;
                string color = cal?.Color ?? "#4285F4";

                // 같은 SourceEvent를 참조하는 항목 모두 제거 (다일 일정 복제본 포함)
                _allItems.RemoveAll(a => a.SourceEvent == item.SourceEvent);

                if (ev.ItemType == "task")
                {
                    _allItems.Add(AgendaItem.FromTask(ev, name, color));
                }
                else
                {
                    int days = (ev.End.Date - ev.Start.Date).Days;
                    if (days <= 0) days = 0;
                    for (int d = 0; d <= days; d++)
                        _allItems.Add(AgendaItem.FromEvent(ev, name, color,
                            displayDate: ev.Start.Date.AddDays(d)));
                }
                ApplyFilter();
            }
            else if (result == ContentDialogResult.Secondary)
            {
                // 삭제: 같은 SourceEvent를 참조하는 항목 모두 제거
                _allItems.RemoveAll(a => a.SourceEvent == item.SourceEvent);
                ApplyFilter();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KAgendaControl] 항목 편집 오류: {ex.Message}");
        }
    }

    private async void TaskToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.Controls.Primitives.ToggleButton btn ||
            btn.Tag is not AgendaItem item || item.SourceEvent == null || !item.IsTask)
            return;
        try
        {
            var taskEvent = item.SourceEvent;
            taskEvent.IsDone  = btn.IsChecked == true;
            taskEvent.Updated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            taskEvent.Completed = taskEvent.IsDone ? taskEvent.Updated : string.Empty;
            item.IsTaskDone = taskEvent.IsDone;

            using var svc = Scheduler.CreateService();
            await svc.UpdateTaskAsync(taskEvent);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KAgendaControl] TaskToggle_Click 오류: {ex.Message}");
        }
    }
}
