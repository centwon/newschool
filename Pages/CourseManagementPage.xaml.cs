using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Dialogs;
using System.Threading.Tasks;

namespace NewSchool.Pages;

/// <summary>
/// 수업 관리 페이지
/// Course 목록 조회, 추가, 수정, 삭제
/// </summary>
public sealed partial class CourseManagementPage : Page
{
    private ObservableCollection<Course> _courses = new();
    private bool _isInitialized = false;

    public CourseManagementPage()
    {
        this.InitializeComponent();
        this.Loaded += CourseManagementPage_Loaded;
    }

    private void CourseManagementPage_Loaded(object sender, RoutedEventArgs e)
    {
        // SchoolFilterPicker가 자동으로 초기화함
        _isInitialized = true;
        LoadCoursesAsync();
    }

    /// <summary>
    /// 필터 변경 이벤트
    /// </summary>
    private void OnFilterChanged(object? sender, SchoolFilterChangedEventArgs e)
    {
        if (!_isInitialized) return;
        LoadCoursesAsync();
    }

    /// <summary>
    /// 수업 목록 로드
    /// </summary>
    private async void LoadCoursesAsync()
    {
        // 유효성 검사
        if (FilterPicker.SelectedYear == 0 || FilterPicker.SelectedSemester == 0)
            return;

        try
        {
            ShowLoadingState();

            // FilterPicker에서 값 가져오기
            int year = FilterPicker.SelectedYear;
            int semester = FilterPicker.SelectedSemester;
            int grade = FilterPicker.SelectedGrade; // 0 = 전체

            string teacherId = Settings.User.Value;
            if (string.IsNullOrEmpty(teacherId))
            {
                await MessageBox.ShowAsync("오류", "교사 정보를 찾을 수 없습니다.");
                ShowEmptyState();
                return;
            }

            // 수업 목록 조회
            using var repo = new CourseRepository(SchoolDatabase.DbPath);
            var courses = await repo.GetByTeacherAsync(teacherId, year, semester);

            // 학년 필터 적용
            if (grade > 0)
            {
                courses = courses.Where(c => c.Grade == grade).ToList();
            }

            _courses.Clear();
            foreach (var course in courses)
            {
                _courses.Add(course);
            }

            CourseListView.ItemsSource = _courses;

            // UI 업데이트
            UpdateUI();
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync("오류", $"수업 목록 조회 중 오류가 발생했습니다.\n{ex.Message}");
            ShowEmptyState();
        }
    }

    /// <summary>
    /// 수업 추가 버튼 클릭
    /// </summary>
    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (FilterPicker.SelectedYear == 0 || FilterPicker.SelectedSemester == 0)
        {
            await MessageBox.ShowAsync("알림", "학년도와 학기를 먼저 선택해주세요.");
            return;
        }

        int year = FilterPicker.SelectedYear;
        int semester = FilterPicker.SelectedSemester;
        string teacherId = Settings.User.Value;
        string schoolCode = Settings.SchoolCode.Value;

        var dialog = new CourseEditDialog(schoolCode, teacherId, year, semester);
        dialog.XamlRoot = this.XamlRoot;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            LoadCoursesAsync();
        }
    }

    /// <summary>
    /// 수업 수정 버튼 클릭
    /// </summary>
    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var course = button?.Tag as Course;
        if (course == null) return;

        var dialog = new CourseEditDialog(course);
        dialog.XamlRoot = this.XamlRoot;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            LoadCoursesAsync();
        }
    }
    ///<summary>
    ///수강 학생 관리 버튼 클릭
    /// </summary>
    private async void OnEnrollClick(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var course = button?.Tag as Course;
        if (course == null) return;
        var dialog = new CourseEnrollmentDialog(course)
        {
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            // 저장 완료 - 필요시 새로고침
        }
    }

    /// 
    /// <summary>
    /// 시간표 배치 버튼 클릭
    /// </summary>
    private async void OnScheduleClick(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var course = button?.Tag as Course;
        if (course == null) return;

        var dialog = new CourseScheduleDialog(course);
        dialog.XamlRoot = this.XamlRoot;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await MessageBox.ShowAsync("완료", "시간표 배치가 저장되었습니다.");
        }
    }

    /// <summary>
    /// 수업 삭제 버튼 클릭
    /// </summary>
    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var course = button?.Tag as Course;
        if (course == null) return;

        // 확인 다이얼로그
        var confirmed = await MessageBox.ShowConfirmAsync(
            $"'{course.Subject}' 수업을 삭제하시겠습니까?\n연결된 시간표 배치도 함께 삭제됩니다.",
            "수업 삭제", "삭제", "취소");
        if (!confirmed) return;

        try
        {
            using var repo = new CourseRepository(SchoolDatabase.DbPath);
            bool success = await repo.DeleteAsync(course.No);

            if (success)
            {
                await MessageBox.ShowAsync("완료", "수업이 삭제되었습니다.");
                LoadCoursesAsync();
            }
            else
            {
                await MessageBox.ShowAsync("오류", "수업 삭제에 실패했습니다.");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync("오류", $"수업 삭제 중 오류가 발생했습니다.\n{ex.Message}");
        }
    }

    #region UI 상태 관리

    private void UpdateUI()
    {
        bool hasCourses = _courses.Count > 0;

        LoadingState.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = hasCourses ? Visibility.Collapsed : Visibility.Visible;
        CourseListContainer.Visibility = hasCourses ? Visibility.Visible : Visibility.Collapsed;

        TxtCourseCount.Text = $"총 {_courses.Count}개 수업";
    }

    private void ShowLoadingState()
    {
        LoadingState.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        CourseListContainer.Visibility = Visibility.Collapsed;
    }

    private void ShowEmptyState()
    {
        LoadingState.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Visible;
        CourseListContainer.Visibility = Visibility.Collapsed;
    }


    #endregion

}
