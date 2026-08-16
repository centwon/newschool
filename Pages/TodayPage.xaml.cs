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
using NewSchool.Board.Services;
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
public sealed partial class TodayPage : Page, INotifyPropertyChanged
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

    // 현재 교시 행 강조를 위해 로드된 슬롯 참조 유지 (1분 주기로 재계산)
    private List<TimetableItemViewModel> _teacherSlots = new();
    private List<ClassTimetable> _classSlots = new();

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
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        ViewModel = new TodayPageViewModel(DispatcherQueue);
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        _periodTimer?.Stop();

        if (ViewModel != null)
        {
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            ViewModel.SchoolEvents.Clear();
            ViewModel.Tasks.Clear();
            ViewModel.Meals = null;
        }

        ViewModel = null;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) { }

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
                ClassBodyCell.Visibility = Visibility.Collapsed;
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
            Debug.WriteLine($"[TodayPage] 페이지 로드 오류: {ex.GetType().Name} - {ex.Message}");
        }

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
            Debug.WriteLine($"[TodayPage] ✗ {name} 로드 실패: {ex}");
            lock (failed) failed.Add(name);
        }
    }

    #region 상단 날짜 헤더 / 현재 교시

    private DispatcherQueueTimer CreatePeriodTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMinutes(1);
        timer.Tick += (_, _) => UpdateCurrentPeriod();
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
        foreach (var s in _teacherSlots)
            s.IsCurrentPeriod = index >= 1 && s.Period == index;
        foreach (var s in _classSlots)
            s.IsCurrentPeriod = index >= 1 && s.Period == index;
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

    #region 오늘 시간표 (내 수업 / 우리 반)

    /// <summary>그날이 학사일정상 휴업일/공휴일이면 그 사유명(예: "휴업일"), 아니면 null.</summary>
    private static async Task<string?> GetHolidayNameAsync(DateTime date)
    {
        using var svc = new SchoolScheduleService(SchoolDatabase.DbPath);
        var (success, _, list) = await svc.GetSchedulesByDataRangeAsync(
            Settings.SchoolCode.Value, date, date.AddDays(1));
        if (!success || list == null) return null;
        return list.FirstOrDefault(s => s.IsHoliday)?.SBTR_DD_SC_NM;
    }

    private async Task LoadTimetableSlotsAsync()
    {
        // .NET DayOfWeek: 0=일 … 6=토 / 시간표 DayOfWeek: 1=월 … 5=금
        int netDow = (int)_viewDate.DayOfWeek;
        int dow = (netDow >= 1 && netDow <= 5) ? netDow : 0;

        // 학사일정상 휴업일/공휴일이면 수업·학급 시간표를 표시하지 않는다(빈 상태에 사유 표시).
        string? holidayName = await GetHolidayNameAsync(_viewDate);
        if (holidayName != null) dow = 0;
        TxtNoTeacherSlots.Text = holidayName ?? "수업 없음";
        TxtNoClassSlots.Text   = holidayName ?? "시간표 없음";

        // 내 수업 (교사 시간표)
        var teacherSlots = new List<TimetableItemViewModel>();
        if (dow != 0)
        {
            using var svc = new LessonService();
            var tvm = await svc.GetTeacherTimetableViewModelAsync(
                Settings.User.Value, Settings.WorkYear.Value, Settings.WorkSemester.Value);
            teacherSlots = tvm.Items
                .Where(x => x.DayOfWeek == dow && !x.IsEmpty)
                .OrderBy(x => x.Period)
                .ToList();
        }

        // 그날만 걸리는 변경(휴강·교체·보강·대강)을 얹는다.
        // 휴업일이라 정기 수업을 안 그리는 날에도 보강은 있을 수 있으므로 dow 와 무관하게 읽는다.
        teacherSlots = await ApplyLessonChangesAsync(teacherSlots, _viewDate);
        if (teacherSlots.Count > 0)
            TxtNoTeacherSlots.Text = holidayName ?? "수업 없음";

        _teacherSlots = teacherSlots;
        TeacherSlotsList.ItemsSource = teacherSlots;
        bool hasTeacher = teacherSlots.Count > 0;
        TeacherSlotsList.Visibility = hasTeacher ? Visibility.Visible : Visibility.Collapsed;
        TxtNoTeacherSlots.Visibility = hasTeacher ? Visibility.Collapsed : Visibility.Visible;

        // 우리 반 (담임인 경우만)
        if (_isHomeroom)
        {
            var classSlots = new List<ClassTimetable>();
            if (dow != 0)
            {
                using var repo = new ClassTimetableRepository(SchoolDatabase.DbPath);
                var all = await repo.GetByClassAsync(
                    Settings.SchoolCode.Value, Settings.WorkYear.Value, Settings.WorkSemester.Value,
                    Settings.HomeGrade.Value, Settings.HomeRoom.Value);
                classSlots = all.Where(x => x.DayOfWeek == dow).OrderBy(x => x.Period).ToList();
            }
            _classSlots = classSlots;
            ClassSlotsList.ItemsSource = classSlots;
            bool hasClass = classSlots.Count > 0;
            ClassSlotsList.Visibility = hasClass ? Visibility.Visible : Visibility.Collapsed;
            TxtNoClassSlots.Visibility = hasClass ? Visibility.Collapsed : Visibility.Visible;
        }

        // 로드는 첫 타이머 틱 이후 완료되므로, 새 슬롯에 현재 교시 강조를 즉시 반영
        HighlightCurrentPeriod(Functions.GetPeriodNow().Index);
    }

    /// <summary>
    /// 그날만 걸리는 시간표 변경을 정기 슬롯 위에 얹는다.
    /// 병합 규칙은 <see cref="TimetableChangeMerger"/> 에 있다(DB 없이 검증할 수 있게 떼어 뒀다).
    /// </summary>
    private static async Task<List<TimetableItemViewModel>> ApplyLessonChangesAsync(
        List<TimetableItemViewModel> slots, DateTime date)
    {
        try
        {
            using var repo = new LessonChangeRepository(SchoolDatabase.DbPath);
            var changes = await repo.GetByDateAsync(Settings.User.Value, date);

            return TimetableChangeMerger.Apply(
                slots, changes, Helpers.SchoolCalendar.ToLessonDayOfWeek(date));
        }
        catch (Exception ex)
        {
            // 변경을 못 읽었다고 오늘 시간표까지 비우지는 않는다 — 평소 시간표는 그대로 쓸모가 있다.
            Debug.WriteLine($"[TodayPage] 시간표 변경 조회 실패: {ex.Message}");
            return slots;
        }
    }

    /// <summary>변경 배지 표시 여부 → Visibility (DataTemplate x:Bind용 순수 함수)</summary>
    public static Visibility ShowIfChanged(bool hasChange)
        => hasChange ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>휴강이면 취소선 (DataTemplate x:Bind용 순수 함수)</summary>
    public static Windows.UI.Text.TextDecorations StrikeIfCancelled(bool isCancelled)
        => isCancelled ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None;

    /// <summary>휴강은 흐리게 (DataTemplate x:Bind용 순수 함수)</summary>
    public static double DimIfCancelled(bool isCancelled) => isCancelled ? 0.5 : 1.0;

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    #endregion
}
