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
        if (!await NewSchool.Scheduler.Scheduler.InitAsync())
            NewSchool.Logging.Log.Error("Kcalendar", "일정 DB 를 준비하지 못했다 — 달력이 비어 보인다");

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
                cell.PointerPressed += DayCell_PointerPressed;
                cell.KeyDown += DayCell_KeyDown;
                cell.GotFocus += DayCell_GotFocus;
                cell.CellChanged += DayCell_CellChanged;

                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                GridBody.Children.Add(cell);

                Cells[i] = cell;
            }
            catch (Exception ex)
            {
                NewSchool.Logging.Log.Warning("Kcalendar", $"날짜 칸 {i} 을(를) 만들지 못했다: {ex.Message}");
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

                    // SchoolDatabase.DbPath 를 쓴다 — Settings.SchoolDB.Value 는 "school.db" 라는
                    // 파일 이름뿐이라 상대 경로가 되고, SQLite 가 그것을 프로세스 작업 폴더(실행 파일
                    // 옆)에서 찾다가 없으면 빈 DB 를 새로 만든다. 달력에 학사일정이 하나도 안 뜨고
                    // 실행 파일 옆에 school.db 가 생기던 원인이다. 다른 호출처 6곳은 모두 DbPath 를 썼다.
                    using var scheduleService = new SchoolScheduleService(SchoolDatabase.DbPath);

                    // 보는 달의 학년도가 DB 에 없으면 통째로 받아 넣는다(실행마다 학년도당 한 번 확인).
                    // 예전에는 이 화면이 42일치를 받아 그리기만 하고 저장하지 않아서, 달을 넘길
                    // 때마다 NEIS 를 다시 부르면서도 DB 에는 아무것도 남기지 않았다.
                    await scheduleService.EnsureSchoolYearDownloadedAsync(
                        Settings.SchoolCode, Settings.ProvinceCode, DateTimeHelper.SchoolYearOf(_basedate));

                    // ✅ DB에서 비동기로 로드
                    Debug.WriteLine($"[Kcalendar] DB에서 로드: {calendarStart:yyyy-MM-dd} + 42일");
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

                    Debug.WriteLine($"[Kcalendar] 스케줄 로드 완료: {newSchedules.Count}개");
                }
                catch (Exception ex)
                {
                    NewSchool.Logging.Log.Error("Kcalendar", "학사일정을 읽지 못했다", ex);
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
                NewSchool.Logging.Log.Error("Kcalendar", "일정·할 일을 읽지 못했다", ex);
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
            NewSchool.Logging.Log.Error("Kcalendar", "달력 자료를 통째로 읽지 못했다 — 빈 달력으로 보인다", ex);

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
                // 셀마다 전체 목록을 다시 훑으면 O(42 × N) 이 된다.
                // 날짜별 버킷을 한 번만 만들어 두고 각 셀은 자기 날짜만 꺼내 쓴다(O(N)).
                // ItemType="schoolschedule"(학사일정 자동동기화분)은 날짜 옆 DateName 에 이미
                // 표시되므로 목록에서 제외 — 사용자가 직접 넣은 항목("event")은 그대로 둔다.
                var scheduleByDate = new Dictionary<DateTime, List<SchoolSchedule>>();
                if (SchoolSchedules != null)
                {
                    foreach (var s in SchoolSchedules)
                    {
                        if (!scheduleByDate.TryGetValue(s.AA_YMD.Date, out var bucket))
                            scheduleByDate[s.AA_YMD.Date] = bucket = new List<SchoolSchedule>();
                        bucket.Add(s);
                    }
                }

                var tasksByDate  = new Dictionary<DateTime, List<KEvent>>();
                var eventsByDate = new Dictionary<DateTime, List<KEvent>>();
                if (KEvents != null)
                {
                    var windowStart = calendarStart.Date;
                    var windowEnd   = calendarStart.Date.AddDays(41);

                    foreach (var ev in KEvents)
                    {
                        if (ev.ItemType == "schoolschedule") continue;

                        var target = ev.ItemType == "task" ? tasksByDate : eventsByDate;

                        // 다중일 이벤트는 Start~End 전 구간에 걸쳐 넣는다(End 는 inclusive).
                        // 표시 창 밖으로는 확장하지 않는다.
                        var day = ev.Start.Date < windowStart ? windowStart : ev.Start.Date;
                        var last = ev.End.Date > windowEnd ? windowEnd : ev.End.Date;

                        for (; day <= last; day = day.AddDays(1))
                        {
                            if (!target.TryGetValue(day, out var bucket))
                                target[day] = bucket = new List<KEvent>();
                            bucket.Add(ev);
                        }
                    }
                }

                for (int i = 0; i < 42; i++)
                {
                    var cellDate = DateTime.SpecifyKind(
                        calendarStart.AddDays(i),
                        DateTimeKind.Unspecified
                    );

                    try
                    {
                        var schedules = scheduleByDate.TryGetValue(cellDate.Date, out var sList)
                            ? sList : new List<SchoolSchedule>();
                        var tasks = tasksByDate.TryGetValue(cellDate.Date, out var tList)
                            ? tList : new List<KEvent>();
                        var events = eventsByDate.TryGetValue(cellDate.Date, out var eList)
                            ? eList : new List<KEvent>();

                        dayInfos[i] = new DayInfo(cellDate, schedules, tasks, events);
                    }
                    catch (Exception ex)
                    {
                        NewSchool.Logging.Log.Warning("Kcalendar", $"날짜 칸 {i} 의 내용을 준비하지 못했다: {ex.Message}");
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
                    NewSchool.Logging.Log.Warning("Kcalendar", $"날짜 칸 {i} 을(를) 갱신하지 못했다: {ex.Message}");
                }
            }

            ResetRovingForMonth(calendarStart);

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
        if (sender is not DayCell cell) return;
        await OpenNewItemAsync(cell);
    }

    #region 날짜 칸 키보드 (roving 포커스)

    // 날짜 칸은 예전에 키보드로 닿지 않았다(IsTabStop 이 없었다) — 마우스로만 새 항목을 만들 수 있었다.
    // 42칸을 전부 Tab 정지점으로 만들면 달력을 지나가는 데만 Tab 을 42번 눌러야 하므로, 한 칸만
    // Tab 정지점으로 두고 방향키로 옮긴다(roving tab stop). 칸 안의 할 일 완료 단추는 원래대로
    // Tab 으로 닿는다 — 이 방식이 그 단추들을 건드리지 않는 이유다.

    private int _rovingIndex = -1;
    private DateTime _rovingMonth;

    /// <summary>Tab 이 들어올 칸을 <paramref name="index"/> 하나로 정한다.</summary>
    private void SetRovingCell(int index)
    {
        if (index < 0 || index >= Cells.Length || Cells[index] == null) return;

        for (int i = 0; i < Cells.Length; i++)
            if (Cells[i] != null) Cells[i].IsTabStop = i == index;
        _rovingIndex = index;
    }

    /// <summary>
    /// 달이 바뀌었을 때만 Tab 이 들어올 칸을 다시 고른다 — 보이는 달에 오늘이 있으면 오늘, 없으면 1일.
    /// (같은 달을 새로 그릴 때마다 오늘로 되돌리면, 새 항목을 만든 뒤 방금 있던 칸을 잃는다.)
    /// </summary>
    private void ResetRovingForMonth(DateTime calendarStart)
    {
        var month = new DateTime(_basedate.Year, _basedate.Month, 1);
        if (_rovingIndex >= 0 && month == _rovingMonth) { SetRovingCell(_rovingIndex); return; }

        _rovingMonth = month;
        int today = (DateTime.Today - calendarStart.Date).Days;
        int first = (month - calendarStart.Date).Days;
        bool todayInMonth = DateTime.Today >= month && DateTime.Today < month.AddMonths(1);
        SetRovingCell(todayInMonth ? today : first);
    }

    private void DayCell_GotFocus(object sender, RoutedEventArgs e)
    {
        // 칸 자체가 포커스를 받았을 때만 — 칸 안 할 일 단추로 간 포커스는 칸을 옮긴 것이 아니다.
        if (sender is DayCell cell && ReferenceEquals(e.OriginalSource, cell))
            SetRovingCell(Array.IndexOf(Cells, cell));
    }

    private async void DayCell_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        // 칸 안 할 일 단추에서 올라온 키는 건드리지 않는다(그 단추의 Enter·Space 는 제 일이다).
        if (sender is not DayCell cell || !ReferenceEquals(e.OriginalSource, cell)) return;

        int i = Array.IndexOf(Cells, cell);
        int next = e.Key switch
        {
            Windows.System.VirtualKey.Left => i - 1,
            Windows.System.VirtualKey.Right => i + 1,
            Windows.System.VirtualKey.Up => i - 7,
            Windows.System.VirtualKey.Down => i + 7,
            Windows.System.VirtualKey.Home => i - i % 7,
            Windows.System.VirtualKey.End => i - i % 7 + 6,
            _ => int.MinValue,
        };

        if (next != int.MinValue)
        {
            e.Handled = true;
            if (next >= 0 && next < Cells.Length && Cells[next] != null)
            {
                SetRovingCell(next);
                Cells[next].Focus(FocusState.Keyboard);
            }
            return;
        }

        if (e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)
        {
            e.Handled = true;
            await OpenNewItemAsync(cell);
        }
    }

    #endregion

    /// <summary>
    /// 그 날짜에 새 일정·할 일을 만든다 — 마우스로 칸을 누를 때와 키보드로 Enter·Space 를 누를 때 같은 길.
    /// </summary>
    private async Task OpenNewItemAsync(DayCell cell)
    {
        if (cell.Dayinfo == null) return;

        try
        {
            var dialog = new UnifiedItemDialog(cell.Dayinfo.Date)
            {
                XamlRoot = this.XamlRoot
            };

            var result = await MessageBox.ShowDialogAsync(dialog);

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
        await MessageBox.ShowDialogAsync(dialog);

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
            NewSchool.Logging.Log.Error("Kcalendar", $"오류 안내를 띄우지 못했다 — 사용자는 아무 반응도 못 본다: {message}", ex);
            Debug.WriteLine($"[Kcalendar] 내부 오류: {ex.Message}");
        }
    }
}
