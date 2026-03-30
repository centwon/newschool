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
using Windows.UI;
using NewSchool.Models;
using NewSchool.Controls;

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

    /// <summary>TimeLabel 비어있으면 Collapsed (EventsRepeater용)</summary>
    public static Visibility IsNotEmpty(string text)
        => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

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
    private readonly SolidColorBrush _normalBrush;
    private readonly SolidColorBrush _holidayBrush;
    private readonly SolidColorBrush _saturdayBrush;
    private readonly SolidColorBrush _sundayBrush;
    private readonly SolidColorBrush _vacationBrush;
    private readonly SolidColorBrush _taskHoverBrush;

    private bool _isInitialized = false;
    private DayInfo? _pendingDayInfo;
    #endregion

    #region Properties
    public (int Row, int Column) Position { get; set; } = (-1, -1);

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

        // 색상 초기화
        _normalBrush = new SolidColorBrush(Colors.Black);
        _holidayBrush = new SolidColorBrush(Color.FromArgb(255, 255, 68, 68));
        _saturdayBrush = new SolidColorBrush(Color.FromArgb(255, 68, 68, 255));
        _sundayBrush = new SolidColorBrush(Color.FromArgb(255, 255, 68, 68));
        _vacationBrush = new SolidColorBrush(Color.FromArgb(255, 255, 165, 0));
        _taskHoverBrush = new SolidColorBrush(Color.FromArgb(255, 230, 244, 255));

        // Loaded 이벤트에서 초기화
        this.Loaded += DayCell_Loaded;
    }

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
            await Task.Run(async () =>
            {
                try
                {
                    using var service = Scheduler.CreateService();
                    await service.UpdateTaskAsync(task);
                    Debug.WriteLine($"[DayCell] 작업 상태 업데이트 완료: {task.No}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DayCell] 작업 상태 업데이트 오류: {ex.Message}");
                }
            });

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

    public async Task AddNewTaskAsync()
    {
        if (Dayinfo == null) return;

        try
        {
            var dialog = new UnifiedItemDialog(Dayinfo.Date)
            {
                XamlRoot = this.XamlRoot
            };
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && dialog.ResultEvent != null)
            {
                // DB에서 전체 새로고침 (반복 생성, 다일 일정 등 반영)
                CellChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"항목 추가 오류: {ex.Message}");
        }
    }

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
            var result = await dialog.ShowAsync();

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
    /// 비동기 업데이트 (필요 시 사용)
    /// </summary>
    public async Task UpdateDayDisplayAsync(DayInfo dayInfo)
    {
        if (dayInfo == null)
        {
            Debug.WriteLine($"[DayCell] UpdateDayDisplayAsync: dayInfo가 null입니다.");
            return;
        }

        try
        {
            Debug.WriteLine($"[DayCell] 표시 업데이트 시작: {dayInfo.Date:yyyy-MM-dd}");

            // UI 스레드에서 실행되는지 확인
            if (!this.DispatcherQueue.HasThreadAccess)
            {
                Debug.WriteLine($"[DayCell] 잘못된 스레드에서 호출됨. DispatcherQueue로 전환.");
                await this.DispatcherQueue.EnqueueAsync(async () =>
                {
                    await UpdateDayDisplayAsync(dayInfo);
                });
                return;
            }

            // ✅ 여기서부터는 UI 스레드에서 실행됨이 보장됨

            // ✅ 일자 표시
            if (LbDate != null)
            {
                LbDate.Text = dayInfo.Date.Day.ToString();
            }
            else
            {
                Debug.WriteLine($"[DayCell] 경고: LbDate가 null입니다.");
            }

            // ✅ 날짜 이름 표시 (공휴일 등)
            if (TbDateName != null)
            {
                TbDateName.Text = dayInfo.DateName ?? string.Empty;
                TbDateName.Visibility = string.IsNullOrEmpty(dayInfo.DateName)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            // ✅ 오늘 날짜 강조
            if (TodayHighlight != null)
            {
                TodayHighlight.Visibility = dayInfo.IsToday
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            // ✅ 이벤트 표시 (EventsRepeater) — 새 리스트로 참조 변경하여 UI 갱신 보장
            if (EventsRepeater != null)
            {
                EventsRepeater.ItemsSource = null;
                if (dayInfo.Events?.Count > 0)
                    EventsRepeater.ItemsSource = new List<KEvent>(dayInfo.Events);
            }

            // ✅ 작업 표시 (ItemsRepeater 사용) — 새 리스트로 참조 변경하여 UI 갱신 보장
            if (TasksRepeater != null)
            {
                if (dayInfo.Tasks?.Count > 0)
                {
                    Debug.WriteLine($"[DayCell] 작업 표시 시작: {dayInfo.Tasks.Count}개");

                    try
                    {
                        TasksRepeater.ItemsSource = null;
                        TasksRepeater.ItemsSource = new List<KEvent>(dayInfo.Tasks);

                        Debug.WriteLine($"[DayCell] 작업 표시 완료: {dayInfo.Tasks.Count}개");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[DayCell] ItemsRepeater 설정 오류: {ex.Message}");
                    }
                }
                else
                {
                    TasksRepeater.ItemsSource = null;
                }
            }
            else
            {
                Debug.WriteLine($"[DayCell] 경고: TasksRepeater가 null입니다.");
            }

            // ✅ 작업 개수 배지 표시
            if (TaskCountBadge != null && TaskCountText != null)
            {
                int taskCount = dayInfo.Tasks?.Count ?? 0;
                if (taskCount > 0)
                {
                    TaskCountText.Text = taskCount.ToString();
                    TaskCountBadge.Visibility = Visibility.Visible;
                }
                else
                {
                    TaskCountBadge.Visibility = Visibility.Collapsed;
                }
            }

            Debug.WriteLine($"[DayCell] 표시 업데이트 완료: {dayInfo.Date:yyyy-MM-dd}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DayCell] UpdateDayDisplayAsync 오류: {ex.Message}");
            Debug.WriteLine($"[DayCell] 스택 트레이스: {ex.StackTrace}");

            // ✅ COMException인 경우 추가 정보
            if (ex is System.Runtime.InteropServices.COMException comEx)
            {
                Debug.WriteLine($"[DayCell] COM 오류 코드: 0x{comEx.ErrorCode:X8}");
            }

            throw;
        }
    }
    private void UpdateDateDisplay(DayInfo dayInfo)
    {
        try
        {
            // 날짜 표시
            if (LbDate != null)
            {
                LbDate.Text = dayInfo.Date.Day.ToString();
            }

            // 날짜 이름 표시
            if (TbDateName != null)
            {
                TbDateName.Text = dayInfo.DateName ?? string.Empty;
                TbDateName.Visibility = string.IsNullOrEmpty(dayInfo.DateName)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            // 오늘 날짜 강조
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
            SolidColorBrush targetBrush;

            if (dayInfo.IsHoliday)
            {
                targetBrush = _holidayBrush;
            }
            else if (dayInfo.IsVacation)
            {
                targetBrush = _vacationBrush;
            }
            else if (dayInfo.Date.DayOfWeek == DayOfWeek.Sunday)
            {
                targetBrush = _sundayBrush;
            }
            else if (dayInfo.Date.DayOfWeek == DayOfWeek.Saturday)
            {
                targetBrush = _saturdayBrush;
            }
            else
            {
                targetBrush = _normalBrush;
            }

            // 색상 적용
            if (LbDate != null)
            {
                LbDate.Foreground = targetBrush;
            }
            if (TbDateName != null)
            {
                TbDateName.Foreground = targetBrush;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DayCell] UpdateColorDisplay 오류: {ex.Message}");
        }
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

                // Loaded 이벤트 대기 후 업데이트
                void OnLoaded(object sender, RoutedEventArgs e)
                {
                    TasksRepeater.Loaded -= OnLoaded;
                    UpdateTasksDisplay(dayInfo);
                }

                TasksRepeater.Loaded += OnLoaded;
                return;
            }

            var tasks = dayInfo?.Tasks ?? new List<KEvent>();
            var displayTasks = tasks.Where(t => !t.IsDone || Settings.ShowTasks).ToList();

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
    private void TaskItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid) grid.Background = _taskHoverBrush;
    }

    private void TaskItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid) grid.Background = new SolidColorBrush(Colors.Transparent);
    }

    public async void OnCellDoubleClick()
    {
        await AddNewTaskAsync();
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
            var result = await dialog.ShowAsync();

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
            border.Opacity = 0.8;
    }

    private void EventItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
            border.Opacity = 1.0;
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

public sealed partial class DayOfWeekToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        try
        {
            if (value is DayOfWeek dayOfWeek)
            {
                return dayOfWeek switch
                {
                    DayOfWeek.Sunday => new SolidColorBrush(Color.FromArgb(255, 255, 68, 68)),
                    DayOfWeek.Saturday => new SolidColorBrush(Color.FromArgb(255, 68, 68, 255)),
                    _ => new SolidColorBrush(Colors.Black)
                };
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DayOfWeekToColorConverter 오류: {ex.Message}");
        }
        return new SolidColorBrush(Colors.Black);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed partial class TaskCompletionToTextDecorationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        try
        {
            return value is bool isDone && isDone
                ? Windows.UI.Text.TextDecorations.Strikethrough
                : Windows.UI.Text.TextDecorations.None;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TaskCompletionToTextDecorationConverter 오류: {ex.Message}");
            return Windows.UI.Text.TextDecorations.None;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        try
        {
            return value is Windows.UI.Text.TextDecorations decorations
                && decorations == Windows.UI.Text.TextDecorations.Strikethrough;
        }
        catch
        {
            return false;
        }
    }
}
/// <summary>비어있지 않은 string이면 Visible, 비어있으면 Collapsed</summary>
public sealed partial class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is string s && !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public sealed partial class BoolToTextDecorationsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        try
        {
            if (value is bool isDone && isDone)
            {
                return Windows.UI.Text.TextDecorations.Strikethrough;
            }
            return Windows.UI.Text.TextDecorations.None;
        }
        catch
        {
            return Windows.UI.Text.TextDecorations.None;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}


#endregion
