using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Services;

namespace NewSchool.Pages;

/// <summary>
/// 학교 업무 관리 페이지
/// 좌측: 할 일 목록
/// 우측: 업무 일지
/// </summary>
public sealed partial class PageSchoolWork : Page
{
    #region Fields

    private WorkLog? _currentWorkLog;
    private readonly ObservableCollection<TodoItem> _todos = new();
    private readonly ObservableCollection<WorkLog> _workLogs = new();

    private string TeacherId => Settings.User.Value;

    #endregion

    #region Constructor

    public PageSchoolWork()
    {
        this.InitializeComponent();
        InitializeControls();
    }

    #endregion

    #region Initialization

    private void InitializeControls()
    {
        // 업무 일지 카테고리
        CBoxWorkLogCategory.ItemsSource = Enum.GetValues<WorkLogCategory>().ToList();
        CBoxWorkLogCategory.SelectedIndex = 0;

        // 날짜 피커
        WorkLogDatePicker.Date = DateTimeOffset.Now;

        // ItemsSource 연결
        TodoList.ItemsSource = _todos;
        WorkLogListView.ItemsSource = _workLogs;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        CalView.SelectedDates.Add(DateTimeOffset.Now);
        await LoadTodosAsync();
        await LoadWorkLogsAsync(DateTime.Today);
    }

    #endregion

    #region Calendar

    private async void CalView_SelectedDatesChanged(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
    {
        if (args.AddedDates.Count > 0)
        {
            var date = args.AddedDates[0].Date;
            WorkLogDatePicker.Date = new DateTimeOffset(date);
            await LoadWorkLogsAsync(date);
        }
    }

    #endregion

    #region Todo Management

    private async Task LoadTodosAsync()
    {
        try
        {
            using var svc = new TodoService();
            List<TodoItem> items;

            if (TglShowCompleted?.IsOn == true)
                items = await svc.GetAllAsync(TeacherId);
            else
                items = await svc.GetActiveAsync(TeacherId);

            _todos.Clear();
            foreach (var t in items) _todos.Add(t);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PageSchoolWork] 할 일 로드 실패: {ex.Message}");
        }
    }

    private async void BtnAddTodo_Click(object sender, RoutedEventArgs e)
    {
        var newItem = new TodoItem { TeacherID = TeacherId };
        var saved = await ShowTodoDialogAsync(newItem, isNew: true);
        if (saved) await LoadTodosAsync();
    }

    private async void TodoItem_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TodoItem item)
        {
            var saved = await ShowTodoDialogAsync(item, isNew: false);
            if (saved) await LoadTodosAsync();
        }
    }

    private async void TodoCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is TodoItem item)
        {
            item.ToggleCompleted();
            try
            {
                using var svc = new TodoService();
                await svc.UpdateAsync(item);
                await LoadTodosAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PageSchoolWork] 할 일 상태 변경 실패: {ex.Message}");
            }
        }
    }

    private async void TglShowCompleted_Toggled(object sender, RoutedEventArgs e)
    {
        await LoadTodosAsync();
    }

    private async void BtnClearCompleted_Click(object sender, RoutedEventArgs e)
    {
        if (await MessageBox.ShowConfirmAsync(
            "완료된 할 일을 모두 삭제합니다. 계속할까요?",
            "완료 항목 정리", "삭제", "취소"))
        {
            using var svc = new TodoService();
            await svc.DeleteCompletedAsync(TeacherId);
            await LoadTodosAsync();
        }
    }

    private async Task<bool> ShowTodoDialogAsync(TodoItem item, bool isNew)
    {
        var titleBox = new TextBox { Text = item.Title, PlaceholderText = "할 일 제목" };
        var descBox = new TextBox { Text = item.Description, PlaceholderText = "상세 내용", AcceptsReturn = true, MinHeight = 60 };
        var priorityBox = new ComboBox
        {
            ItemsSource = Enum.GetValues<TodoPriority>().ToList(),
            SelectedItem = item.Priority,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Header = "우선순위"
        };
        var dueDatePicker = new CalendarDatePicker
        {
            Date = item.DueDate.HasValue ? new DateTimeOffset(item.DueDate.Value) : null,
            PlaceholderText = "마감일 (선택)",
            Header = "마감일"
        };
        var tagBox = new TextBox { Text = item.Tag, PlaceholderText = "태그 (쉼표 구분)" };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(titleBox);
        panel.Children.Add(priorityBox);
        panel.Children.Add(dueDatePicker);
        panel.Children.Add(tagBox);
        panel.Children.Add(descBox);

        var dlg = new ContentDialog
        {
            Title = isNew ? "할 일 추가" : "할 일 편집",
            Content = panel,
            PrimaryButtonText = "저장",
            SecondaryButtonText = isNew ? null : "삭제",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dlg.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            item.Title = titleBox.Text;
            item.Description = descBox.Text;
            item.Priority = priorityBox.SelectedItem is TodoPriority p ? p : TodoPriority.보통;
            item.DueDate = dueDatePicker.Date?.Date;
            item.Tag = tagBox.Text;

            using var svc = new TodoService();
            if (isNew) await svc.InsertAsync(item);
            else await svc.UpdateAsync(item);
            return true;
        }
        else if (result == ContentDialogResult.Secondary && !isNew)
        {
            using var svc = new TodoService();
            await svc.DeleteAsync(item.No);
            return true;
        }

        return false;
    }

    #endregion

    #region WorkLog Management

    private async Task LoadWorkLogsAsync(DateTime date)
    {
        try
        {
            using var svc = new WorkLogService();
            var logs = await svc.GetByDateAsync(TeacherId, date);
            _workLogs.Clear();
            foreach (var l in logs) _workLogs.Add(l);

            _currentWorkLog = null;
            ClearWorkLogEditor();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PageSchoolWork] 업무 일지 로드 실패: {ex.Message}");
        }
    }

    private async void WorkLogDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (args.NewDate.HasValue)
        {
            await LoadWorkLogsAsync(args.NewDate.Value.Date);
        }
    }

    private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var keyword = args.QueryText?.Trim();
        if (string.IsNullOrEmpty(keyword)) return;

        try
        {
            using var svc = new WorkLogService();
            var results = await svc.SearchAsync(TeacherId, keyword);
            _workLogs.Clear();
            foreach (var l in results) _workLogs.Add(l);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PageSchoolWork] 검색 실패: {ex.Message}");
        }
    }

    private void WorkLogListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorkLogListView.SelectedItem is WorkLog log)
        {
            _currentWorkLog = log;
            LoadWorkLogToEditor(log);
        }
    }

    private void BtnAddWorkLog_Click(object sender, RoutedEventArgs e)
    {
        _currentWorkLog = new WorkLog
        {
            TeacherID = TeacherId,
            Date = WorkLogDatePicker.Date?.Date ?? DateTime.Today
        };

        ClearWorkLogEditor();
        TxtWorkLogTitle.Focus(FocusState.Programmatic);
    }

    private async void BtnSaveWorkLog_Click(object sender, RoutedEventArgs e)
    {
        if (_currentWorkLog == null)
        {
            _currentWorkLog = new WorkLog
            {
                TeacherID = TeacherId,
                Date = WorkLogDatePicker.Date?.Date ?? DateTime.Today
            };
        }

        SaveEditorToWorkLog(_currentWorkLog);

        if (string.IsNullOrWhiteSpace(_currentWorkLog.Title) &&
            string.IsNullOrWhiteSpace(_currentWorkLog.Content))
        {
            await MessageBox.ShowAsync("제목 또는 내용을 입력해주세요.", "저장 실패");
            return;
        }

        try
        {
            using var svc = new WorkLogService();
            if (_currentWorkLog.No > 0)
                await svc.UpdateAsync(_currentWorkLog);
            else
                await svc.InsertAsync(_currentWorkLog);

            await LoadWorkLogsAsync(_currentWorkLog.Date);
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync(ex.Message, "저장 실패");
        }
    }

    private async void BtnDeleteWorkLog_Click(object sender, RoutedEventArgs e)
    {
        if (_currentWorkLog == null || _currentWorkLog.No <= 0) return;

        if (await MessageBox.ShowConfirmAsync(
            $"'{_currentWorkLog.Title}' 을(를) 삭제할까요?",
            "삭제 확인", "삭제", "취소"))
        {
            using var svc = new WorkLogService();
            await svc.DeleteAsync(_currentWorkLog.No);
            await LoadWorkLogsAsync(_currentWorkLog.Date);
        }
    }

    #endregion

    #region WorkLog Editor Helpers

    private void LoadWorkLogToEditor(WorkLog log)
    {
        TxtWorkLogTitle.Text = log.Title;
        TxtWorkLogContent.Text = log.Content;
        TxtWorkLogTag.Text = log.Tag;
        ChkWorkLogImportant.IsChecked = log.IsImportant;

        for (int i = 0; i < CBoxWorkLogCategory.Items.Count; i++)
        {
            if (CBoxWorkLogCategory.Items[i] is WorkLogCategory cat && cat == log.Category)
            {
                CBoxWorkLogCategory.SelectedIndex = i;
                break;
            }
        }
    }

    private void SaveEditorToWorkLog(WorkLog log)
    {
        log.Title = TxtWorkLogTitle.Text?.Trim() ?? string.Empty;
        log.Content = TxtWorkLogContent.Text?.Trim() ?? string.Empty;
        log.Tag = TxtWorkLogTag.Text?.Trim() ?? string.Empty;
        log.IsImportant = ChkWorkLogImportant.IsChecked == true;
        log.Category = CBoxWorkLogCategory.SelectedItem is WorkLogCategory cat
            ? cat : WorkLogCategory.일반;
    }

    private void ClearWorkLogEditor()
    {
        TxtWorkLogTitle.Text = string.Empty;
        TxtWorkLogContent.Text = string.Empty;
        TxtWorkLogTag.Text = string.Empty;
        ChkWorkLogImportant.IsChecked = false;
        CBoxWorkLogCategory.SelectedIndex = 0;
    }

    #endregion

    #region Helpers

    #endregion
}
