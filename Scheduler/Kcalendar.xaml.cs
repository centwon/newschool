using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;


namespace NewSchool.Scheduler;

public sealed partial class Kcalendar : Page
{
    private DateTime _basedate = DateTime.Today;
    private readonly DayCell[] Cells = new DayCell[42];
    private bool _isInitialized = false;
    private bool _isInitializing = false;

    public DateTime BaseDate
    {
        get => _basedate;
        set
        {
            if (value != _basedate && _isInitialized)
            {
                _basedate = value;
                _ = RefreshCalendarAsync();
            }
        }
    }

    public List<SchoolSchedule> SchoolSchedules { get; set; } = new();
    /// <summary>모든 KEvent (task + event 통합)</summary>
    public List<KEvent> KEvents { get; set; } = new();

    public Kcalendar()
    {
        InitializeComponent();
        Loaded += Kcalendar_Loaded;
    }

    private async void Kcalendar_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || _isInitialized) return;

        _isInitializing = true;

        try
        {
            System.Diagnostics.Debug.WriteLine("Kcalendar_Loaded 시작");

            // UI 완전 로드 대기
            await Task.Delay(100);

            // 안전한 초기화 순서
            await InitializeCalendarSafelyAsync();

            _isInitialized = true;

            System.Diagnostics.Debug.WriteLine("Kcalendar_Loaded 완료");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"달력 초기화 오류: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"스택 트레이스: {ex.StackTrace}");
            await ShowErrorAsync($"달력 초기화 오류: {ex.Message}");
        }
        finally
        {
            _isInitializing = false;
            this.Loaded -= Kcalendar_Loaded;
        }
    }

    /// <summary>
    /// 안전한 달력 초기화
    /// </summary>
    private async Task InitializeCalendarSafelyAsync()
    {
        Debug.WriteLine($"[Kcalendar] 1단계: 데이터베이스 초기화");
        //Scheduler 초기화
        await NewSchool.Scheduler.Scheduler.InitAsync();  // 내부에서 파일 존재 여부 + 플래그 체크함

        Debug.WriteLine($"[Kcalendar] 2단계: DayCell 생성");
        await CreateDayCellsSynchronouslyAsync();

        Debug.WriteLine($"[Kcalendar] 3단계: 데이터 로드");
        await LoadCalendarDataAsync();

        Debug.WriteLine($"[Kcalendar] 4단계: UI 업데이트");
        await UpdateCellsDisplayAsync();
    }


    /// <summary>
    /// DayCell들을 동기적으로 생성
    /// </summary>
    private async Task CreateDayCellsSynchronouslyAsync()
    {
        // ✅ UI 스레드에서 직접 실행 (DispatcherQueue 불필요)
        for (int i = 0; i < Cells.Length; i++)
        {
            try
            {
                var row = (i / 7) + 1;
                var column = i % 7;

                var cell = new DayCell();
                cell.Position = (row, column);
                cell.PointerPressed += DayCell_PointerPressed;

                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                GridBody.Children.Add(cell);

                Cells[i] = cell;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DayCell 생성 오류 (인덱스 {i}): {ex.Message}");
            }

            // 매 행(7개)마다 잠깐 대기
            if (i % 7 == 6)
            {
                await Task.Delay(5);
            }
        }

        Debug.WriteLine($"[Kcalendar] 모든 DayCell 생성 완료: {Cells.Count(c => c != null)}개");

        // 모든 셀의 Loaded 이벤트 대기
        await Task.Delay(100);
    }

    private async Task CreateDayCellsAsync()
    {
        // ✅ UI 스레드에서 직접 실행 (DispatcherQueue 불필요)
        for (int i = 0; i < Cells.Length; i++)
        {
            try
            {
                var row = (i / 7) + 1;
                var column = i % 7;

                var cell = new DayCell();
                cell.Position = (row, column);
                cell.PointerPressed += DayCell_PointerPressed;

                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                GridBody.Children.Add(cell);

                Cells[i] = cell;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DayCell 생성 오류 (인덱스 {i}): {ex.Message}");
            }

            // 매 행(7개)마다 잠깐 대기
            if (i % 7 == 6)
            {
                await Task.Delay(5);
            }
        }

        // 모든 셀이 생성될 때까지 추가 대기
        await Task.Delay(100);
    }

    /// <summary>
    /// 달력 데이터 로드 (✅ Ktask → KEvent 통합)
    /// </summary>
    private async Task LoadCalendarDataAsync()
    {
        List<SchoolSchedule> newSchedules = new();
        List<KEvent> newEvents = new();

        try
        {
            Debug.WriteLine($"[Kcalendar] 데이터 로드 시작");

            // 날짜 계산
            var firstDayOfMonth = new DateTime(_basedate.Year, _basedate.Month, 1);
            var dayOfWeekValue = (int)firstDayOfMonth.DayOfWeek;
            var calendarStart = firstDayOfMonth.AddDays(-dayOfWeekValue);
            var calendarEnd = calendarStart.AddDays(42);

            Debug.WriteLine($"[Kcalendar] 날짜 범위: {calendarStart:yyyy-MM-dd} ~ {calendarEnd:yyyy-MM-dd}");

            // 스케줄 로드
            if (Settings.ShowEvents.Value)
            {
                try
                {
                    Debug.WriteLine($"[Kcalendar] 스케줄 로드 시작");

                    if (Settings.IsNeisEventDownloaded.Value)
                    {
                        // ✅ DB에서 비동기로 로드
                        Debug.WriteLine($"[Kcalendar] DB에서 로드: {calendarStart:yyyy-MM-dd} + 42일");
                        using var scheduleService = new SchoolScheduleService(Settings.SchoolDB.Value);
                        var schedules = await scheduleService.GetSchedulesByDataRangeAsync(Settings.SchoolCode, calendarStart, calendarEnd);
                        if (schedules.Success)
                        {
                            newSchedules = schedules.Schedules;
                            Debug.WriteLine($"[Kcalendar] DB 로드 결과: {newSchedules.Count}개");
                        }
                        else
                        {
                            Debug.WriteLine($"[Kcalendar] 경고: DB에 데이터가 없습니다!");
                        }
                    }
                    else
                    {
                        // API에서 로드
                        Debug.WriteLine($"[Kcalendar] NEIS API에서 로드");
                        using var scheduleService = new SchoolScheduleService(Settings.SchoolDB.Value);
                        var downloads = await scheduleService.DownloadFromNeisAsync(schoolCode: Settings.SchoolCode,
                                                                                   provinceCode: Settings.ProvinceCode,
                                                                                   year: _basedate.Year,
                                                                                   startDate: calendarStart,
                                                                                   endDate: calendarEnd);
                        if (downloads.Success) { newSchedules = downloads.Schedules; }
                        Debug.WriteLine($"[Kcalendar] API 로드 결과: {newSchedules.Count}개");
                    }

                    Debug.WriteLine($"[Kcalendar] 스케줄 로드 완료: {newSchedules.Count}개");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Kcalendar] 스케줄 로드 오류: {ex.Message}");
                    newSchedules = new List<SchoolSchedule>();
                }
            }

            // ✅ KEvent 통합 로드 (task + event 모두 KEvent)
            try
            {
                using var service = Scheduler.CreateService();

                // 모든 KEvent 로드 (task + event 포함)
                var allEvents = await service.GetEventsByDateAsync(calendarStart, 42);
                Debug.WriteLine($"[Kcalendar] KEvent 전체 로드 완료: {allEvents.Count}개");

                // task 항목도 별도 로드 (ShowTasks 설정 반영)
                if (Settings.ShowTasks.Value)
                {
                    var tasks = await service.GetTasksByDateAsync(calendarStart, 42, true);
                    Debug.WriteLine($"[Kcalendar] Task 로드 완료: {tasks.Count}개");

                    // events에서 task 항목 제외하고, task 쿼리 결과 추가 (중복 방지)
                    var eventOnly = allEvents.Where(e => e.ItemType != "task").ToList();
                    newEvents = new List<KEvent>(eventOnly.Count + tasks.Count);
                    newEvents.AddRange(tasks);
                    newEvents.AddRange(eventOnly);
                }
                else
                {
                    // task 숨김 → event만 표시
                    newEvents = allEvents.Where(e => e.ItemType != "task").ToList();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Kcalendar] KEvent 로드 오류: {ex.Message}");
                newEvents = new List<KEvent>();
            }

            // 속성 업데이트
            SchoolSchedules = newSchedules;
            KEvents = newEvents;
            Debug.WriteLine($"[Kcalendar] 데이터 로드 완료 - SchoolSchedules: {SchoolSchedules.Count}개, KEvents: {KEvents.Count}개");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Kcalendar] 데이터 로드 전체 오류: {ex.Message}");
            Debug.WriteLine($"[Kcalendar] 스택: {ex.StackTrace}");

            SchoolSchedules = new List<SchoolSchedule>();
            KEvents = new List<KEvent>();
        }
    }

    /// <summary>
    /// 셀 표시 업데이트 (✅ Ktask → KEvent 통합)
    /// </summary>
    private async Task UpdateCellsDisplayAsync()
    {
        try
        {
            var firstDayOfMonth = new DateTime(_basedate.Year, _basedate.Month, 1);
            var dayOfWeekValue = (int)firstDayOfMonth.DayOfWeek;
            var calendarStart = firstDayOfMonth.AddDays(-dayOfWeekValue);

            Debug.WriteLine($"[UpdateCellsDisplayAsync] 시작: {calendarStart}");

            // ✅ 모든 DayInfo를 먼저 준비
            var dayInfos = new DayInfo[42];

            await Task.Run(() =>
            {
                for (int i = 0; i < 42; i++)
                {
                    var cellDate = DateTime.SpecifyKind(
                        calendarStart.AddDays(i),
                        DateTimeKind.Unspecified
                    );

                    try
                    {
                        var schedules = SchoolSchedules?
                            .Where(x => x.AA_YMD.Date == cellDate.Date)
                            .ToList() ?? new List<SchoolSchedule>();

                        if (schedules.Count > 0)
                            Debug.WriteLine($"[UpdateCells] {cellDate:yyyy-MM-dd} 스케줄: {schedules.Count}개");

                        // KEvent를 task와 event로 분리
                        var dayEvents = KEvents?
                            .Where(x => x.Start.Date == cellDate.Date)
                            .ToList() ?? new List<KEvent>();

                        var tasks = dayEvents.Where(e => e.ItemType == "task").ToList();
                        var events = dayEvents.Where(e => e.ItemType != "task").ToList();

                        if (dayEvents.Count > 0)
                            Debug.WriteLine($"[UpdateCells] {cellDate:yyyy-MM-dd} KEvent: {dayEvents.Count}개 (task: {tasks.Count}, event: {events.Count})");

                        dayInfos[i] = new DayInfo(cellDate, schedules, tasks, events);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[UpdateCellsDisplayAsync] DayInfo 준비 오류 (인덱스 {i}): {ex.Message}");
                        dayInfos[i] = new DayInfo
                        {
                            Date = cellDate,
                            SchoolSchedules = new List<SchoolSchedule>(),
                            Tasks = new List<KEvent>()
                        };
                    }
                }
            });

            Debug.WriteLine($"[UpdateCellsDisplayAsync] DayInfo 준비 완료");

            // UI 스레드에서 직접 할당 (Loaded 이벤트 체인에서 호출되므로 이미 UI 스레드)
            for (int i = 0; i < Cells.Length; i++)
            {
                if (Cells[i] == null)
                {
                    Debug.WriteLine($"[UpdateCellsDisplayAsync] 경고: Cells[{i}]가 null입니다.");
                    continue;
                }

                if (dayInfos[i] == null)
                {
                    Debug.WriteLine($"[UpdateCellsDisplayAsync] 경고: dayInfos[{i}]가 null입니다.");
                    continue;
                }

                try
                {
                    Cells[i].Dayinfo = dayInfos[i];
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UpdateCellsDisplayAsync] 셀 {i} 업데이트 오류: {ex.Message}");
                }

                // 매 행(7개)마다 UI 스레드 양보
                if (i % 7 == 6)
                {
                    await Task.Delay(10);
                }
            }

            Debug.WriteLine($"[UpdateCellsDisplayAsync] 완료");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateCellsDisplayAsync] 전체 오류: {ex.Message}");
            Debug.WriteLine($"[UpdateCellsDisplayAsync] 스택: {ex.StackTrace}");
            throw;
        }
    }    /// <summary>
         /// 달력 새로고침
         /// </summary>
    private async Task RefreshCalendarAsync()
    {
        if (!_isInitialized && !_isInitializing) return;

        Debug.WriteLine($"[Kcalendar] 새로고침 시작");

        try
        {
            await LoadCalendarDataAsync();
            await UpdateCellsDisplayAsync();

            Debug.WriteLine($"[Kcalendar] 새로고침 완료");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Kcalendar] 새로고침 오류: {ex.Message}");
            await ShowErrorAsync($"달력 새로고침 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// DayCell 클릭 이벤트 처리 (✅ ResultEvent 통합)
    /// </summary>
    private async void DayCell_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not DayCell cell || cell.Dayinfo == null) return;

        try
        {
            var dialog = new UnifiedItemDialog(cell.Dayinfo.Date)
            {
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && dialog.ResultEvent != null)
            {
                var savedEvent = dialog.ResultEvent;
                if (!KEvents.Any(ev => ev.No == savedEvent.No))
                    KEvents.Add(savedEvent);

                if (savedEvent.Start.Date == cell.Dayinfo.Date.Date)
                {
                    if (savedEvent.ItemType == "task")
                        cell.Dayinfo.Tasks?.Add(savedEvent);
                    else
                        cell.Dayinfo.Events?.Add(savedEvent);
                }

                await RefreshCalendarAsync();
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"항목 생성 오류: {ex.Message}");
        }
    }
    /// <summary>
    /// 이전 달 버튼 클릭
    /// </summary>
    private void BtnPrevious_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            BaseDate = _basedate.AddMonths(-1);
            PickerMonth.SelectedMonth = BaseDate;
        }
    }

    /// <summary>
    /// 다음 달 버튼 클릭
    /// </summary>
    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            BaseDate = _basedate.AddMonths(1);
            PickerMonth.SelectedMonth = BaseDate;
        }
    }

    /// <summary>
    /// 월 선택기 변경 이벤트
    /// </summary>
    private void PickerMonth_SelectedMonthChanged(object sender, EventArgs data)
    {
        if (_isInitialized)
        {
            BaseDate = PickerMonth.SelectedMonth;
        }
    }

    /// <summary>
    /// 설정 버튼 클릭
    /// </summary>
    private void BtnSetup_Click(object sender, RoutedEventArgs e)
    {
        // 설정 창 표시 로직
        Debug.WriteLine("[Kcalendar] 설정 버튼 클릭");
    }

    /// <summary>
    /// 안전한 오류 메시지 표시
    /// </summary>
    private async Task ShowErrorAsync(string message)
    {
        try
        {
            await MessageBox.ShowAsync(message);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Kcalendar] 오류 메시지 표시 실패: {message}");
            Debug.WriteLine($"[Kcalendar] 내부 오류: {ex.Message}");
        }
    }
}
