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
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NewSchool.Controls;
using NewSchool.Models;
using Windows.UI;

namespace NewSchool.Scheduler;

#region DayInfo Class
/// <summary>
/// 일별 정보를 담는 데이터 클래스
/// ✅ Ktask → KEvent 통합: Tasks는 KEvent(ItemType="task") 리스트
/// </summary>
public sealed class DayInfo : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private DateTime _date;
    private string _dateName = string.Empty;
    private bool _isHoliday;
    private bool _isVacation;
    private bool _isToday;
    private List<KEvent> _tasks;
    private List<KEvent> _events;
    private List<SchoolSchedule> _schoolSchedules;

    public DateTime Date
    {
        get => _date;
        set
        {
            if (_date != value)
            {
                _date = value;
                OnPropertyChanged();
                UpdateTodayStatus();
            }
        }
    }

    public string DateName
    {
        get => _dateName;
        set
        {
            if (_dateName != value)
            {
                _dateName = value ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    public bool IsHoliday
    {
        get => _isHoliday;
        set
        {
            if (_isHoliday != value)
            {
                _isHoliday = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsVacation
    {
        get => _isVacation;
        set
        {
            if (_isVacation != value)
            {
                _isVacation = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsToday
    {
        get => _isToday;
        set
        {
            if (_isToday != value)
            {
                _isToday = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>할 일 목록 (KEvent, ItemType="task")</summary>
    public List<KEvent> Tasks
    {
        get => _tasks;
        set
        {
            if (_tasks != value)
            {
                _tasks = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>일정 목록 (KEvent, ItemType="event")</summary>
    public List<KEvent> Events
    {
        get => _events;
        set
        {
            if (_events != value)
            {
                _events = value;
                OnPropertyChanged();
            }
        }
    }

    public List<SchoolSchedule> SchoolSchedules
    {
        get => _schoolSchedules;
        set
        {
            if (_schoolSchedules != value)
            {
                _schoolSchedules = value;
                OnPropertyChanged();
                UpdateDateInfo();
            }
        }
    }

    // 기본 생성자
    public DayInfo()
    {
        _date = DateTime.Now;
        _tasks = new List<KEvent>();
        _events = new List<KEvent>();
        _schoolSchedules = new List<SchoolSchedule>();
        UpdateTodayStatus();
    }

    // 파라미터 생성자
    public DayInfo(DateTime date, List<SchoolSchedule> schedules, List<KEvent> tasks, List<KEvent>? events = null)
    {
        _date = date;
        _schoolSchedules = schedules ?? new List<SchoolSchedule>();
        _tasks = tasks ?? new List<KEvent>();
        _events = events ?? new List<KEvent>();

        UpdateDateInfo();
        UpdateTodayStatus();
    }

    private void UpdateDateInfo()
    {
        if (_schoolSchedules != null && _schoolSchedules.Any())
        {
            DateName = string.Join(", ", _schoolSchedules.Select(x => x.EVENT_NM));
            IsHoliday = _schoolSchedules.Any(x => x.SBTR_DD_SC_NM?.Equals("공휴일") == true);
            IsVacation = _schoolSchedules.Any(x => x.SBTR_DD_SC_NM?.Equals("휴업일") == true);
        }
        else
        {
            DateName = string.Empty;
            IsHoliday = false;
            IsVacation = false;
        }
    }

    private void UpdateTodayStatus()
    {
        IsToday = _date.Date == DateTime.Now.Date;
    }
}
#endregion

#region DayCell Control
/// <summary>
/// 달력의 날짜 셀 컨트롤
/// ✅ Ktask → KEvent 통합 완료
/// </summary>
public sealed partial class DayCell : UserControl
{
    #region x:Bind 함수 바인딩용 static 메서드

    public static string ToStatusLabel(bool isDone) => isDone ? "완료" : "진행";

    public static SolidColorBrush ToStatusColor(bool isDone) => isDone
        ? new(Colors.Gray)
        : new(ColorHelper.FromArgb(255, 0, 120, 215));

    /// <summary>KEvent 색상 문자열을 SolidColorBrush로 변환</summary>
    public static SolidColorBrush EventColorToBrush(string colorHex)
    {
        if (!string.IsNullOrEmpty(colorHex))
        {
            try
            {
                colorHex = colorHex.TrimStart('#');
                byte r = Convert.ToByte(colorHex[0..2], 16);
                byte g = Convert.ToByte(colorHex[2..4], 16);
                byte b = Convert.ToByte(colorHex[4..6], 16);
                return new SolidColorBrush(Color.FromArgb(255, r, g, b));
            }
            catch { /* 파싱 실패 시 기본색 */ }
        }
        return new SolidColorBrush(Color.FromArgb(255, 66, 133, 244)); // Google Blue
    }

    #endregion

    /// <summary>일정/할일 변경 시 부모 캘린더에 전체 새로고침 요청</summary>
    public event EventHandler? CellChanged;

    #region Fields
    // 평일용 브러시(_normalBrush = 검정)는 없앴다 — 평일은 색을 걸지 않고 테마 기본 글자색을
    // 그대로 쓴다(ApplyForeground 참고). 아래 색들은 요일·휴일 표시라 테마와 무관하게 고정이다.
    private readonly SolidColorBrush _holidayBrush;
    private readonly SolidColorBrush _saturdayBrush;
    private readonly SolidColorBrush _sundayBrush;
    private readonly SolidColorBrush _vacationBrush;
    private readonly SolidColorBrush _taskHoverBrush;
    private readonly SolidColorBrush _transparentBrush;

    private bool _isInitialized = false;
    private DayInfo? _pendingDayInfo;

    // TasksRepeater 가 아직 로드되지 않았을 때 마지막으로 요청된 표시 대상.
    // 구독은 _tasksLoadedHooked 로 1회만 걸어 핸들러 누적을 막는다.
    private DayInfo? _pendingTasksDayInfo;
    private bool _tasksLoadedHooked;
    #endregion

    #region Properties
    // 격자 위치를 담아 두던 Position 은 대입만 하고 읽는 곳이 없어 지웠다(2026-08-31).
    // 배치는 Grid.SetRow/SetColumn 이 하고, 셀이 어느 날인지는 Dayinfo.Date 가 안다.

    public DayInfo Dayinfo
    {
        get => (DayInfo)GetValue(DayinfoProperty);
        set => SetValue(DayinfoProperty, value);
    }

    public static readonly DependencyProperty DayinfoProperty =
        DependencyProperty.Register(
            nameof(Dayinfo),
            typeof(DayInfo),
            typeof(DayCell),
            new PropertyMetadata(null, OnDayinfoChanged));
    #endregion

    #region Constructor
    public DayCell()
    {
        this.InitializeComponent();

        // 색상 초기화 (인스턴스 브러시 — DependencyObject로 static 불가)
        _holidayBrush = new SolidColorBrush(Color.FromArgb(255, 255, 68, 68));
        _saturdayBrush = new SolidColorBrush(Color.FromArgb(255, 68, 68, 255));
        _sundayBrush = new SolidColorBrush(Color.FromArgb(255, 255, 68, 68));
        _vacationBrush = new SolidColorBrush(Color.FromArgb(255, 255, 165, 0));
        // 할 일 항목 호버 배경. 그 위 글씨는 테마 기본색이라 배경도 테마를 따라가야
        // 다크에서 "밝은 배경 + 밝은 글씨"가 되지 않는다.
        _taskHoverBrush = (SolidColorBrush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        _transparentBrush = new SolidColorBrush(Colors.Transparent);

        // Loaded 이벤트에서 초기화
        this.Loaded += DayCell_Loaded;
    }

    // ItemsRepeater 컨테이너 재활용 타이밍에 기대는 code-behind(ElementPrepared) 방식은 신뢰성이
    // 낮아, EventColorToBrush(DisplayColor)처럼 이미 검증된 x:Bind 정적 메서드 패턴으로 통일한다.
    // 컨테이너가 재사용될 때마다 x:Bind가 다시 평가되므로 설정 변경이 항상 반영된다.
    // 설정 다이얼로그의 "할 일 폰트"가 DayCell의 이벤트/할일 목록을 함께 관장한다(레이블과 달리
    // "이벤트 폰트"는 예전부터 TbDateName=학사일정 텍스트를 가리켰음 — UpdateDateDisplay 참고).
    public static double GetEventTitleFontSize(string _) => Settings.TaskFontSize.Value;
    public static double GetEventTimeFontSize(string _) => Math.Max(7, Settings.TaskFontSize.Value - 1);
    public static double GetTaskTitleFontSize(string _) => Settings.TaskFontSize.Value;

    private void DayCell_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;

        Debug.WriteLine($"[DayCell] Loaded 이벤트 발생");

        try
        {
            _isInitialized = true;

            // 대기 중인 DayInfo가 있다면 즉시 적용
            if (_pendingDayInfo != null)
            {
                Debug.WriteLine($"[DayCell] Pending DayInfo 적용: {_pendingDayInfo.Date:yyyy-MM-dd}");
                UpdateDayDisplaySync(_pendingDayInfo);
                _pendingDayInfo = null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DayCell] Loaded 오류: {ex.Message}");
        }
        finally
        {
            this.Loaded -= DayCell_Loaded;
        }
    }
    #endregion

    #region Dependency Property Changed Callbacks

    private static void OnDayinfoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DayCell cell) return;

        try
        {
            var newDayInfo = e.NewValue as DayInfo;

            if (newDayInfo == null)
            {
                System.Diagnostics.Debug.WriteLine("OnDayinfoChanged: DayInfo is null");
                return;
            }

            // DependencyProperty callback은 이미 UI 스레드에서 호출됨
            if (cell._isInitialized)
            {
                cell.UpdateDayDisplaySync(newDayInfo);
            }
            else
            {
                cell._pendingDayInfo = newDayInfo;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OnDayinfoChanged 오류: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"스택 트레이스: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// 완료 토글 버튼 클릭 (KEvent task)
    /// </summary>
    private async void TaskToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not KEvent task)
            return;

        try
        {
            // 되돌리기용 이전 상태 보관 — DB 저장이 실패하면 화면과 DB 가 갈라지므로
            // 반드시 원상 복구해야 한다.
            bool prevIsDone = task.IsDone;
            string prevUpdated = task.Updated;
            string prevCompleted = task.Completed;

            task.IsDone = !task.IsDone;
            task.Updated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            Debug.WriteLine($"[DayCell] 작업 상태 변경: {task.Title}, IsDone={task.IsDone}");

            if (task.IsDone)
            {
                task.Completed = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            }
            else
            {
                task.Completed = string.Empty;
            }

            // ✅ Service를 통한 비동기 업데이트
            try
            {
                using var service = Scheduler.CreateService();

                // 반영 여부를 확인한다. 예외만 잡던 때는 <b>0행 갱신</b>(이미 지워진 할 일)이
                // 그물을 빠져나갔다 — 예외가 아니라 false 로 돌아오기 때문이다. 그러면 화면은
                // 완료, DB 는 미완료로 갈라진 채 다음 새로고침에 소리 없이 되돌아간다.
                // (목록 보기 KAgendaControl.TaskToggle_Click 은 이미 이렇게 확인한다.)
                if (!await service.UpdateTaskAsync(task))
                    throw new InvalidOperationException("변경된 항목이 없습니다. 이미 지워진 할 일일 수 있습니다.");

                Debug.WriteLine($"[DayCell] 작업 상태 업데이트 완료: {task.No}");
            }
            catch (Exception ex)
            {
                // 사용자가 직접 누른 동작이 실패한 경우다 → 상태를 복구하고 알린다.
                Debug.WriteLine($"[DayCell] 작업 상태 업데이트 오류: {ex.Message}");

                task.IsDone = prevIsDone;
                task.Updated = prevUpdated;
                task.Completed = prevCompleted;

                if (Dayinfo != null)
                {
                    await UpdateDayDisplayAsync(Dayinfo);
                }

                await UserErrorReporter.ReportAsync("할 일 상태 변경", ex);
                return;
            }

            // 표시 업데이트 (필요시)
            if (Dayinfo != null)
            {
                await UpdateDayDisplayAsync(Dayinfo);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DayCell] 작업 체크박스 처리 오류: {ex.Message}");
        }
    }

    // 새 항목 추가(AddNewTaskAsync)는 이를 부르던 OnCellDoubleClick 과 함께 지웠다(39차) —
    // 날짜 칸에서 항목을 만드는 길은 달력 쪽 DayCell_PointerPressed 하나뿐이다.

    private async void TaskItem_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Grid grid || grid.Tag is not KEvent clickedTask) return;

        try
        {
            var dialog = new UnifiedItemDialog(clickedTask)
            {
                XamlRoot = this.XamlRoot
            };
            var result = await MessageBox.ShowDialogAsync(dialog);

            if (result == ContentDialogResult.Primary || result == ContentDialogResult.Secondary)
            {
                // DB에서 전체 새로고침 (날짜 변경, 삭제, 반복 생성 등 반영)
                CellChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DayCell] 작업 편집 오류: {ex.Message}");
            await MessageBox.ShowAsync($"작업 편집 오류: {ex.Message}");
        }
    }

    #endregion

    #region Display Update Methods
    /// <summary>
    /// 동기적으로 DayInfo 표시 업데이트
    /// </summary>
    private void UpdateDayDisplaySync(DayInfo dayInfo)
    {
        if (dayInfo == null)
        {
            Debug.WriteLine($"[DayCell] UpdateDayDisplaySync - dayInfo가 null");
            return;
        }

        try
        {
            Debug.WriteLine($"[DayCell] 표시 업데이트 시작: {dayInfo.Date:yyyy-MM-dd}");

            UpdateDateDisplay(dayInfo);
            UpdateColorDisplay(dayInfo);
            // KEvent 표시 — 새 리스트로 참조 변경하여 UI 갱신 보장
            if (EventsRepeater != null)
            {
                EventsRepeater.ItemsSource = null;
                if (dayInfo.Events?.Count > 0)
                    EventsRepeater.ItemsSource = new List<KEvent>(dayInfo.Events);
            }
            UpdateTasksDisplay(dayInfo);

            Debug.WriteLine($"[DayCell] 표시 업데이트 완료: {dayInfo.Date:yyyy-MM-dd}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DayCell] 표시 업데이트 오류: {ex.Message}");
            Debug.WriteLine($"[DayCell] 스택: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// 비동기 표시 업데이트 — UI 스레드 보장만 담당하고, 실제 갱신은
    /// <see cref="UpdateDayDisplaySync"/> 한 곳에 위임한다.
    ///
    /// 예전에는 이 메서드가 날짜·이벤트·할 일 갱신을 따로 복제해 갖고 있었다.
    /// 그 결과 <see cref="UpdateColorDisplay"/> 호출이 이쪽에만 빠져 있는 등
    /// 두 경로가 조용히 어긋났고, 실제로 한 번 버그가 났다(할 일 필터가 한쪽에만 적용).
    /// 표시 로직은 갈래를 만들지 않는다.
    /// </summary>
    public async Task UpdateDayDisplayAsync(DayInfo dayInfo)
    {
        if (dayInfo == null) return;

        if (!this.DispatcherQueue.HasThreadAccess)
        {
            await this.DispatcherQueue.EnqueueAsync(() => UpdateDayDisplaySync(dayInfo));
            return;
        }

        UpdateDayDisplaySync(dayInfo);
    }
    private void UpdateDateDisplay(DayInfo dayInfo)
    {
        try
        {
            // 날짜 표시
            if (LbDate != null)
            {
                LbDate.Text = dayInfo.Date.Day.ToString();
                LbDate.FontSize = Settings.DateFontSize.Value;
            }

            // 날짜 이름 표시
            if (TbDateName != null)
            {
                TbDateName.Text = dayInfo.DateName ?? string.Empty;
                // 설정 다이얼로그의 "학사일정 폰트"(EventFontSize)가 이 텍스트를 가리킴
                TbDateName.FontSize = Settings.EventFontSize.Value;
                TbDateName.Visibility = string.IsNullOrEmpty(dayInfo.DateName)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            // 오늘 날짜 강조: 셀 전체 테두리(TodayHighlight)만으로 표시
            if (TodayHighlight != null)
            {
                TodayHighlight.Visibility = dayInfo.IsToday
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DayCell] UpdateDateDisplay 오류: {ex.Message}");
        }
    }

    private void UpdateColorDisplay(DayInfo dayInfo)
    {
        try
        {
            // 날짜 숫자: 공휴일(최우선) → 일요일 → 토요일 → 평일. 휴업일은 날짜 색에 영향 없음.
            //
            // ⚠ 평일은 색을 **지정하지 않는다**(null → ClearValue). 예전에는 검정을 박았는데,
            //    셀 배경은 ThemeResource(LayerFillColorDefaultBrush)라 테마를 따라가므로
            //    **다크 테마에서 어두운 배경 위 검정 글씨가 되어 평일 날짜가 보이지 않았다**
            //    (공휴일·토·일은 제 색이 있어 멀쩡하니 평일만 사라진다). 지정하지 않으면
            //    TextBlock 이 테마 기본 글자색을 상속해 라이트·다크 모두 제대로 보인다.
            SolidColorBrush? dateBrush;
            if (dayInfo.IsHoliday) dateBrush = _holidayBrush;
            else if (dayInfo.Date.DayOfWeek == DayOfWeek.Sunday) dateBrush = _sundayBrush;
            else if (dayInfo.Date.DayOfWeek == DayOfWeek.Saturday) dateBrush = _saturdayBrush;
            else dateBrush = null;

            // 학사일정 텍스트: 휴일 → 휴업일 → 나머지(테마 기본색). 요일은 텍스트 색에 영향 없음.
            SolidColorBrush? scheduleBrush;
            if (dayInfo.IsHoliday) scheduleBrush = _holidayBrush;
            else if (dayInfo.IsVacation) scheduleBrush = _vacationBrush;
            else scheduleBrush = null;

            // 색상 적용 (오늘 강조는 셀 테두리(TodayHighlight)가 담당하므로 날짜 색은 평소 규칙 그대로)
            ApplyForeground(LbDate, dateBrush);
            ApplyForeground(TbDateName, scheduleBrush);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DayCell] UpdateColorDisplay 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 글자색을 건다. <paramref name="brush"/> 가 null 이면 <b>되돌린다</b> —
    /// 지정을 지워야 TextBlock 이 테마 기본 글자색을 상속한다(그냥 두면 이전에 걸어 둔
    /// 빨강·주황이 남는다. 셀은 재활용되므로 어제 공휴일이던 칸이 오늘 평일이 될 수 있다).
    /// </summary>
    private static void ApplyForeground(TextBlock? target, SolidColorBrush? brush)
    {
        if (target == null) return;

        if (brush == null) target.ClearValue(TextBlock.ForegroundProperty);
        else target.Foreground = brush;
    }

    /// <summary>
    /// TasksRepeater 로드 완료 시 1회 실행 — 가장 최근에 요청된 dayInfo 로만 갱신한다.
    /// </summary>
    private void OnTasksRepeaterLoaded(object sender, RoutedEventArgs e)
    {
        if (TasksRepeater != null)
            TasksRepeater.Loaded -= OnTasksRepeaterLoaded;

        _tasksLoadedHooked = false;

        var pending = _pendingTasksDayInfo;
        _pendingTasksDayInfo = null;

        if (pending != null)
            UpdateTasksDisplay(pending);
    }

    private void UpdateTasksDisplay(DayInfo dayInfo)
    {
        try
        {
            // UI 스레드 확인
            if (!this.DispatcherQueue.HasThreadAccess)
            {
                Debug.WriteLine("[DayCell] UpdateTasksDisplay - UI 스레드가 아님!");
                this.DispatcherQueue.TryEnqueue(() => UpdateTasksDisplay(dayInfo));
                return;
            }

            if (TasksRepeater == null)
            {
                Debug.WriteLine("[DayCell] TasksRepeater가 null");
                return;
            }

            // ✅ ItemsRepeater가 로드되었는지 확인
            if (!TasksRepeater.IsLoaded)
            {
                Debug.WriteLine("[DayCell] TasksRepeater가 아직 로드되지 않음");

                // 로드 완료 후 재시도한다. 지역 함수를 매번 새로 구독하면 호출마다
                // 다른 델리게이트 인스턴스가 쌓이고(-= 는 자기 것만 제거) 로드 시점에
                // 전부 발동하면서 오래된 dayInfo 로 덮어쓸 수 있다.
                // → 대기 중인 dayInfo 만 갱신하고 구독은 1회로 고정한다.
                _pendingTasksDayInfo = dayInfo;

                if (!_tasksLoadedHooked)
                {
                    _tasksLoadedHooked = true;
                    TasksRepeater.Loaded += OnTasksRepeaterLoaded;
                }
                return;
            }

            // Settings.ShowTasks("할 일 표시/숨김")는 Kcalendar.LoadCalendarDataAsync 가
            // 조회 단계에서 이미 적용한다(꺼져 있으면 task 가 아예 실려오지 않고, 설정을
            // 바꾸면 RefreshCalendarAsync 로 다시 읽는다). 여기서 한 번 더 거르면
            // 판정이 두 곳으로 갈리므로 받은 목록을 그대로 쓴다.
            var displayTasks = dayInfo?.Tasks ?? new List<KEvent>();

            // ✅ ItemsSource 변경 전 기존 바인딩 해제
            if (TasksRepeater.ItemsSource != null)
            {
                // 기존 ItemsSource가 같으면 스킵
                if (TasksRepeater.ItemsSource is List<KEvent> currentTasks &&
                    currentTasks.SequenceEqual(displayTasks))
                {
                    Debug.WriteLine("[DayCell] ItemsSource가 동일함, 스킵");
                    return;
                }

                // null로 설정하여 기존 바인딩 해제
                TasksRepeater.ItemsSource = null;
            }

            // ✅ 새 리스트로 설정 (참조 변경을 위해 새 리스트 생성)
            TasksRepeater.ItemsSource = new List<KEvent>(displayTasks);

            // 작업 개수 배지 업데이트
            if (TaskCountBadge != null && TaskCountText != null)
            {
                if (displayTasks.Count > 0)
                {
                    TaskCountText.Text = displayTasks.Count.ToString();
                    TaskCountBadge.Visibility = Visibility.Visible;
                }
                else
                {
                    TaskCountBadge.Visibility = Visibility.Collapsed;
                }
            }

            Debug.WriteLine($"[DayCell] 작업 표시 완료: {displayTasks.Count}개");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DayCell] UpdateTasksDisplay 오류: {ex.GetType().FullName}");
            Debug.WriteLine($"[DayCell] Message: {ex.Message}");

            // 오류 발생 시 안전 모드 — ItemsSource 초기화
            try
            {
                if (TasksRepeater != null && TasksRepeater.IsLoaded)
                {
                    TasksRepeater.ItemsSource = new List<KEvent>();
                }
            }
            catch
            {
                // 무시
            }
        }
    }
    #endregion

    #region Event Handlers

    // 셀 호버 효과
    private void BrdBase_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (HoverHighlight != null)
            HoverHighlight.Opacity = 1;
    }

    private void BrdBase_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (HoverHighlight != null)
            HoverHighlight.Opacity = 0;
    }

    private void TaskItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid) grid.Background = _taskHoverBrush;
    }

    private void TaskItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid) grid.Background = _transparentBrush;
    }

    private async void EventItem_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border border || border.Tag is not KEvent clickedEvent) return;

        try
        {
            var dialog = new UnifiedItemDialog(clickedEvent)
            {
                XamlRoot = this.XamlRoot
            };
            var result = await MessageBox.ShowDialogAsync(dialog);

            if (result == ContentDialogResult.Primary || result == ContentDialogResult.Secondary)
            {
                // DB에서 전체 새로고침 (날짜 변경, 삭제, 다일 일정 등 반영)
                CellChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DayCell] 이벤트 편집 오류: {ex.Message}");
        }
    }

    private void EventItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
            border.Background = _taskHoverBrush;
    }

    private void EventItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
            border.Background = _transparentBrush;
    }

    #endregion
}
#endregion

#region Converters
public sealed partial class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        try
        {
            return value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            return Visibility.Collapsed;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        try
        {
            return value is Visibility v && v == Visibility.Visible;
        }
        catch
        {
            return false;
        }
    }
}

// 쓰이지 않던 컨버터 셋을 지웠다(2026-08-31, XAML 참조 0건):
//   · DayOfWeekToColorConverter — 평일에 Colors.Black 을 박고 있었다. 바로 위
//     UpdateColorDisplay 가 길게 적어 두고 고친 다크 테마 결함(어두운 배경 위 검정 글씨라
//     평일 날짜가 안 보임)을 그대로 되살리는 코드라, 남겨 두면 갖다 쓰는 순간 버그가 돌아온다.
//   · TaskCompletionToTextDecorationConverter, BoolToTextDecorationsConverter — 서로 같은 일을
//     하는 사본 둘. 취소선은 KEvent.TextDecorations / AgendaItem.Decorations 가 직접 낸다.

/// <summary>비어있지 않은 string이면 Visible, 비어있으면 Collapsed</summary>
public sealed partial class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is string s && !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}


#endregion
