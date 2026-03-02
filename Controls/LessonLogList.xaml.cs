using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using NewSchool.Services;

namespace NewSchool.Controls;

/// <summary>
/// 수업 기록 리스트 컨트롤
/// 학급, 단원 표시 지원
/// </summary>
public sealed partial class LessonLogList : UserControl
{
    #region Fields

    private LessonLogService? _service;
    private string? _currentSubject;
    private string? _currentRoom;
    private int? _currentGrade;
    private int? _currentClass;
    private int? _courseNo;

    #endregion

    #region Properties

    public ObservableCollection<LessonLog> LessonLogs { get; } = new();
    public LessonLog? SelectedLog => LvLessonLogs.SelectedItem as LessonLog;

    /// <summary>과목 필터의 CourseNo (다이얼로그에서 단원 목록 표시용)</summary>
    public int? CourseNo
    {
        get => _courseNo;
        set => _courseNo = value;
    }

    #endregion

    #region Events

    public event EventHandler<LessonLog>? LessonSelected;
    public event EventHandler? AddRequested;

    #endregion

    public LessonLogList()
    {
        this.InitializeComponent();
        _service = new LessonLogService();
        LvLessonLogs.ItemsSource = LessonLogs;
    }

    #region Public Methods

    /// <summary>
    /// 과목/강의실 필터 설정 및 로드 (기존 호환)
    /// </summary>
    public async Task LoadAsync(string? subject = null, string? room = null)
    {
        _currentSubject = subject;
        _currentRoom = room;
        _currentGrade = null;
        _currentClass = null;
        await RefreshAsync();
    }

    /// <summary>
    /// 과목/학급 필터 설정 및 로드
    /// </summary>
    public async Task LoadByClassAsync(string? subject = null, int? grade = null, int? classNum = null)
    {
        _currentSubject = subject;
        _currentRoom = null;
        _currentGrade = grade;
        _currentClass = classNum;
        await RefreshAsync();
    }

    /// <summary>
    /// 새로고침
    /// </summary>
    public async Task RefreshAsync()
    {
        if (_service == null) return;

        try
        {
            ShowLoading(true);
            LessonLogs.Clear();

            System.Collections.Generic.List<LessonLog> logs;

            if (_currentGrade.HasValue || _currentClass.HasValue)
            {
                logs = await _service.GetBySubjectAndClassAsync(
                    _currentSubject, _currentGrade, _currentClass);
            }
            else
            {
                logs = await _service.GetBySubjectAndRoomAsync(
                    _currentSubject, _currentRoom);
            }

            foreach (var log in logs)
            {
                LessonLogs.Add(log);
            }

            UpdateEmptyState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LessonLogList] RefreshAsync 오류: {ex.Message}");
        }
        finally
        {
            ShowLoading(false);
        }
    }

    /// <summary>
    /// 특정 수업 기록 선택
    /// </summary>
    public void SelectLog(int no)
    {
        foreach (var log in LessonLogs)
        {
            if (log.No == no)
            {
                LvLessonLogs.SelectedItem = log;
                break;
            }
        }
    }

    #endregion

    #region Event Handlers

    private void LvLessonLogs_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LessonLog log)
        {
            LessonSelected?.Invoke(this, log);
        }
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        AddRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    #endregion

    #region Helper Methods

    private void ShowLoading(bool isLoading)
    {
        LoadingRing.IsActive = isLoading;
        LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        LvLessonLogs.Visibility = isLoading ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateEmptyState()
    {
        TxtEmpty.Visibility = LessonLogs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LvLessonLogs.Visibility = LessonLogs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtCount.Text = LessonLogs.Count > 0 ? $"({LessonLogs.Count}건)" : "";
    }

    #endregion
}

#region Converters

public partial class DateToShortConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTime date)
            return date.ToString("M/d(ddd)");
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public partial class PeriodToStringConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int period && period > 0)
            return $"{period}교시";
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

#endregion

#region Static Helpers for x:Bind

/// <summary>
/// XAML x:Bind에서 사용하는 정적 헬퍼
/// </summary>
public static class LessonLogListHelpers
{
    public static Visibility HasSectionVisibility(string sectionName)
    {
        return string.IsNullOrWhiteSpace(sectionName)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}

#endregion
