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
    private DateTime _headerDate;                 // 헤더에 표시된 날짜 (자정 롤오버 감지용)
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
                SafeLoadAsync("급식",      () => MealBox.LoadMealsAsync(DateTime.Today), failed)
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

    /// <summary>날짜 헤더(날짜·요일) 갱신 + 표시 날짜 기억</summary>
    private void UpdateDateHeader()
    {
        _headerDate = DateTime.Today;
        TxtTodayDate.Text = _headerDate.ToString("yyyy년 M월 d일");
        TxtTodayDow.Text = GetKoreanDayOfWeek(_headerDate.DayOfWeek);
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
        // 1분 주기 타이머에서 날짜 변경을 감지해 전체를 다시 로드한다
        if (_headerInitialized && _headerDate != DateTime.Today)
        {
            UpdateDateHeader();
            _ = LoadTodayDataAsync();
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
            Settings.SchoolCode.Value, DateTime.Today, DateTime.Today.AddDays(1));

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

    /// <summary>오늘이 학사일정상 휴업일/공휴일이면 그 사유명(예: "휴업일"), 아니면 null.</summary>
    private static async Task<string?> GetTodayHolidayNameAsync()
    {
        using var svc = new SchoolScheduleService(SchoolDatabase.DbPath);
        var (success, _, list) = await svc.GetSchedulesByDataRangeAsync(
            Settings.SchoolCode.Value, DateTime.Today, DateTime.Today.AddDays(1));
        if (!success || list == null) return null;
        return list.FirstOrDefault(s => s.IsHoliday)?.SBTR_DD_SC_NM;
    }

    private async Task LoadTimetableSlotsAsync()
    {
        // .NET DayOfWeek: 0=일 … 6=토 / 시간표 DayOfWeek: 1=월 … 5=금
        int netDow = (int)DateTime.Today.DayOfWeek;
        int dow = (netDow >= 1 && netDow <= 5) ? netDow : 0;

        // 학사일정상 휴업일/공휴일이면 수업·학급 시간표를 표시하지 않는다(빈 상태에 사유 표시).
        string? holidayName = await GetTodayHolidayNameAsync();
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

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    #endregion
}
