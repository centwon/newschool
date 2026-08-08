using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Dialogs;
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
                _ = RefreshCalendarAsync().ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        System.Diagnostics.Debug.WriteLine($"[Kcalendar] {t.Exception?.InnerException?.Message}");
                }, TaskContinuationOptions.OnlyOnFaulted);
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

            // 안전한 초기화 순서
            await InitializeCalendarSafelyAsync();

            _isInitialized = true;

            System.Diagnostics.Debug.WriteLine("Kcalendar_Loaded 완료");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"달력 초기화 오류: {ex}");

            // 초기화가 실패해도 조작은 살려 둔다. 예전에는 _isInitialized 가 false 로 남는데
            // Loaded 구독은 아래에서 해제돼, 이전/다음 달 버튼과 월 선택이 전부 조용히
            // 무반응이 되고 되살릴 방법이 없었다(달력이 영구히 죽었다).
            // 셀은 이미 만들어져 있으므로 달을 옮기면 다시 읽어볼 수 있다.
            _isInitialized = true;

            await ShowErrorAsync(
                $"달력을 불러오지 못했습니다.\n{ex.Message}\n\n달을 옮기거나 새로고침하면 다시 시도합니다.");
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

        ApplyHeaderFontSize();

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
    private Task CreateDayCellsSynchronouslyAsync()
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
                cell.CellChanged += DayCell_CellChanged;

                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                GridBody.Children.Add(cell);

                Cells[i] = cell;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DayCell 생성 오류 (인덱스 {i}): {ex.Message}");
            }
        }

        // Dayinfo 대입은 DayCell._pendingDayInfo 큐잉으로 Loaded 이전에도 안전하게 처리됨(대기 불필요)
        Debug.WriteLine($"[Kcalendar] 모든 DayCell 생성 완료: {Cells.Count(c => c != null)}개");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 달력 데이터 로드 (✅ Ktask → KEvent 통합)
    /// </summary>
    private async Task LoadCalendarDataAsync()
    {
        List<SchoolSchedule> newSchedules = new();
        List<KEvent> newEvents = new();

        // 무엇을 못 읽었는지 모아 두었다가 한 번에 알린다. 예전에는 어느 쪽이 실패해도
        // Debug 로그만 남기고 빈 목록으로 넘어가, 달력이 "그 달에 아무 일정도 없는" 것처럼
        // 보였다 — 일정·할 일이 통째로 사라져도 사용자는 알 수 없었다.
        var failed = new List<string>();

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
                            Debug.WriteLine($"[Kcalendar] 학사일정 조회 실패: {schedules.Message}");
                            failed.Add("학사일정");
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
                        else { failed.Add("학사일정"); }
                        Debug.WriteLine($"[Kcalendar] API 로드 결과: {newSchedules.Count}개");
                    }

                    Debug.WriteLine($"[Kcalendar] 스케줄 로드 완료: {newSchedules.Count}개");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Kcalendar] 스케줄 로드 오류: {ex}");
                    newSchedules = new List<SchoolSchedule>();
                    failed.Add("학사일정");
                }
            }

            // ✅ KEvent 통합 로드 (task + event 모두 KEvent)
            try
            {
                using var service = Scheduler.CreateService();

                // 모든 KEvent 로드 (task + event 포함) — GetTasksByDateAsync 중복 조회 없이 메모리에서 분리
                var allEvents = await service.GetEventsByDateAsync(calendarStart, 42);
                Debug.WriteLine($"[Kcalendar] KEvent 전체 로드 완료: {allEvents.Count}개");

                // 이벤트 자체 색상(ColorId)이 없을 때 표시할 폴백 색 — 소속 캘린더 색상을 미리 채워둠
                var calendars = await service.GetAllCalendarsAsync();
                var colorByCalendarId = calendars.ToDictionary(c => c.No, c => c.Color);
                foreach (var ev in allEvents)
                {
                    if (colorByCalendarId.TryGetValue(ev.CalendarId, out var color))
                        ev.CalendarColor = color;
                }

                newEvents = Settings.ShowTasks.Value
                    ? allEvents
                    : allEvents.Where(e => e.ItemType != "task").ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Kcalendar] KEvent 로드 오류: {ex}");
                newEvents = new List<KEvent>();
                failed.Add("일정·할 일");
            }

            // 속성 업데이트
            SchoolSchedules = newSchedules;
            KEvents = newEvents;
            Debug.WriteLine($"[Kcalendar] 데이터 로드 완료 - SchoolSchedules: {SchoolSchedules.Count}개, KEvents: {KEvents.Count}개");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Kcalendar] 데이터 로드 전체 오류: {ex}");

            SchoolSchedules = new List<SchoolSchedule>();
            KEvents = new List<KEvent>();
            failed.Add("달력 데이터");
        }

        if (failed.Count > 0 && App.MainWindow is MainWindow main)
        {
            main.ShowGlobalWarning(
                "달력의 일부 정보를 불러오지 못했습니다",
                $"{string.Join(", ", failed.Distinct())} — 빈 달력이 아니라 조회 실패입니다. 잠시 후 다시 확인해주세요.");
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

                        // KEvent를 task와 event로 분리 (다중일 이벤트: Start~End 범위, End는 inclusive)
                        // ItemType="schoolschedule"(학사일정 자동동기화분)은 날짜 옆 DateName(SchoolSchedules)에
                        // 이미 표시되므로 목록에서 제외 — 사용자가 직접 넣은 항목(ItemType="event")은 그대로 표시.
                        var dayEvents = KEvents?
                            .Where(x => cellDate.Date >= x.Start.Date && cellDate.Date <= x.End.Date)
                            .ToList() ?? new List<KEvent>();

                        var tasks = dayEvents.Where(e => e.ItemType == "task").ToList();
                        var events = dayEvents
                            .Where(e => e.ItemType != "task" && e.ItemType != "schoolschedule")
                            .ToList();

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
            ApplyHeaderFontSize();
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
    /// DayCell에서 일정/할일 편집/삭제 후 전체 새로고침
    /// </summary>
    private async void DayCell_CellChanged(object? sender, EventArgs e)
    {
        await RefreshCalendarAsync();
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
    private async void BtnSetup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CalendarSettingsDialog
        {
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();

        // 다이얼로그 닫힌 후 달력 새로고침
        await RefreshCalendarAsync();
    }

    /// <summary>요일 헤더("일~토")·년월 선택 폰트 크기를 설정값에 맞춰 적용 (날짜 숫자와 같은 크기 사용)</summary>
    private void ApplyHeaderFontSize()
    {
        double size = Settings.DateFontSize.Value;
        TxtWeekdaySun.FontSize = size;
        TxtWeekdayMon.FontSize = size;
        TxtWeekdayTue.FontSize = size;
        TxtWeekdayWed.FontSize = size;
        TxtWeekdayThu.FontSize = size;
        TxtWeekdayFri.FontSize = size;
        TxtWeekdaySat.FontSize = size;
        PickerMonth.DisplayFontSize = size;
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
