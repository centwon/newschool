using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NewSchool.Controls;
using Windows.UI;

namespace NewSchool.Scheduler;

/// <summary>
/// 할 일(KEvent task) / 일정(KEvent event) 통합 다이얼로그
/// ✅ Ktask → KEvent 통합 완료: 단일 ResultEvent 출력
/// </summary>
public sealed partial class UnifiedItemDialog : ContentDialog
{
    #region State

    /// <summary>결과 KEvent (task 또는 event)</summary>
    public KEvent? ResultEvent { get; private set; }

    private KEvent _taskEvent;   // ItemType="task" 용
    private KEvent _event;       // ItemType="event" 용

    private bool _isTaskMode = true;    // true=할일, false=일정
    private bool _isNew = true;
    private bool _isChanged = false;
    private bool _isInitialized = false;

    private List<KCalendarList> _calendars = [];

    #endregion

    #region Constructors

    /// <summary>새 항목 (날짜만 지정)</summary>
    public UnifiedItemDialog(DateTime date)
    {
        _taskEvent = NewTaskEvent(date);
        _event = NewEvent(date);
        _isNew = true;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>기존 KEvent 수정 (task 또는 event 자동 판별)</summary>
    public UnifiedItemDialog(KEvent ev)
    {
        if (ev.ItemType == "task")
        {
            _taskEvent = ev;
            _event = NewEvent(ev.Start);
            _isTaskMode = true;
        }
        else
        {
            _taskEvent = NewTaskEvent(ev.Start);
            _event = ev;
            _isTaskMode = false;
        }
        _isNew = ev.No < 0;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    #endregion

    #region Initialization

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;
        Loaded -= OnLoaded;

        try
        {
            await Task.Delay(50);

            // 캘린더 목록 로드
            await LoadListsAsync();

            // 탭 설정
            RbTypeTask.IsChecked  = _isTaskMode;
            RbTypeEvent.IsChecked = !_isTaskMode;

            // 패널 표시
            UpdatePanelVisibility();

            // 값 채우기
            if (_isTaskMode) FillTaskForm();
            else FillEventForm();

            // 신규가 아니면 삭제 버튼 표시
            SecondaryButtonText = _isNew ? string.Empty : "삭제";
            Title = _isNew ? "새 항목" : (_isTaskMode ? "할 일 수정" : "일정 수정");

            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UnifiedItemDialog] 초기화 오류: {ex.Message}");
        }
    }

    private async Task LoadListsAsync()
    {
        try
        {
            using var service = Scheduler.CreateService();
            _calendars = await service.GetAllCalendarsAsync();

            // 할 일 캘린더 (동일한 캘린더 리스트 사용)
            CBoxTaskList.ItemsSource = _calendars.Select(c => c.Title).ToList();
            var taskIdx = _calendars.FindIndex(c => c.No == _taskEvent.CalendarId);
            CBoxTaskList.SelectedIndex = taskIdx >= 0 ? taskIdx : 0;

            // 캘린더 목록
            CBoxCalendar.ItemsSource = _calendars.Select(c => c.Title).ToList();
            var calIdx = _calendars.FindIndex(c => c.No == _event.CalendarId);
            CBoxCalendar.SelectedIndex = calIdx >= 0 ? calIdx : 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UnifiedItemDialog] 목록 로드 오류: {ex.Message}");
        }
    }

    private void FillTaskForm()
    {
        TxtTaskTitle.Text = _taskEvent.Title;
        TaskDueDatePicker.Date = new DateTimeOffset(_taskEvent.Start.Date);
        TaskDueTimePicker.Time = _taskEvent.Start.TimeOfDay;
        ChkTaskAllday.IsChecked = _taskEvent.IsAllday;
        ChkTaskDone.IsChecked = _taskEvent.IsDone;
        TxtTaskNotes.Text = _taskEvent.Notes;

        TaskDueTimePicker.Visibility = _taskEvent.IsAllday ? Visibility.Collapsed : Visibility.Visible;
        GridRepeat.Visibility = _isNew ? Visibility.Visible : Visibility.Collapsed;
        UpdateRepeatLabels();
    }

    private void FillEventForm()
    {
        TxtEventTitle.Text = _event.Title;
        EventStartDatePicker.Date = new DateTimeOffset(_event.Start.Date);
        EventStartTimePicker.Time = _event.Start.TimeOfDay;
        EventEndDatePicker.Date = new DateTimeOffset(_event.End.Date);
        EventEndTimePicker.Time = _event.End.TimeOfDay;
        ChkEventAllday.IsChecked = _event.IsAllday;
        TxtEventLocation.Text = _event.Location;
        TxtEventNotes.Text = _event.Notes;

        EventStartTimePicker.Visibility = _event.IsAllday ? Visibility.Collapsed : Visibility.Visible;
        EventEndTimePicker.Visibility   = _event.IsAllday ? Visibility.Collapsed : Visibility.Visible;

        // 색상 선택
        var colorIdx = FindColorIndex(_event.ColorId);
        CBoxColor.SelectedIndex = colorIdx;
        UpdateColorPreview(_event.ColorId);
    }

    #endregion

    #region Tab Switching

    private void RbType_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        _isTaskMode = RbTypeTask.IsChecked == true;
        UpdatePanelVisibility();
        Title = _isNew ? "새 항목" : (_isTaskMode ? "할 일 수정" : "일정 수정");
    }

    private void UpdatePanelVisibility()
    {
        PanelTask.Visibility  = _isTaskMode ? Visibility.Visible  : Visibility.Collapsed;
        PanelEvent.Visibility = _isTaskMode ? Visibility.Collapsed : Visibility.Visible;
    }

    #endregion

    #region Task Form Events

    private void CBoxTaskList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || CBoxTaskList.SelectedIndex < 0) return;
        if (CBoxTaskList.SelectedIndex < _calendars.Count)
            _taskEvent.CalendarId = _calendars[CBoxTaskList.SelectedIndex].No;
        _isChanged = true;
    }

    private void TaskDueDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (!_isInitialized || !args.NewDate.HasValue) return;
        var newDate = args.NewDate.Value.Date;
        _taskEvent.Start = DateTime.SpecifyKind(newDate + _taskEvent.Start.TimeOfDay, DateTimeKind.Unspecified);
        _taskEvent.End   = DateTime.SpecifyKind(newDate.AddDays(1), DateTimeKind.Unspecified);
        _isChanged = true;
        UpdateRepeatLabels();
    }

    private void TaskDueTimePicker_TimeChanged(object sender, TimePickerValueChangedEventArgs e)
    {
        if (!_isInitialized) return;
        _taskEvent.Start = DateTime.SpecifyKind(_taskEvent.Start.Date + e.NewTime, DateTimeKind.Unspecified);
        _taskEvent.End   = DateTime.SpecifyKind(_taskEvent.Start.AddHours(1), DateTimeKind.Unspecified);
        _isChanged = true;
        UpdateRepeatLabels();
    }

    private void ChkTaskAllday_Click(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        _taskEvent.IsAllday = ChkTaskAllday.IsChecked == true;
        TaskDueTimePicker.Visibility = _taskEvent.IsAllday ? Visibility.Collapsed : Visibility.Visible;
        _isChanged = true;
        UpdateRepeatLabels();
    }

    private void RepeatOption_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        bool showEnd = sender is RadioButton rb && rb != RbNone;
        LbRepeatEnd.Visibility = showEnd ? Visibility.Visible : Visibility.Collapsed;
        PickerEnd.Visibility   = showEnd ? Visibility.Visible : Visibility.Collapsed;
        _isChanged = true;
    }

    private void UpdateRepeatLabels()
    {
        if (!_isInitialized) return;
        string t = _taskEvent.IsAllday ? string.Empty : $" {_taskEvent.Start:HH:mm}";
        if (RbDaily   != null) RbDaily.Content   = $"매일{t}";
        if (RbWeekly  != null) RbWeekly.Content  = $"매주 {KorDow(_taskEvent.Start.DayOfWeek)}{t}";
        if (RbMonthly != null) RbMonthly.Content = $"매월 {_taskEvent.Start.Day}일{t}";
        if (RbYearly  != null) RbYearly.Content  = $"매년 {_taskEvent.Start.Month}월 {_taskEvent.Start.Day}일{t}";
    }

    #endregion

    #region Event Form Events

    private void CBoxCalendar_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || CBoxCalendar.SelectedIndex < 0) return;
        if (CBoxCalendar.SelectedIndex < _calendars.Count)
        {
            var cal = _calendars[CBoxCalendar.SelectedIndex];
            _event.CalendarId = cal.No;
            // 이벤트 고유 색상이 없으면 캘린더 기본색 미리보기
            if (string.IsNullOrEmpty(_event.ColorId))
                UpdateColorPreviewHex(cal.Color);
        }
        _isChanged = true;
    }

    private void EventStartDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (!_isInitialized || !args.NewDate.HasValue) return;
        var newDate = args.NewDate.Value.Date;
        _event.Start = DateTime.SpecifyKind(newDate + _event.Start.TimeOfDay, DateTimeKind.Unspecified);
        // 종료일이 시작일보다 이르면 맞춤
        if (_event.End.Date < newDate)
            _event.End = DateTime.SpecifyKind(newDate + _event.End.TimeOfDay, DateTimeKind.Unspecified);
        _isChanged = true;
    }

    private void EventStartTimePicker_TimeChanged(object sender, TimePickerValueChangedEventArgs e)
    {
        if (!_isInitialized) return;
        _event.Start = DateTime.SpecifyKind(_event.Start.Date + e.NewTime, DateTimeKind.Unspecified);
        _isChanged = true;
    }

    private void EventEndDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (!_isInitialized || !args.NewDate.HasValue) return;
        _event.End = DateTime.SpecifyKind(args.NewDate.Value.Date + _event.End.TimeOfDay, DateTimeKind.Unspecified);
        _isChanged = true;
    }

    private void EventEndTimePicker_TimeChanged(object sender, TimePickerValueChangedEventArgs e)
    {
        if (!_isInitialized) return;
        _event.End = DateTime.SpecifyKind(_event.End.Date + e.NewTime, DateTimeKind.Unspecified);
        _isChanged = true;
    }

    private void ChkEventAllday_Click(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        _event.IsAllday = ChkEventAllday.IsChecked == true;
        EventStartTimePicker.Visibility = _event.IsAllday ? Visibility.Collapsed : Visibility.Visible;
        EventEndTimePicker.Visibility   = _event.IsAllday ? Visibility.Collapsed : Visibility.Visible;
        _isChanged = true;
    }

    private void CBoxColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || CBoxColor.SelectedItem is not ComboBoxItem item) return;
        _event.ColorId = item.Tag?.ToString() ?? string.Empty;
        UpdateColorPreview(_event.ColorId);
        _isChanged = true;
    }

    #endregion

    #region Common Events

    private void TextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        _isChanged = true;
    }

    #endregion

    #region Save / Delete

    private async void Dialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            if (_isTaskMode)
                await SaveTaskAsync();
            else
                await SaveEventAsync();

            if (ResultEvent == null) args.Cancel = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UnifiedItemDialog] 저장 오류: {ex.Message}");
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void Dialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            if (!_isNew)
            {
                using var service = Scheduler.CreateService();
                if (_isTaskMode)
                    await service.DeleteEventAsync(_taskEvent.No);
                else
                    await service.DeleteEventAsync(_event.No);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UnifiedItemDialog] 삭제 오류: {ex.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task SaveTaskAsync()
    {
        // 제목에서 직접 가져오기
        _taskEvent.Title = TxtTaskTitle.Text.Trim();
        _taskEvent.Notes = TxtTaskNotes.Text;
        _taskEvent.IsDone = ChkTaskDone.IsChecked == true;
        _taskEvent.ItemType = "task";
        _taskEvent.Updated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        if (string.IsNullOrWhiteSpace(_taskEvent.Title))
        {
            await MessageBox.ShowAsync("제목을 입력해주세요.");
            throw new InvalidOperationException("제목이 비어있습니다.");
        }

        _taskEvent.Start = DateTime.SpecifyKind(_taskEvent.Start, DateTimeKind.Unspecified);
        _taskEvent.End   = DateTime.SpecifyKind(_taskEvent.End,   DateTimeKind.Unspecified);
        if (_taskEvent.IsDone) _taskEvent.Completed = _taskEvent.Updated;
        else _taskEvent.Completed = string.Empty;

        var tasks = GenerateRepeatTasks();
        using var service = Scheduler.CreateService();

        if (tasks.Count <= 1)
        {
            if (_taskEvent.No <= 0) _taskEvent.No = await service.CreateTaskAsync(_taskEvent);
            else await service.UpdateTaskAsync(_taskEvent);
            ResultEvent = _taskEvent;
        }
        else
        {
            using var uow = Scheduler.CreateUnitOfWork();
            await uow.ExecuteInTransactionAsync(async () =>
            {
                foreach (var t in tasks)
                {
                    t.Start = DateTime.SpecifyKind(t.Start, DateTimeKind.Unspecified);
                    t.End   = DateTime.SpecifyKind(t.End,   DateTimeKind.Unspecified);
                    t.No = await uow.KEvents.CreateAsync(t);
                }
            });
            ResultEvent = tasks.First();
        }
    }

    private async Task SaveEventAsync()
    {
        _event.Title    = TxtEventTitle.Text.Trim();
        _event.Notes    = TxtEventNotes.Text;
        _event.Location = TxtEventLocation.Text.Trim();
        _event.Updated  = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        _event.User     = Environment.UserName;

        if (string.IsNullOrWhiteSpace(_event.Title))
        {
            await MessageBox.ShowAsync("제목을 입력해주세요.");
            throw new InvalidOperationException("제목이 비어있습니다.");
        }

        _event.Start = DateTime.SpecifyKind(_event.Start, DateTimeKind.Unspecified);
        _event.End   = DateTime.SpecifyKind(_event.End,   DateTimeKind.Unspecified);

        if (_event.End < _event.Start)
            _event.End = _event.Start.AddHours(1);

        using var service = Scheduler.CreateService();
        if (_event.No <= 0) _event.No = await service.CreateEventAsync(_event);
        else await service.UpdateEventAsync(_event);

        ResultEvent = _event;
    }

    #endregion

    #region Repeat Task Generation

    private List<KEvent> GenerateRepeatTasks()
    {
        var tasks = new List<KEvent>();

        if (RbNone?.IsChecked == true)
        {
            tasks.Add(_taskEvent);
            return tasks;
        }

        var endDate = DateTime.SpecifyKind(
            PickerEnd?.Date?.Date ?? _taskEvent.Start.Date.AddYears(1),
            DateTimeKind.Unspecified);

        var current = DateTime.SpecifyKind(_taskEvent.Start.Date, DateTimeKind.Unspecified);
        int count = 0;

        while (current <= endDate && count < 365)
        {
            var t = CloneTaskEvent(_taskEvent);
            t.Start = DateTime.SpecifyKind(current + _taskEvent.Start.TimeOfDay, DateTimeKind.Unspecified);
            t.End   = DateTime.SpecifyKind(current.AddDays(1), DateTimeKind.Unspecified);
            tasks.Add(t);
            count++;

            if      (RbDaily?.IsChecked   == true) current = current.AddDays(1);
            else if (RbWeekly?.IsChecked  == true) current = current.AddDays(7);
            else if (RbMonthly?.IsChecked == true) current = current.AddMonths(1);
            else if (RbYearly?.IsChecked  == true) current = current.AddYears(1);
            else break;
        }

        return tasks;
    }

    private static KEvent CloneTaskEvent(KEvent src) => new()
    {
        GoogleId = src.GoogleId, Title = src.Title, Notes = src.Notes,
        Start = src.Start, End = src.End, IsAllday = src.IsAllday,
        IsDone = src.IsDone, ItemType = "task",
        CalendarId = src.CalendarId, User = src.User,
        Updated = src.Updated, Completed = src.Completed,
        Status = "confirmed"
    };

    #endregion

    #region Color Helpers

    private void UpdateColorPreview(string colorId)
    {
        var hex = string.IsNullOrEmpty(colorId)
            ? GetCalendarColor()
            : KEvent.ColorIdToHex(colorId);
        UpdateColorPreviewHex(hex);
    }

    private void UpdateColorPreviewHex(string hex)
    {
        if (string.IsNullOrEmpty(hex)) hex = "#4285F4";
        try
        {
            hex = hex.TrimStart('#');
            byte r = Convert.ToByte(hex[0..2], 16);
            byte g = Convert.ToByte(hex[2..4], 16);
            byte b = Convert.ToByte(hex[4..6], 16);
            ColorPreview.Background = new SolidColorBrush(Color.FromArgb(255, r, g, b));
        }
        catch { ColorPreview.Background = new SolidColorBrush(Colors.Gray); }
    }

    private string GetCalendarColor()
    {
        if (CBoxCalendar.SelectedIndex >= 0 && CBoxCalendar.SelectedIndex < _calendars.Count)
            return _calendars[CBoxCalendar.SelectedIndex].Color;
        return "#4285F4";
    }

    private static int FindColorIndex(string colorId)
    {
        var ids = new[] { "", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11" };
        int idx = Array.IndexOf(ids, colorId ?? string.Empty);
        return idx >= 0 ? idx : 0;
    }

    #endregion

    #region Static Factories

    /// <summary>새 task용 KEvent 생성 (ItemType="task")</summary>
    private static KEvent NewTaskEvent(DateTime date) => new()
    {
        No = -1,
        ItemType = "task",
        Start = DateTime.SpecifyKind(date.Date.AddHours(DateTime.Now.Hour).AddMinutes(DateTime.Now.Minute), DateTimeKind.Unspecified),
        End   = DateTime.SpecifyKind(date.Date.AddDays(1), DateTimeKind.Unspecified),
        IsAllday = true,
        IsDone = false,
        Status = "confirmed",
        User = Environment.UserName,
        Updated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    };

    private static KEvent NewEvent(DateTime date) => new()
    {
        No = -1,
        Start = DateTime.SpecifyKind(date.Date.AddHours(9), DateTimeKind.Unspecified),
        End   = DateTime.SpecifyKind(date.Date.AddHours(10), DateTimeKind.Unspecified),
        IsAllday = false,
        Status = "confirmed",
        User = Environment.UserName,
        Updated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    };

    #endregion

    #region Helpers

    private static string KorDow(DayOfWeek d) => d switch
    {
        DayOfWeek.Sunday    => "일요일",
        DayOfWeek.Monday    => "월요일",
        DayOfWeek.Tuesday   => "화요일",
        DayOfWeek.Wednesday => "수요일",
        DayOfWeek.Thursday  => "목요일",
        DayOfWeek.Friday    => "금요일",
        DayOfWeek.Saturday  => "토요일",
        _ => string.Empty
    };

    #endregion
}
