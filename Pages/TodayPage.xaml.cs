using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NewSchool.Board;
using NewSchool.Controls;
using NewSchool.Board.Services;
using NewSchool.Dialogs;
using NewSchool.Helpers;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;
using NewSchool.ViewModels;

namespace NewSchool.Pages;

/// <summary>
/// 오늘의 할일, 학사일정, 급식 등을 표시하는 메인 페이지
/// 상단 날짜 헤더(오늘 날짜·요일·오늘 행사·현재 교시) + 내 수업/우리 반 오늘 시간표.
/// (Avalonia SaemDesk TodayPage 설계 이식)
/// </summary>
public sealed partial class TodayPage : Page, INotifyPropertyChanged, ILessonSlotMenuHost
{
    private TodayPageViewModel? _viewModel;

    private DispatcherQueueTimer? _periodTimer;   // 현재 교시 배지 1분 주기 갱신
    private bool _headerInitialized;

    /// <summary>
    /// 보고 있는 날짜. 이 날짜를 따르는 것은 <b>내 수업 · 우리 반 · 급식 · 그날 행사</b> 넷뿐이다 —
    /// 학사일정 목록·할 일·메모는 원래 "앞으로"를 보는 카드라 오늘 기준으로 둔다.
    /// </summary>
    private DateTime _viewDate = DateTime.Today;

    /// <summary>마지막으로 확인한 "오늘" (자정 롤오버 감지용)</summary>
    private DateTime _knownToday = DateTime.Today;

    private bool IsViewingToday => _viewDate == DateTime.Today;
    private readonly bool _isHomeroom = Settings.HomeGrade.Value > 0 && Settings.HomeRoom.Value > 0;

    /// <summary>내 수업 — 기초 + 그 날 변경 + 일지 표시. 수업 홈 시간표와 같은 장부·같은 칸 메뉴를 쓴다.</summary>
    private readonly LessonSlotBook _book = new();
    private readonly LessonSlotMenu _menu;

    /// <summary>우리 반 — 보고 있는 날의 교시 → 학급 시간표 칸 (담임일 때만)</summary>
    private Dictionary<int, ClassTimetable> _classSlots = [];

    /// <summary>시간표 카드의 줄들 (현재 교시 강조를 1분 주기로 다시 매긴다)</summary>
    private List<TodayPeriodRow> _rows = [];

    /// <summary>
    /// ViewModel - x:Bind를 위한 public 속성
    /// </summary>
    public TodayPageViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel != value)
            {
                _viewModel = value;
                OnPropertyChanged();
            }
        }
    }

    public TodayPage()
    {
        InitializeComponent();
        _menu = new LessonSlotMenu(_book, this);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // ViewModel 의 PropertyChanged 를 구독하던 줄은 지웠다(2026-08-30) —
        // 핸들러 본문이 비어 있어, 구독·해제만 하고 아무 일도 하지 않았다.
        // 화면은 x:Bind 로 ViewModel 을 직접 읽는다.
        ViewModel = new TodayPageViewModel(DispatcherQueue);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        _periodTimer?.Stop();

        if (ViewModel != null)
        {
            ViewModel.SchoolEvents.Clear();
            ViewModel.Tasks.Clear();
            ViewModel.Meals = null;
        }

        ViewModel = null;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // 1. 헤더/시간표 UI 1회 초기화 (담임 아니면 '우리 반' 열 접기)
        if (!_headerInitialized)
        {
            _headerInitialized = true;

            UpdateDateHeader();

            if (!_isHomeroom)
            {
                ClassColumn.Width = new GridLength(0);
                ClassHeaderCell.Visibility = Visibility.Collapsed;
            }
        }

        // 2. 현재 교시: 즉시 1회 + 1분 주기 타이머
        UpdateCurrentPeriod();
        _periodTimer ??= CreatePeriodTimer();
        _periodTimer.Start();

        // 3. 데이터 병렬 로드 (개별 실패는 서로 영향 없음)
        await LoadTodayDataAsync();
    }

    private async Task LoadTodayDataAsync()
    {
        // 섹션 하나가 실패해도 나머지는 보여주되, 실패했다는 사실은 알린다.
        //
        // ⚠ 예전에는 SafeLoadAsync 가 실패를 Debug 로그로만 남겨서, 급식이나 시간표를
        //    못 불러와도 화면상 "오늘은 없음"과 구분되지 않았다.
        var failed = new List<string>();

        try
        {
            await Task.WhenAll(
                SafeLoadAsync("오늘 시간표", LoadTimetableSlotsAsync, failed),
                SafeLoadAsync("오늘 행사",  LoadTodayEventAsync, failed),
                SafeLoadAsync("학사일정",  () => ScheduleList.LoadSchedulesAsync(DateTime.Today, 28, true), failed),
                SafeLoadAsync("할 일/일정", () => AgendaList.LoadPendingAndFutureAsync(), failed),
                SafeLoadAsync("급식",      () => MealBox.LoadMealsAsync(_viewDate), failed)
            );
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Error("TodayPage", "오늘 화면을 통째로 채우지 못했다", ex);
        }

        if (failed.Count > 0 && App.MainWindow is MainWindow main)
        {
            main.ShowGlobalWarning(
                "일부 정보를 불러오지 못했습니다",
                $"{string.Join(", ", failed)} — 새로고침하거나 잠시 후 다시 확인해주세요.");
        }
    }

    /// <summary>보는 날짜와 상관없이 오늘을 기준으로 하는 두 카드만 다시 읽는다.</summary>
    private async Task ReloadTodayAnchoredAsync()
    {
        var failed = new List<string>();

        await Task.WhenAll(
            SafeLoadAsync("학사일정",  () => ScheduleList.LoadSchedulesAsync(DateTime.Today, 28, true), failed),
            SafeLoadAsync("할 일/일정", () => AgendaList.LoadPendingAndFutureAsync(), failed));

        if (failed.Count > 0 && App.MainWindow is MainWindow main)
        {
            main.ShowGlobalWarning(
                "일부 정보를 불러오지 못했습니다",
                $"{string.Join(", ", failed)} — 새로고침하거나 잠시 후 다시 확인해주세요.");
        }
    }

    /// <summary>
    /// 날짜 헤더 갱신. 오늘이 아니면 [오늘] 버튼과 안내를 띄우고 현재 교시 배지를 감춘다 —
    /// 다른 날짜에서 "3교시"는 참이 아니다.
    /// </summary>
    private void UpdateDateHeader()
    {
        TxtTodayDate.Text = _viewDate.ToString("yyyy년 M월 d일");
        TxtTodayDow.Text = GetKoreanDayOfWeek(_viewDate.DayOfWeek);

        bool today = IsViewingToday;

        BtnBackToToday.Visibility = today ? Visibility.Collapsed : Visibility.Visible;
        TxtOtherDayNotice.Visibility = today ? Visibility.Collapsed : Visibility.Visible;
        CurrentPeriodBadge.Visibility = today ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnPreviousDayClick(object sender, RoutedEventArgs e)
        => await MoveToAsync(_viewDate.AddDays(-1));

    private async void OnNextDayClick(object sender, RoutedEventArgs e)
        => await MoveToAsync(_viewDate.AddDays(1));

    private async void OnBackToTodayClick(object sender, RoutedEventArgs e)
        => await MoveToAsync(DateTime.Today);

    /// <summary>
    /// 날짜를 옮기고 그 날짜를 따르는 카드만 다시 읽는다.
    /// 학사일정 목록·할 일·메모는 건드리지 않는다 — 오늘 기준으로 두는 카드다.
    /// </summary>
    private async Task MoveToAsync(DateTime date)
    {
        if (_viewDate == date.Date) return;

        _viewDate = date.Date;
        UpdateDateHeader();
        UpdateCurrentPeriod();

        var failed = new List<string>();

        await Task.WhenAll(
            SafeLoadAsync("시간표", LoadTimetableSlotsAsync, failed),
            SafeLoadAsync("행사", LoadTodayEventAsync, failed),
            SafeLoadAsync("급식", () => MealBox.LoadMealsAsync(_viewDate), failed));

        if (failed.Count > 0 && App.MainWindow is MainWindow main)
        {
            main.ShowGlobalWarning(
                "일부 정보를 불러오지 못했습니다",
                $"{string.Join(", ", failed)} — 다시 시도해주세요.");
        }
    }

    private static async Task SafeLoadAsync(string name, Func<Task> load, List<string> failed)
    {
        try
        {
            await load();
        }
        catch (Exception ex)
        {
            // 사용자에게는 이름만 알린다(위 InfoBar). 왜 실패했는지는 여기서만 남는다.
            NewSchool.Logging.Log.Error("TodayPage", $"{name} 을(를) 불러오지 못했다", ex);
            lock (failed) failed.Add(name);
        }
    }

    #region 상단 날짜 헤더 / 현재 교시

    private DispatcherQueueTimer CreatePeriodTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMinutes(1);
        // 약하게 잇는다 — 람다로 이으면 홈 페이지가 수거되지 않는다(TimerTick 주석).
        timer.TickWeakly(this, static page => page.UpdateCurrentPeriod());
        return timer;
    }

    private void UpdateCurrentPeriod()
    {
        // 앱을 켜둔 채 자정을 넘기면 날짜 헤더·시간표·급식이 어제 것으로 남으므로
        // 1분 주기 타이머에서 날짜 변경을 감지해 전체를 다시 로드한다.
        // 다른 날짜를 보고 있는 중이라면 끌고 가지 않는다 — 보던 화면이 멋대로 바뀐다.
        if (_headerInitialized && _knownToday != DateTime.Today)
        {
            bool wasViewingToday = _viewDate == _knownToday;
            _knownToday = DateTime.Today;

            if (wasViewingToday)
            {
                _viewDate = DateTime.Today;
                UpdateDateHeader();
                _ = LoadTodayDataAsync();
            }
            else
            {
                UpdateDateHeader();

                // 보던 날은 두되, 오늘을 기준으로 하는 카드(학사일정·할 일)는 새 오늘로 옮긴다 —
                // 예전에는 이것까지 전날 기준으로 남았다.
                _ = ReloadTodayAnchoredAsync();
            }
        }

        // 현재 교시는 오늘에만 참이다.
        if (!IsViewingToday)
        {
            HighlightCurrentPeriod(0);
            return;
        }

        var period = Functions.GetPeriodNow();
        TxtCurrentPeriod.Text = period.Name;
        HighlightCurrentPeriod(period.Index);
    }

    /// <summary>
    /// 현재 교시(1~7)와 일치하는 시간표 행에 강조 플래그 설정.
    /// 쉬는시간·점심·방과후 등은 Index=0 이라 아무 행도 강조되지 않는다.
    /// </summary>
    private void HighlightCurrentPeriod(int index)
    {
        foreach (var row in _rows)
            row.IsCurrentPeriod = index >= 1 && row.Period == index;
    }

    /// <summary>현재 교시 강조 표시 여부 → Visibility (DataTemplate x:Bind용 순수 함수)</summary>
    public static Visibility ShowIfNow(bool isNow)
        => isNow ? Visibility.Visible : Visibility.Collapsed;

    private static string GetKoreanDayOfWeek(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Monday => "월요일",
        DayOfWeek.Tuesday => "화요일",
        DayOfWeek.Wednesday => "수요일",
        DayOfWeek.Thursday => "목요일",
        DayOfWeek.Friday => "금요일",
        DayOfWeek.Saturday => "토요일",
        _ => "일요일",
    };

    /// <summary>오늘 학사일정(행사명)을 헤더에 표시. 여러 개면 "… 외 N".</summary>
    private async Task LoadTodayEventAsync()
    {
        using var svc = new SchoolScheduleService(SchoolDatabase.DbPath);
        var (success, _, list) = await svc.GetSchedulesByDataRangeAsync(
            Settings.SchoolCode.Value, _viewDate, _viewDate.AddDays(1));

        if (!success || list == null) return;

        var names = list
            .Where(s => !string.IsNullOrWhiteSpace(s.EVENT_NM))
            .Select(s => s.EVENT_NM.Trim())
            .Distinct()
            .ToList();

        string text = names.Count switch
        {
            0 => string.Empty,
            1 => names[0],
            _ => $"{names[0]} 외 {names.Count - 1}",
        };

        TxtTodayEvent.Text = string.IsNullOrEmpty(text) ? string.Empty : $"· {text}";
        TxtTodayEvent.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    #endregion

    #region 오늘 시간표 (한 줄 = 한 교시: 내 수업 | 우리 반)

    /// <summary>
    /// 보고 있는 날의 시간표를 읽는다 — 내 수업은 수업 홈 시간표와 같은 장부(<see cref="LessonSlotBook"/>)로
    /// 풀어서, 휴업일·학년 행사·그 날 변경 판정이 두 화면에서 같다.
    /// </summary>
    private async Task LoadTimetableSlotsAsync()
    {
        List<Course> courses;
        using (var courseService = new CourseService())
            courses = await courseService.GetMyCoursesAsync();

        await _book.SetScopeAsync(Settings.WorkYear.Value, Settings.WorkSemester.Value, courses);
        await _book.LoadRangeAsync(_viewDate, _viewDate, withJournals: true);

        // 우리 반 (담임인 경우만) — 학교 전체가 쉬는 날은 비운다.
        var classSlots = new Dictionary<int, ClassTimetable>();
        int day = SchoolCalendar.ToLessonDayOfWeek(_viewDate);
        if (_isHomeroom && IsWeekday(_viewDate) && _book.OffDayReason(_viewDate) == null)
        {
            using var repo = new ClassTimetableRepository(SchoolDatabase.DbPath);
            var all = await repo.GetByClassAsync(
                Settings.SchoolCode.Value, Settings.WorkYear.Value, Settings.WorkSemester.Value,
                Settings.HomeGrade.Value, Settings.HomeRoom.Value);
            foreach (var slot in all.Where(x => x.DayOfWeek == day))
                classSlots[slot.Period] = slot;
        }
        _classSlots = classSlots;

        BuildRows();
    }

    private static bool IsWeekday(DateTime date)
        => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    /// <summary>
    /// 장부와 우리 반 칸으로 줄을 만든다(DB 는 읽지 않는다 — 메뉴로 바꾼 뒤에도 이것만 다시 부른다).
    ///
    /// <para>교시 수는 설정의 그 요일 교시 수이고, 그보다 뒤 교시에 수업·변경이 있으면 거기까지 늘린다.
    /// 휴업일(학교 전체)에는 그 날 넣은 보강이 없으면 줄 대신 사유를 띄운다.</para>
    /// </summary>
    private void BuildRows()
    {
        var date = _viewDate;
        var rows = new List<TodayPeriodRow>();
        string? off = _book.OffDayReason(date);

        if (IsWeekday(date))
        {
            int day = SchoolCalendar.ToLessonDayOfWeek(date);
            int last = PeriodCounts.Parse(Settings.PeriodsPerDay.Value).ForDay(day);

            // 평소 수업도 본다 — 교시 수를 줄이기 전에 넣은 수업이 빠지면 보이지 않는데도
            // 시수·진도는 그 시간을 센다(CourseTimetableBoard.IsShown 주석).
            var placed = _book.Lessons.Where(l => l.DayOfWeek == day).Select(l => l.Period);
            last = Math.Max(last, placed.DefaultIfEmpty(0).Max());

            var changed = _book.Changes.Keys.Where(k => k.Date == date).Select(k => k.Period);
            last = Math.Max(last, changed.DefaultIfEmpty(0).Max());
            last = Math.Max(last, _classSlots.Keys.DefaultIfEmpty(0).Max());

            bool anyLesson = false;
            var classWidth = _isHomeroom ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

            for (int period = 1; period <= last; period++)
            {
                var slot = _book.Resolve(date, period);
                if (!slot.IsBlank) anyLesson = true;

                _classSlots.TryGetValue(period, out var cls);

                rows.Add(new TodayPeriodRow
                {
                    Period = period,
                    Subject = LessonChangeLabels.WithPrefix(slot.Kind, slot.Subject),
                    Room = slot.Room,
                    IsCancelled = slot.Kind == LessonChangeKind.Cancelled,
                    HasJournal = _book.Journals.ContainsKey((date, period)),
                    Tooltip = SlotTooltip(slot, _book.MemoOf(date, period)),
                    ClassSubject = cls?.SubjectName ?? string.Empty,
                    ClassTeacher = cls?.TeacherName ?? string.Empty,
                    ClassColumnWidth = classWidth
                });
            }

            // 학교가 통째로 쉬는 날은 넣은 수업이 없으면 빈 줄 대신 사유를 띄운다.
            if (off != null && !anyLesson) rows.Clear();
        }

        _rows = rows;
        PeriodRowsList.ItemsSource = rows;

        bool has = rows.Count > 0;
        TxtNoSlots.Text = off ?? "수업 없음";
        PeriodRowsList.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        TxtNoSlots.Visibility = has ? Visibility.Collapsed : Visibility.Visible;

        // 로드는 첫 타이머 틱 이후 완료되므로, 새 줄에 현재 교시 강조를 즉시 반영
        HighlightCurrentPeriod(IsViewingToday ? Functions.GetPeriodNow().Index : 0);
    }

    /// <summary>
    /// 내 수업 칸의 툴팁. 변경이 걸린 교시면 사유 메모까지 함께 보여 준다 — 변경 배지를 없애면서
    /// 메모가 옮겨 온 자리다.
    /// </summary>
    private static string SlotTooltip(DaySlot slot, string memo)
    {
        if (slot.IsBlank) return "눌러서 수업 넣기 · 대강 입력";

        var tip = slot.Kind == LessonChangeKind.None ? string.Empty : $"[{LessonChangeLabels.Name(slot.Kind)}]";
        if (!string.IsNullOrWhiteSpace(memo)) tip += $" {memo}";

        const string hint = "눌러서 수업 일지 · 진도 · 수업 변경";
        return tip.Length == 0 ? hint : $"{tip.Trim()} · {hint}";
    }

    /// <summary>
    /// 교시 줄 단추의 UIA 이름. 단추 안은 교시·과목·교실을 담은 패널이라 이름이 되지 않고
    /// 툴팁도 이름이 아니어서, 낭독기가 줄마다 그냥 "단추" 라고만 읽었다.
    /// </summary>
    public static string SlotName(int period, string? subject, string? room)
    {
        if (string.IsNullOrWhiteSpace(subject)) return $"{period}교시 빈 시간 — 메뉴";
        return string.IsNullOrWhiteSpace(room)
            ? $"{period}교시 {subject} — 메뉴"
            : $"{period}교시 {subject} {room} — 메뉴";
    }

    /// <summary>휴강이면 취소선 (DataTemplate x:Bind용 순수 함수)</summary>
    public static Windows.UI.Text.TextDecorations StrikeIfCancelled(bool isCancelled)
        => isCancelled ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None;

    /// <summary>휴강은 흐리게 (DataTemplate x:Bind용 순수 함수)</summary>
    public static double DimIfCancelled(bool isCancelled) => isCancelled ? 0.5 : 1.0;

    #endregion

    #region 칸 메뉴 (수업 홈 시간표와 같은 메뉴)

    /// <summary>
    /// 내 수업 칸 클릭 → 수업 홈 시간표와 같은 칸 메뉴(수업 일지 · 진도 완료 표시 · 수업 변경).
    /// 날짜는 <b>보고 있는 날짜</b>다 — 날짜를 옮겨 둔 채 누르면 그 날짜로 쓴다.
    ///
    /// <para>예전에는 누르면 곧장 새 일지를 열었다(<c>ComposeAsync</c>). 이미 써 둔 일지가 있어도
    /// 새 글이 열려 같은 수업 일지가 두 벌 생길 수 있었다 — 메뉴의 일지 항목은 써 둔 글을 연다.</para>
    /// </summary>
    private async void TeacherSlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TodayPeriodRow row } button) return;

        try
        {
            var menu = await _menu.BuildAsync(_viewDate, row.Period);
            if (menu.Items.Count > 0)
                menu.ShowAt(button);
        }
        catch (Exception ex)
        {
            await Controls.UserErrorReporter.ReportAsync("시간표 메뉴", ex);
        }
    }

    XamlRoot? ILessonSlotMenuHost.XamlRoot => XamlRoot;

    Task ILessonSlotMenuHost.OnSlotChangedAsync(DateTime date, int period)
    {
        BuildRows();
        return Task.CompletedTask;
    }

    async Task ILessonSlotMenuHost.OnRecordChangedAsync()
    {
        await _book.LoadJournalMarksAsync();
        BuildRows();
    }

    void ILessonSlotMenuHost.ShowInfo(string message) => ShowSlotNotice(InfoBarSeverity.Success, message);
    void ILessonSlotMenuHost.ShowWarning(string message) => ShowSlotNotice(InfoBarSeverity.Warning, message);

    private void ShowSlotNotice(InfoBarSeverity severity, string message)
    {
        SlotInfoBar.Severity = severity;
        SlotInfoBar.Message = message;
        SlotInfoBar.IsOpen = true;
    }

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    #endregion
}
