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

    public CourseManagementPage()
    {
        this.InitializeComponent();
    }

    private async void YearSemPicker_YearSemesterChanged(object? sender, YearSemesterChangedEventArgs e)
    {
        await ClassFilter.LoadAsync(e.Year, e.Semester);
    }

    /// <summary>
    /// 필터 변경 이벤트 — 학년도·학기·학년이 정해지면 목록을 다시 읽는다.
    ///
    /// ⚠ 예전에는 페이지의 Loaded 에서 세운 _isInitialized 플래그로 이 핸들러를 막았다.
    /// 그런데 WinUI 는 자식 컨트롤의 Loaded 를 페이지의 Loaded 보다 먼저 보내므로,
    /// YearSemesterPicker 자체 초기화 → ClassFilter.LoadAsync → ClassChanged 로 이어지는
    /// **최초 로드가 통째로 버려졌다.** 그래서 페이지에 처음 들어가면 목록도 빈 상태 메시지도
    /// 없이 비어 있었고, 필터를 한 번 건드려야 그제야 떴다.
    ///
    /// 준비 여부는 LoadCoursesAsync 가 학년도·학기 0 검사로 이미 막고 있으므로 플래그는 필요 없다.
    /// </summary>
    private void ClassFilter_ClassChanged(object? sender, ClassChangedEventArgs e)
    {
        LoadCoursesAsync();
    }

    /// <summary>
    /// 수업 목록 로드
    /// </summary>
    private async void LoadCoursesAsync()
    {
        // 유효성 검사
        if (YearSemPicker.Year == 0 || YearSemPicker.Semester == 0)
            return;

        try
        {
            ShowLoadingState();

            // FilterPicker에서 값 가져오기
            int year = YearSemPicker.Year;
            int semester = YearSemPicker.Semester;
            int grade = ClassFilter.Grade; // 0 = 전체

            string teacherId = Settings.User.Value;
            if (string.IsNullOrEmpty(teacherId))
            {
                await MessageBox.ShowAsync("교사 정보를 찾을 수 없습니다.", "오류");
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
            await MessageBox.ShowAsync($"수업 목록 조회 중 오류가 발생했습니다.\n{ex.Message}", "오류");
            ShowEmptyState();
        }
    }

    /// <summary>
    /// 수업 추가 버튼 클릭
    /// </summary>
    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (YearSemPicker.Year == 0 || YearSemPicker.Semester == 0)
        {
            await MessageBox.ShowAsync("학년도와 학기를 먼저 선택해주세요.", "알림");
            return;
        }

        int year = YearSemPicker.Year;
        int semester = YearSemPicker.Semester;
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
    /// <summary>단원 관리 페이지로 이동 (단원은 교과에 종속되므로 여기서 들어간다)</summary>
    private void OnSectionsClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not Course course) return;
        Frame.Navigate(typeof(CourseSectionPage), course);
    }

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
            await MessageBox.ShowAsync("시간표 배치가 저장되었습니다.", "완료");
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
                await MessageBox.ShowAsync("수업이 삭제되었습니다.", "완료");
                LoadCoursesAsync();
            }
            else
            {
                await MessageBox.ShowAsync("수업 삭제에 실패했습니다.", "오류");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"수업 삭제 중 오류가 발생했습니다.\n{ex.Message}", "오류");
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
