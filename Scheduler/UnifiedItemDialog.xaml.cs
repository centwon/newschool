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
using NewSchool.Google;
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
    private bool _isInitialized = false;

    private List<KCalendarList> _calendars = [];
    private List<string> _titles = [];

    /// <summary>
    /// 이번 저장으로 만들어진 할 일 전부(반복 생성 포함).
    /// <see cref="ResultEvent"/> 는 대표 1건만 담으므로, 구글 Push 는 이 목록을 쓴다
    /// — 예전에는 첫 항목만 올라가고 나머지 반복분이 구글에 없었다.
    /// </summary>
    private readonly List<KEvent> _savedTasks = [];

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
            // 캘린더 목록 로드
            await LoadListsAsync();

            // 탭 설정
            RbTypeTask.IsChecked  = _isTaskMode;
            RbTypeEvent.IsChecked = !_isTaskMode;

            // 기존 항목은 종류를 바꿀 수 없다.
            //
            // 할 일과 일정은 서로 다른 KEvent 객체로 들고 있어서, 예전에는 탭을 옮기면
            // 제목·날짜가 사라져 보이고 그대로 저장하면 <b>같은 제목의 항목이 하나 더</b>
            // 생겼다(반대쪽 No 가 -1 이라 새로 만들어지고 원래 항목은 남았다).
            //
            // 신원을 넘겨 진짜 '변환' 으로 만들 수도 있지만, 반복 시리즈에 종류가 섞이고
            // 구글 동기화가 보내는 내용이 달라지는 등 가장자리가 늘어난다. 종류를 잘못 골랐다면
            // 지우고 다시 만드는 편이 싸다 — 그래서 <b>수정할 때는 아예 잠근다</b>.
            // (새 항목은 아직 저장 전이라 자유롭게 옮길 수 있고, 적은 내용은 따라간다.)
            RbTypeTask.IsEnabled  = _isNew;
            RbTypeEvent.IsEnabled = _isNew;
            if (!_isNew)
            {
                const string why = "이미 만든 항목의 종류는 바꿀 수 없습니다. 지우고 다시 만들어 주세요.";
                ToolTipService.SetToolTip(RbTypeTask, why);
                ToolTipService.SetToolTip(RbTypeEvent, why);
            }

            // 패널 표시
            UpdatePanelVisibility();

            // 값 채우기 — <b>두 서식을 모두</b> 채운다.
            //
            // 예전에는 지금 보이는 탭만 채웠다. 새 항목은 '할 일'로 열리므로 '일정' 서식은
            // 한 번도 채워지지 않았고, 탭을 옮겨도 RbType_Checked 는 패널만 바꿀 뿐이라
            // 날짜·시간 칸이 빈 채로 남았다. 그런데 메모리의 _event 는 값을 들고 있어서
            // 그대로 저장하면 보이지도 않던 날짜·시간이 등록됐다.
            FillTaskForm();
            FillEventForm();

            // 신규가 아니면 삭제 버튼 표시
            SecondaryButtonText = _isNew ? string.Empty : "삭제";
            Title = _isNew
                ? (_isTaskMode ? "새 할 일" : "새 일정")
                : (_isTaskMode ? "할 일 수정" : "일정 수정");

            // 구글 캘린더 동기화 체크박스 표시 여부
            UpdateGoogleSyncCheckboxVisibility();

            _isInitialized = true;

            // _isInitialized 후 반복 라벨 갱신 (선택 날짜 반영)
            if (_isTaskMode) UpdateRepeatLabels();
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

            _titles = _calendars.Select(c => c.Title).ToList();

            // 할 일 캘린더
            CBoxTaskList.ItemsSource = _titles;
            var taskIdx = _calendars.FindIndex(c => c.No == _taskEvent.CalendarId);
            CBoxTaskList.SelectedIndex = taskIdx >= 0 ? taskIdx : 0;

            // 캘린더 목록
            CBoxCalendar.ItemsSource = _titles;
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

        // 기존 반복 시리즈 항목이면 삭제 범위 선택(이 항목만 / 이후 모두)을 표시
        bool isSeriesMember = !_isNew && !string.IsNullOrEmpty(_taskEvent.SeriesId);
        PanelSeriesDelete.Visibility = isSeriesMember ? Visibility.Visible : Visibility.Collapsed;
        RbDeleteThisOnly.IsChecked = true;

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

        bool toTask = RbTypeTask.IsChecked == true;
        if (toTask == _isTaskMode) return;   // 같은 탭을 다시 고른 경우

        CarryOverToOtherType(toTask);

        _isTaskMode = toTask;
        UpdatePanelVisibility();

        // 넘겨받은 값으로 새 탭의 서식을 다시 그린다
        if (toTask) FillTaskForm();
        else FillEventForm();

        Title = _isNew
            ? (_isTaskMode ? "새 할 일" : "새 일정")
            : (_isTaskMode ? "할 일 수정" : "일정 수정");
    }

    /// <summary>
    /// 종류(할 일 ↔ 일정)를 바꿀 때 <b>지금까지 적은 것을 반대쪽으로 넘긴다.</b>
    ///
    /// <para>할 일과 일정은 서로 다른 <see cref="KEvent"/> 객체로 들고 있어서, 탭을 옮기면
    /// 제목·메모·날짜가 사라져 보였다. 새 항목을 쓰다 "이건 할 일이 아니라 일정이네" 하고
    /// 옮기는 것은 흔한 일이라, 적은 내용은 따라가야 한다.</para>
    ///
    /// <para>⚠ <b>새 항목에서만 일어난다.</b> 기존 항목은 종류 전환 자체를 잠갔다
    /// (<see cref="OnLoaded"/> 참고) — 신원까지 넘겨 진짜 변환으로 만들 수도 있지만
    /// 반복 시리즈·구글 동기화 쪽 가장자리가 늘어난다. 그래서 <c>No</c>·<c>GoogleId</c> 같은
    /// 신원은 여기서 넘기지 않는다.</para>
    /// </summary>
    /// <param name="toTask">옮겨 갈 쪽이 할 일이면 true.</param>
    private void CarryOverToOtherType(bool toTask)
    {
        var from = toTask ? _event : _taskEvent;   // 지금까지 보던 쪽
        var to   = toTask ? _taskEvent : _event;

        // 제목·메모는 저장할 때 읽으므로 아직 모델에 없다 — 화면에서 먼저 걷는다.
        if (toTask)
        {
            from.Title = TxtEventTitle.Text.Trim();
            from.Notes = TxtEventNotes.Text;
        }
        else
        {
            from.Title = TxtTaskTitle.Text.Trim();
            from.Notes = TxtTaskNotes.Text;
        }

        // 고른 캘린더는 두 탭이 각자 콤보를 갖고 있어 함께 넘긴다
        to.CalendarId = from.CalendarId;

        // 사람이 적은 값
        to.Title    = from.Title;
        to.Notes    = from.Notes;
        to.Start    = from.Start;
        to.IsAllday = from.IsAllday;
        to.End      = from.End;

        if (toTask)
        {
            NormalizeTaskEnd();   // 할 일의 End 는 Start 를 따른다
        }
        else if (to.End <= to.Start)
        {
            // 할 일은 End 가 Start 와 같다 — 일정으로 오면 길이가 0 이 되므로 한 시간을 준다
            to.End = to.Start.AddHours(1);
        }

        // 캘린더 콤보도 넘겨받은 값으로 맞춘다(두 탭이 각자 콤보를 갖고 있다)
        var idx = _calendars.FindIndex(c => c.No == to.CalendarId);
        if (idx >= 0)
        {
            if (toTask) CBoxTaskList.SelectedIndex = idx;
            else CBoxCalendar.SelectedIndex = idx;
        }
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
        {
            _taskEvent.CalendarId = _calendars[CBoxTaskList.SelectedIndex].No;
            if (Settings.UseGoogle.Value)
                UpdateSyncCheckbox(ChkTaskGoogleSync, CBoxTaskList.SelectedIndex);
        }
    }

    private async void CBoxTaskList_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        args.Handled = true;
        var text = args.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return;

        var idx = await FindOrCreateCalendarAsync(text);
        if (idx >= 0)
        {
            RefreshComboBoxSources();
            sender.SelectedIndex = idx;
        }
    }

    private void TaskDueDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (!_isInitialized || !args.NewDate.HasValue) return;
        var newDate = args.NewDate.Value.Date;
        _taskEvent.Start = DateTime.SpecifyKind(newDate + _taskEvent.Start.TimeOfDay, DateTimeKind.Unspecified);
        NormalizeTaskEnd();

        UpdateRepeatLabels();
    }

    private void TaskDueTimePicker_TimeChanged(object sender, TimeSpan e)
    {
        if (!_isInitialized) return;
        _taskEvent.Start = DateTime.SpecifyKind(_taskEvent.Start.Date + e, DateTimeKind.Unspecified);
        NormalizeTaskEnd();

        UpdateRepeatLabels();
    }

    private void ChkTaskAllday_Click(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        _taskEvent.IsAllday = ChkTaskAllday.IsChecked == true;
        TaskDueTimePicker.Visibility = _taskEvent.IsAllday ? Visibility.Collapsed : Visibility.Visible;
        NormalizeTaskEnd();

        UpdateRepeatLabels();
    }

    private void RepeatOption_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        bool showEnd = sender is RadioButton rb && rb != RbNone;
        LbRepeatEnd.Visibility = showEnd ? Visibility.Visible : Visibility.Collapsed;
        PickerEnd.Visibility   = showEnd ? Visibility.Visible : Visibility.Collapsed;

    }

    /// <summary>
    /// task의 End가 항상 Start 이상이 되도록 맞춘다.
    /// 종일: End = Start.Date. 시간 지정: End = Start(마감 시각과 동일한 시점).
    /// </summary>
    private void NormalizeTaskEnd()
    {
        _taskEvent.End = _taskEvent.IsAllday
            ? DateTime.SpecifyKind(_taskEvent.Start.Date, DateTimeKind.Unspecified)
            : _taskEvent.Start;
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
            if (Settings.UseGoogle.Value)
                UpdateSyncCheckbox(ChkEventGoogleSync, CBoxCalendar.SelectedIndex);
        }
    }

    private async void CBoxCalendar_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        args.Handled = true;
        var text = args.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return;

        var idx = await FindOrCreateCalendarAsync(text);
        if (idx >= 0)
        {
            RefreshComboBoxSources();
            sender.SelectedIndex = idx;
        }
    }

    private void EventStartDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (!_isInitialized || !args.NewDate.HasValue) return;
        var newDate = args.NewDate.Value.Date;
        _event.Start = DateTime.SpecifyKind(newDate + _event.Start.TimeOfDay, DateTimeKind.Unspecified);
        // 종료일이 시작일보다 이르면 맞춤
        if (_event.End.Date < newDate)
            _event.End = DateTime.SpecifyKind(newDate + _event.End.TimeOfDay, DateTimeKind.Unspecified);

    }

    private void EventStartTimePicker_TimeChanged(object sender, TimeSpan e)
    {
        if (!_isInitialized) return;
        _event.Start = DateTime.SpecifyKind(_event.Start.Date + e, DateTimeKind.Unspecified);

    }

    private void EventEndDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (!_isInitialized || !args.NewDate.HasValue) return;
        _event.End = DateTime.SpecifyKind(args.NewDate.Value.Date + _event.End.TimeOfDay, DateTimeKind.Unspecified);

    }

    private void EventEndTimePicker_TimeChanged(object sender, TimeSpan e)
    {
        if (!_isInitialized) return;
        _event.End = DateTime.SpecifyKind(_event.End.Date + e, DateTimeKind.Unspecified);

    }

    private void ChkEventAllday_Click(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        _event.IsAllday = ChkEventAllday.IsChecked == true;
        EventStartTimePicker.Visibility = _event.IsAllday ? Visibility.Collapsed : Visibility.Visible;
        EventEndTimePicker.Visibility   = _event.IsAllday ? Visibility.Collapsed : Visibility.Visible;

    }

    private void CBoxColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || CBoxColor.SelectedItem is not ComboBoxItem item) return;
        _event.ColorId = item.Tag?.ToString() ?? string.Empty;
        UpdateColorPreview(_event.ColorId);

    }

    #endregion

    #region Common Events

    private void TextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;

    }

    #endregion

    #region Save / Delete

    private async void Dialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        HideError();   // 이전 시도의 안내가 남아 있지 않도록
        try
        {
            if (_isTaskMode)
                await SaveTaskAsync();
            else
                await SaveEventAsync();

            if (ResultEvent == null) args.Cancel = true;

            // 구글 캘린더 즉시 Push
            if (ResultEvent != null)
            {
                bool shouldSync = _isTaskMode
                    ? ChkTaskGoogleSync.IsChecked == true
                    : ChkEventGoogleSync.IsChecked == true;

                if (shouldSync)
                {
                    // 반복 할 일은 생성된 항목 전부를 올린다. 예전에는 ResultEvent(=첫 항목)만
                    // 넘겨서, 반복으로 만든 나머지가 구글에 올라가지 않았다.
                    var targets = _savedTasks.Count > 0
                        ? new List<KEvent>(_savedTasks)
                        : new List<KEvent> { ResultEvent };

                    // 구글 왕복을 여기서 기다리지 않는다. deferral 이 완료될 때까지 대화상자가
                    // 열린 채로 남으므로, 네트워크가 끝날 때까지 저장 버튼이 멈춘 것처럼 보였다
                    // (반복 항목 수만큼 순차 왕복 + 실패 시 2·4·8초 백오프까지 겹친다).
                    // 로컬 저장은 이미 끝났으니 창은 바로 닫고, 업로드는 뒤에서 진행한다.
                    _ = PushToGoogleInBackgroundAsync(targets);
                }
            }
        }
        catch (ValidationAbort)
        {
            // 입력 검증은 이미 자체 안내를 띄웠다 — 창만 유지한다
            args.Cancel = true;
        }
        catch (Exception ex)
        {
            // ⚠ 여기서 UserErrorReporter(=MessageBox=또 다른 ContentDialog)를 부르면 안 된다.
            //    WinUI 는 ContentDialog 를 한 번에 하나만 허용해서, 이 창이 열려 있는 동안에는
            //    표시가 막히고 게이트가 헛돌기만 한다 — 사용자에게는 아무것도 안 보였다
            //    (ShowError 주석 참고). 창 안 InfoBar 로 알린다.
            Debug.WriteLine($"[UnifiedItemDialog] 저장 오류: {ex}");
            NewSchool.Logging.Log.Error("UnifiedItemDialog", "일정 저장 실패", ex);
            args.Cancel = true;
            ShowError($"저장 중 오류가 발생했습니다.\n{ex.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void Dialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        HideError();   // 이전 시도의 안내가 남아 있지 않도록
        try
        {
            if (!_isNew)
            {
                using var service = Scheduler.CreateService();
                bool deleteSeries = _isTaskMode
                    && !string.IsNullOrEmpty(_taskEvent.SeriesId)
                    && RbDeleteSeries?.IsChecked == true;

                // 삭제 결과를 확인한다. 예전에는 반환값을 버리고 예외도 Debug 로만 남긴 뒤
                // 창을 그대로 닫아서, 지워지지 않았는데 지운 것처럼 보였다
                // (호출부가 목록을 다시 읽으면 항목이 되살아났다).
                bool deleted = deleteSeries
                    ? await service.DeleteSeriesFromAsync(_taskEvent.SeriesId, _taskEvent.Start.Date) > 0
                    : await service.DeleteEventAsync(_isTaskMode ? _taskEvent.No : _event.No);

                if (!deleted)
                {
                    args.Cancel = true;
                    ShowError("삭제되지 않았습니다. 이미 지워진 항목일 수 있습니다. 창을 닫았다가 다시 열어보세요.");
                }
            }
        }
        catch (Exception ex)
        {
            // 저장 쪽과 같은 이유로 InfoBar 를 쓴다(대화상자 안에서는 MessageBox 가 뜨지 않는다)
            Debug.WriteLine($"[UnifiedItemDialog] 삭제 오류: {ex}");
            NewSchool.Logging.Log.Error("UnifiedItemDialog", "일정 삭제 실패", ex);
            args.Cancel = true;
            ShowError($"삭제 중 오류가 발생했습니다.\n{ex.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>입력 검증 실패로 저장을 중단할 때 쓰는 신호 — 안내는 이미 띄운 상태다.</summary>
    private sealed class ValidationAbort : Exception;

    /// <summary>
    /// 안내를 대화상자 안 InfoBar 로 띄운다.
    ///
    /// <para>MessageBox 를 쓰지 않는 이유: WinUI 는 ContentDialog 를 한 번에 하나만 허용한다.
    /// 이 대화상자가 열려 있는 동안 MessageBox(= 또 다른 ContentDialog)를 부르면 표시가 막히고,
    /// 게이트의 재시도 루프가 250ms 간격으로 헛돌며 로그만 쌓인다 — 화면에는 아무것도 안 뜬다.</para>
    /// </summary>
    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }

    private void HideError() => ErrorInfoBar.IsOpen = false;

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
            ShowError("제목을 입력해주세요.");
            throw new ValidationAbort();
        }

        _taskEvent.Start = DateTime.SpecifyKind(_taskEvent.Start, DateTimeKind.Unspecified);
        _taskEvent.End   = DateTime.SpecifyKind(_taskEvent.End,   DateTimeKind.Unspecified);
        if (_taskEvent.IsDone) _taskEvent.Completed = _taskEvent.Updated;
        else _taskEvent.Completed = string.Empty;

        var tasks = GenerateRepeatTasks();
        using var service = Scheduler.CreateService();

        // 재시도(저장 실패 후 다시 누르기) 시 이전 목록이 남아 중복 Push 되지 않도록 비운다
        _savedTasks.Clear();

        Debug.WriteLine($"[SaveTaskAsync] IsAllday={_taskEvent.IsAllday}, Start={_taskEvent.Start:O}, End={_taskEvent.End:O}");

        if (tasks.Count <= 1)
        {
            if (_taskEvent.No <= 0)
            {
                _taskEvent.No = await service.CreateTaskAsync(_taskEvent);
            }
            // 반영 여부를 확인한다. 예전에는 결과를 버려서, 이미 지워진 할 일을 편집하면
            // 0행이 갱신됐는데도 저장된 척 창이 닫혔다(고친 내용이 그대로 사라졌다).
            else if (!await service.UpdateTaskAsync(_taskEvent))
            {
                ShowError("저장되지 않았습니다. 이미 지워진 할 일일 수 있습니다.");
                throw new ValidationAbort();
            }

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

            // 반복 생성분 전부를 구글 Push 대상으로 남긴다
            _savedTasks.AddRange(tasks);
        }
    }

    private async Task SaveEventAsync()
    {
        _event.Title    = TxtEventTitle.Text.Trim();
        _event.Notes    = TxtEventNotes.Text;
        _event.Location = TxtEventLocation.Text.Trim();
        _event.Updated  = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        // 작성자는 앱이 쓰는 한 벌을 따른다 — 예전에는 Windows 계정 이름(Environment.UserName)이
        // 들어가, 게시판(Settings.AuthorName)·구글 동기화(교사 ID)와 셋으로 갈렸다.
        _event.User     = Settings.AuthorName;

        if (string.IsNullOrWhiteSpace(_event.Title))
        {
            ShowError("제목을 입력해주세요.");
            throw new ValidationAbort();
        }

        _event.Start = DateTime.SpecifyKind(_event.Start, DateTimeKind.Unspecified);
        _event.End   = DateTime.SpecifyKind(_event.End,   DateTimeKind.Unspecified);

        if (_event.End < _event.Start)
            _event.End = _event.Start.AddHours(1);

        using var service = Scheduler.CreateService();
        if (_event.No <= 0)
        {
            _event.No = await service.CreateEventAsync(_event);
        }
        else if (!await service.UpdateEventAsync(_event))   // 0행 갱신을 저장 성공으로 보지 않는다
        {
            ShowError("저장되지 않았습니다. 이미 지워진 일정일 수 있습니다.");
            throw new ValidationAbort();
        }

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

        var endDate = PickerEnd?.Date?.Date ?? _taskEvent.Start.Date.AddYears(1);

        RepeatKind kind =
            RbDaily?.IsChecked   == true ? RepeatKind.Daily   :
            RbWeekly?.IsChecked  == true ? RepeatKind.Weekly  :
            RbMonthly?.IsChecked == true ? RepeatKind.Monthly :
            RbYearly?.IsChecked  == true ? RepeatKind.Yearly  :
            RepeatKind.None;

        if (kind == RepeatKind.None)
        {
            tasks.Add(_taskEvent);
            return tasks;
        }

        // 발생 날짜는 앵커(원본 시작일) 기준으로 계산 — 월말·윤년 드리프트 방지(RecurrenceHelper)
        var dates = RecurrenceHelper.GenerateDates(_taskEvent.Start.Date, endDate, kind, maxCount: 365);

        // 반복 생성된 항목들을 하나의 시리즈로 묶어 "이후 반복 항목 모두 삭제"를 가능하게 함
        var seriesId = Guid.NewGuid().ToString("N");

        foreach (var date in dates)
        {
            var t = CloneTaskEvent(_taskEvent);
            t.Start = DateTime.SpecifyKind(date + _taskEvent.Start.TimeOfDay, DateTimeKind.Unspecified);
            t.End   = t.IsAllday ? DateTime.SpecifyKind(date, DateTimeKind.Unspecified) : t.Start;
            t.SeriesId = seriesId;
            tasks.Add(t);
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
        SeriesId = src.SeriesId,
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
        End   = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified),
        IsAllday = true,
        IsDone = false,
        Status = "confirmed",
        User = Settings.AuthorName,
        Updated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    };

    /// <summary>
    /// 새 일정용 KEvent. <b>고른 날짜 + 지금 시각</b>에서 한 시간짜리로 연다
    /// (예전에는 어느 날을 눌러도 9시~10시 고정이었다). 초는 버린다 —
    /// 할 일 쪽 <see cref="NewTaskEvent"/> 과 같은 규칙이다.
    /// </summary>
    private static KEvent NewEvent(DateTime date)
    {
        var start = DateTime.SpecifyKind(
            date.Date.AddHours(DateTime.Now.Hour).AddMinutes(DateTime.Now.Minute),
            DateTimeKind.Unspecified);

        return new()
        {
            No = -1,
            Start = start,
            End = start.AddHours(1),
            IsAllday = false,
            Status = "confirmed",
            User = Settings.AuthorName,
            Updated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };
    }

    #endregion

    #region Helpers

    /// <summary>캘린더 목록에서 찾거나, 없으면 DB에 새로 생성하고 인덱스 반환</summary>
    private async Task<int> FindOrCreateCalendarAsync(string title)
    {
        var idx = _titles.IndexOf(title);
        if (idx >= 0) return idx;

        try
        {
            using var service = Scheduler.CreateService();
            await service.GetOrCreateCalendarIdAsync(title);
            // DB에서 전체 목록 다시 로드 (새 캘린더의 No 는 아래 목록에서 다시 찾는다)
            _calendars = await service.GetAllCalendarsAsync();
            _titles = _calendars.Select(c => c.Title).ToList();
            return _titles.IndexOf(title);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UnifiedItemDialog] 캘린더 생성 오류: {ex.Message}");
            return -1;
        }
    }

    /// <summary>두 ComboBox의 ItemsSource를 갱신</summary>
    private void RefreshComboBoxSources()
    {
        var taskIdx = CBoxTaskList.SelectedIndex;
        var calIdx = CBoxCalendar.SelectedIndex;

        CBoxTaskList.ItemsSource = null;
        CBoxTaskList.ItemsSource = _titles;
        CBoxCalendar.ItemsSource = null;
        CBoxCalendar.ItemsSource = _titles;

        // 인덱스 복원 (변경되지 않은 쪽)
        if (taskIdx >= 0 && taskIdx < _titles.Count)
            CBoxTaskList.SelectedIndex = taskIdx;
        if (calIdx >= 0 && calIdx < _titles.Count)
            CBoxCalendar.SelectedIndex = calIdx;
    }

    /// <summary>구글 캘린더 연동 활성화 시 체크박스 표시</summary>
    private void UpdateGoogleSyncCheckboxVisibility()
    {
        bool googleEnabled = Settings.UseGoogle.Value;

        if (googleEnabled)
        {
            // 할 일: 선택된 캘린더가 TwoWay 동기화인지 확인
            UpdateSyncCheckbox(ChkTaskGoogleSync, CBoxTaskList.SelectedIndex);
            UpdateSyncCheckbox(ChkEventGoogleSync, CBoxCalendar.SelectedIndex);
        }
        else
        {
            ChkTaskGoogleSync.Visibility = Visibility.Collapsed;
            ChkEventGoogleSync.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateSyncCheckbox(CheckBox chk, int calendarIndex)
    {
        if (calendarIndex >= 0 && calendarIndex < _calendars.Count)
        {
            var cal = _calendars[calendarIndex];
            // 구버전 이름 "담임" 을 빼던 조건은 지웠다(2026-08-31) — 그 이름의 캘린더는
            // 더 이상 없다(기본 넷은 수업·학급·업무·개인, CategoryNames 참고).
            bool isTwoWay = cal.SyncMode == "TwoWay"
                            && !string.IsNullOrEmpty(cal.GoogleId);
            chk.Visibility = isTwoWay ? Visibility.Visible : Visibility.Collapsed;
            chk.IsChecked = isTwoWay;
        }
        else
        {
            chk.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 대화상자를 닫은 뒤 배경에서 구글 캘린더에 올린다.
    ///
    /// 실패는 모달 대신 <see cref="MainWindow.ShowGlobalWarning"/> 로 알린다 —
    /// 이 시점엔 대화상자가 이미 닫혀 있고, 사용자가 다른 작업을 하고 있을 수 있다.
    /// 앱을 바로 닫아 업로드가 중단돼도 <c>GoogleId</c> 가 비어 있으므로
    /// 다음 자동 동기화(미동기화 항목 조회)가 집어 간다.
    /// </summary>
    private async Task PushToGoogleInBackgroundAsync(List<KEvent> targets)
    {
        try
        {
            var failures = new List<string>();
            foreach (var target in targets)
            {
                string? failure = await PushToGoogleAsync(target);
                if (failure != null) failures.Add(failure);
            }

            if (failures.Count > 0 && App.MainWindow is MainWindow main)
            {
                main.ShowGlobalWarning(
                    "구글 캘린더 등록 실패",
                    $"저장은 됐지만 구글에 올리지 못했습니다 — " +
                    string.Join(" / ", failures.Distinct().Take(2)) +
                    " (다음 자동 동기화에서 다시 시도합니다)");
            }
        }
        catch (Exception ex)
        {
            // 배경 작업이라 여기서 새어 나가면 관측되지 않는다
            Debug.WriteLine($"[UnifiedItemDialog] 구글 Push(배경) 실패: {ex}");
        }
    }

    /// <summary>
    /// 저장 후 구글 캘린더에 즉시 Push.
    /// </summary>
    /// <returns>성공하면 null, 실패하면 사용자에게 보여줄 이유.</returns>
    private async Task<string?> PushToGoogleAsync(KEvent ev)
    {
        try
        {
            var cal = _calendars.FirstOrDefault(c => c.No == ev.CalendarId);
            if (cal == null || string.IsNullOrEmpty(cal.GoogleId))
                return "선택한 캘린더가 구글 캘린더와 연결되어 있지 않습니다.";

            if (!GoogleAuthService.HasCredentials)
                return "구글 연동 기능이 비활성화되어 있습니다(인증 정보 없음).";

            var authService = new GoogleAuthService();
            var apiClient = new GoogleCalendarApiClient(authService);
            var gEvent = GoogleSyncService.ConvertToGoogleEvent(ev);

            if (string.IsNullOrEmpty(ev.GoogleId))
            {
                // 신규 → Insert
                var created = await apiClient.InsertEventAsync(cal.GoogleId, gEvent);

                // 구글이 오류 본문을 돌려주면 GoogleEvent 로 역직렬화돼도 Id 가 비어 있다.
                // 예전에는 이 경우를 그냥 넘겨서, 등록이 안 됐는데도 조용히 성공처럼 끝났다.
                if (created?.Id == null)
                    return "구글이 등록 결과를 돌려주지 않았습니다(응답에 이벤트 ID 없음).";

                ev.GoogleId = created.Id;
                ev.Updated = created.Updated ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                // 식별자 두 열만 되써 넣는다 — 배경 업로드 중에 사용자가 같은 항목을
                // 수정했을 수 있어, 전체 행을 쓰면 그 편집이 조용히 사라진다.
                using var service = Scheduler.CreateService();
                await service.UpdateGoogleSyncFieldsAsync(ev.No, ev.GoogleId, ev.Updated);
            }
            else
            {
                // 수정 → Update
                var updated = await apiClient.UpdateEventAsync(cal.GoogleId, ev.GoogleId, gEvent);
                if (updated == null)
                    return "구글이 수정 결과를 돌려주지 않았습니다.";

                ev.Updated = updated.Updated ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                using var service = Scheduler.CreateService();
                await service.UpdateGoogleSyncFieldsAsync(ev.No, ev.GoogleId, ev.Updated);
            }

            Debug.WriteLine($"[UnifiedItemDialog] 구글 Push 완료: {ev.Title}");
            return null;
        }
        catch (Exception ex)
        {
            // 로컬 저장은 유지하고 다음 배치 동기화에서 재시도되지만,
            // 사용자가 "구글에 등록" 을 직접 체크한 동작이므로 실패는 알려야 한다.
            Debug.WriteLine($"[UnifiedItemDialog] 구글 Push 실패: {ex}");
            return ex.Message;
        }
    }

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
