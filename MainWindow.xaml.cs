using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NewSchool.Board.Pages;
using NewSchool.Pages;
using NewSchool.Scheduler;
using Windows.Media.Miracast;
using WinRT.Interop;

namespace NewSchool;

/// <summary>
/// MainWindow with NavigationView
/// </summary>
public sealed partial class MainWindow : Window
{
    private Microsoft.UI.Windowing.AppWindow? _appWindow;

    public MainWindow()
    {
        InitializeComponent();

        this.Title = $"{Settings.SchoolName} - {DateTime.Now:yyyy년 M월 d일 dddd}";

        // ✅ 창 크기 복원 (Settings에서 로드)
        InitializeWindowSize();

        // 초기 페이지 로드
        NavView.SelectedItem = NavView.MenuItems[0];
        WorkFrame.Navigate(typeof(TodayPage));
        SetAppIcon();
    }

private void SetAppIcon()
    {
        // 1. 현재 창의 HWND(윈도우 핸들) 가져오기
        IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // 2. HWND를 WindowId로 변환 (정확한 네임스페이스 명시)
        // Microsoft.UI.Win32Interop 대신 아래와 같이 시도하세요.
        Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);

        // 3. AppWindow 가져오기
        Microsoft.UI.Windowing.AppWindow appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

        // 4. 아이콘 파일 경로 설정
        string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "newschool.ico");

        if (System.IO.File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
        }
    }
    
    /// <summary>
    /// 창 크기 초기화 및 변경 이벤트 등록
    /// </summary>
    private void InitializeWindowSize()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

        // 저장된 크기로 복원
        int width = Settings.WindowWidth.Value;
        int height = Settings.WindowHeight.Value;
        _appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

        // 창 크기 변경 이벤트 등록
        _appWindow.Changed += AppWindow_Changed;
    }

    /// <summary>
    /// 창 크기 변경 시 저장
    /// </summary>
    private void AppWindow_Changed(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        // 크기 변경만 처리 (최대화/최소화 상태가 아닐 때)
        if (args.DidSizeChange && sender.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped)
        {
            var size = sender.Size;
            if (size.Width > 0 && size.Height > 0)
            {
                Settings.WindowWidth.Set(size.Width);
                Settings.WindowHeight.Set(size.Height);
            }
        }
    }

    /// <summary>
    /// NavigationView 아이템 선택 이벤트
    /// </summary>
    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem item)
        {
            string tag = item.Tag?.ToString() ?? "";

            // 태그에 따라 페이지 네비게이션
            switch (tag)
            {
                //홈
                case "Home":
                    WorkFrame.Navigate(typeof(TodayPage));
                    break;
                    //달력
                case "Calendar":
                    WorkFrame.Navigate(typeof(Kcalendar));
                    break;
                    //학급
                case "ClassDiary":
                    WorkFrame.Navigate(typeof(ClassDiaryPage));
                    break;
                case "StudentInfo":
                    WorkFrame.Navigate(typeof(PageStudentInfo));
                    break;
                case "StudentLog":
                    WorkFrame.Navigate(typeof(PageStudentLog));
                    break;
                case "StudentSpec":
                    WorkFrame.Navigate(typeof(StudentSpecPage));
                    break;

                case "Seats":
                    WorkFrame.Navigate(typeof(PageSeats));
                    break;
                case "ClassBoard":
                    // 학급 게시판
                    WorkFrame.Navigate(typeof(PostListPage), new PostListPageParameter
                    {
                        Category = "학급",
                        Title = "학급 게시판",
                        AllowCategoryChange = false,
                        ShowSubjectFilter = true
                    });
                    break;
                case "StudentInfoExport":
                    WorkFrame.Navigate(typeof(StudentInfoExportPage));
                    break;

                case "CourseManagement":
                    // 수업 관리 (신규)
                    WorkFrame.Navigate(typeof(CourseManagementPage));
                    break;
                case "AnnualLessonPlan":
                    // 연간수업계획
                    WorkFrame.Navigate(typeof(AnnualLessonPlanPage), new AnnualLessonPlanPageParameter
                    {
                        TeacherId = Settings.User.Value,
                        DbPath = Settings.SchoolDB.Value,
                        Year = Settings.WorkYear.Value,
                        Semester = Settings.WorkSemester.Value
                    });
                    break;
                case "ProgressMatrix":
                    // 진도 관리
                    WorkFrame.Navigate(typeof(ProgressMatrixPage));
                    break;
                case "LessonActivity":
                    // 수업 관리 (신규)
                    WorkFrame.Navigate(typeof(LessonActivityPage));
                    break;
                case "Timetable_Teacher":
                    // 교사 시간표
                    WorkFrame.Navigate(typeof(TeacherTimetablePage));
                    break;

                case "Timetable_ClassManagement":
                    // 학급 시간표 관리 (신규)
                    WorkFrame.Navigate(typeof(ClassTimetableManagementPage));
                    break;

                case "ClubActivity":
                    // 동아리 활동 기록
                    WorkFrame.Navigate(typeof(ClubActivityPage));
                    break;

                case "LessonBoard":
                    // 수업 게시판
                    WorkFrame.Navigate(typeof(PostListPage), new PostListPageParameter
                    {
                        Category = "수업",
                        Title = "수업 게시판",
                        AllowCategoryChange = false,
                        ShowSubjectFilter = true
                    });
                    break;
                case "ClubManagement":
                    // 동아리 관리
                    WorkFrame.Navigate(typeof(ClubManagementPage));
                    break;
                case "WorkBoard":
                    // 업무 게시판
                    WorkFrame.Navigate(typeof(PostListPage), new PostListPageParameter
                    {
                        Category = "업무",
                        Title = "업무 게시판",
                        AllowCategoryChange = false,
                        ShowSubjectFilter = true
                    });
                    break;
                    //archive
                case "Archive":
                    WorkFrame.Navigate(typeof(PostListPage), new PostListPageParameter
                    {
                        Title = "아카이브",
                        AllowCategoryChange = true,
                        ShowSubjectFilter = true
                    });
                    break;
                case "Settings_School":
                    // 학교 설정
                    WorkFrame.Navigate(typeof(SettingsPage));
                    break;
                case "Settings_SchoolSchedule":
                    // 학사일정 관리
                    WorkFrame.Navigate(typeof(SchoolScheduleManagementPage));
                    break;
                case "Settings_Student":
                    // 학생 관리
                    WorkFrame.Navigate(typeof(StudentManagementPage));
                    break;
                case "Settings_App":
                    // 앱 설정
                    WorkFrame.Navigate(typeof(AppSettingsPage));
                    break;
                case "Settings_Homeroom":
                    // 담임반 설정
                    WorkFrame.Navigate(typeof(HomeroomSettingsPage));
                    break;
                case "LessonHome":
                    WorkFrame.Navigate(typeof(LessonHomePage));
                    break;
                case "SchoolWork":
                    WorkFrame.Navigate(typeof(PageSchoolWork));
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// 네비게이션 실패 이벤트
    /// </summary>
    private void WorkFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Navigation failed: {e.Exception.Message}");
    }
}
