using System;
using System.Threading.Tasks;
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
using NewSchool.Services;

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
    private async void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem item)
        {
            string tag = item.Tag?.ToString() ?? "";

            // 메뉴 네비게이션 시 BackStack 정리 (메모리 절약)
            WorkFrame.BackStack.Clear();

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
                case "UnifiedExport":
                    WorkFrame.Navigate(typeof(UnifiedExportPage));
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
                case "LessonHome":
                    WorkFrame.Navigate(typeof(LessonHomePage));
                    break;
                case "SchoolWork":
                    WorkFrame.Navigate(typeof(PageSchoolWork));
                    break;
                case "Help":
                    WorkFrame.Navigate(typeof(HelpPage));
                    break;
                case "CheckUpdate":
                    await CheckForUpdateAsync();
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// 업데이트 확인 (ContentDialog로 결과 표시)
    /// </summary>
    private async Task CheckForUpdateAsync()
    {
        // 확인 중 다이얼로그
        var progressDialog = new ContentDialog
        {
            Title = "업데이트 확인",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new ProgressRing { IsActive = true, Width = 32, Height = 32 },
                    new TextBlock { Text = "업데이트를 확인하고 있습니다...", HorizontalAlignment = HorizontalAlignment.Center }
                }
            },
            XamlRoot = this.Content.XamlRoot
        };

        // 비동기로 업데이트 확인 시작
        var checkTask = UpdateService.CheckForUpdateAsync();

        // ProgressDialog를 잠깐 표시했다가 결과 나오면 닫기
        _ = progressDialog.ShowAsync();
        var result = await checkTask;
        progressDialog.Hide();

        // 결과 다이얼로그
        if (!result.IsSuccess)
        {
            var errorDialog = new ContentDialog
            {
                Title = "업데이트 확인 실패",
                Content = result.ErrorMessage,
                CloseButtonText = "확인",
                XamlRoot = this.Content.XamlRoot
            };
            await errorDialog.ShowAsync();
            return;
        }

        var info = result.Info!;
        if (info.IsUpdateAvailable)
        {
            var updateContent = new StackPanel { Spacing = 8 };
            updateContent.Children.Add(new TextBlock
            {
                Text = $"새 버전이 있습니다: v{info.LatestVersion.ToString(3)}",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            if (!string.IsNullOrEmpty(info.ReleaseName))
                updateContent.Children.Add(new TextBlock { Text = info.ReleaseName });

            if (!string.IsNullOrEmpty(info.ReleaseNotes))
                updateContent.Children.Add(new TextBlock
                {
                    Text = info.ReleaseNotes,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 400
                });

            var updateDialog = new ContentDialog
            {
                Title = "업데이트 가능",
                Content = updateContent,
                PrimaryButtonText = "다운로드",
                CloseButtonText = "나중에",
                XamlRoot = this.Content.XamlRoot
            };

            if (await updateDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                if (!string.IsNullOrEmpty(info.DownloadUrl))
                {
                    await Windows.System.Launcher.LaunchUriAsync(new Uri(info.DownloadUrl));
                }
            }
        }
        else
        {
            var upToDateDialog = new ContentDialog
            {
                Title = "업데이트 확인",
                Content = $"현재 최신 버전(v{UpdateService.CurrentVersion.ToString(3)})을 사용하고 있습니다.",
                CloseButtonText = "확인",
                XamlRoot = this.Content.XamlRoot
            };
            await upToDateDialog.ShowAsync();
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
