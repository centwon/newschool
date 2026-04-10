using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Services;
using NewSchool.ViewModels;

namespace NewSchool.Pages;

/// <summary>
/// 교사 시간표 페이지
/// 특정 교사의 주간 시간표 조회
/// </summary>
public sealed partial class TeacherTimetablePage : Page
{
    private bool _isInitialized = false;

    public TeacherTimetablePage()
    {
        this.InitializeComponent();
        this.Loaded += TeacherTimetablePage_Loaded;
    }

    private void TeacherTimetablePage_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeFilters();
        ShowEmptyState();
        _isInitialized = true;
    }

    /// <summary>
    /// 필터 초기화
    /// </summary>
    private async void InitializeFilters()
    {
        // 학년도 -course에 등록된 학년 최근 3개, 없으면,  현재 연도
        using var courseService = new CourseService();
        var years = await courseService.GetDistinctCourseYearsAsync(Settings.User.Value);
        if (!years.Contains(Settings.WorkYear.Value))
        {
            years.Insert(Settings.WorkYear.Value,0);
        }
        foreach (var year in years.OrderByDescending(y => y).Take(3))
        {
            var item = new ComboBoxItem { Content = $"{year}학년도", Tag = year };
            CBoxYear.Items.Add(item);
        }
        // 현재 학년도 선택
        if (Settings.WorkYear.Value > 0)
        {
            var currentYearItem = CBoxYear.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(item => (int)item.Tag == Settings.WorkYear.Value);
            if (currentYearItem != null)
            {
                CBoxYear.SelectedItem = currentYearItem;
            }
        }
        else
        {
            CBoxYear.SelectedIndex = 0;
        }

        // 현재 학기 선택
        if (Settings.WorkSemester.Value > 0)
        {
            CBoxSemester.SelectedIndex = Settings.WorkSemester.Value - 1;
        }
        else
        {
            CBoxSemester.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// 필터 변경 이벤트
    /// </summary>
    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        // 필터 변경 시 자동 조회는 하지 않음 (사용자가 조회 버튼을 누르도록)
    }

    /// <summary>
    /// 조회 버튼 클릭
    /// </summary>
    private async void OnLoadClick(object sender, RoutedEventArgs e)
    {
        // 유효성 검사
        if (CBoxYear.SelectedItem == null || CBoxSemester.SelectedItem == null)
        {
            await MessageBox.ShowAsync("학년도와 학기를 선택해주세요.", "오류");
            return;
        }

        // 로그인한 교사 ID 가져오기
        string teacherId = Settings.User.Value;
        if (string.IsNullOrEmpty(teacherId))
        {
            await MessageBox.ShowAsync("교사 정보를 찾을 수 없습니다.", "오류");
            return;
        }
        Debug.WriteLine($"{((ComboBoxItem)(CBoxSemester.SelectedItem)).Tag.ToString()}");
        // 선택된 값 가져오기
        int year = (int)((ComboBoxItem)CBoxYear.SelectedItem).Tag;
        int semester = (int)((ComboBoxItem)CBoxSemester.SelectedItem).Tag;

        // 시간표 로드
        await LoadTimetableAsync(teacherId, year, semester);
    }

    /// <summary>
    /// 시간표 로드
    /// </summary>
    private async System.Threading.Tasks.Task LoadTimetableAsync(string teacherId, int year, int semester)
    {
        try
        {
            ShowLoadingState();

            using var timetableService = new TimetableService(SchoolDatabase.DbPath);
            var viewModel = await timetableService.GetTeacherTimetableAsync(teacherId, year, semester);

            // 시간표 표시 (교사용: 과목 + 강의실)
            TimetableControl.DisplayMode = TimetableDisplayMode.Teacher;
            TimetableControl.DataContext = viewModel;
            ShowTimetableState();
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"시간표 조회 중 오류가 발생했습니다.\n{ex.Message}", "오류");
            ShowEmptyState();
        }
    }

    #region UI 상태 관리

    private void ShowLoadingState()
    {
        LoadingPanel.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = Visibility.Collapsed;
        TimetableCard.Visibility = Visibility.Collapsed;
    }

    private void ShowEmptyState()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Visible;
        TimetableCard.Visibility = Visibility.Collapsed;
    }

    private void ShowTimetableState()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Collapsed;
        TimetableCard.Visibility = Visibility.Visible;
    }

    #endregion

}
