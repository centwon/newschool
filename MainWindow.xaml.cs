using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NewSchool.Board.Pages;
using NewSchool.Controls;
using NewSchool.Pages;
using NewSchool.Scheduler;
using NewSchool.Services;
using Windows.Media.Miracast;
using Windows.Storage;
using Windows.System;
using WinRT.Interop;

namespace NewSchool;

/// <summary>
/// MainWindow with NavigationView
/// </summary>
public sealed partial class MainWindow : Window
{
    private Microsoft.UI.Windowing.AppWindow? _appWindow;
    private Func<Task<Google.SyncResult>>? _infoBarRetryAction;

    public MainWindow()
    {
        InitializeComponent();

        // 보조 창이 닫힌 뒤 대화상자가 돌아올 자리. 등록해 두면 활성 창을 따라간다.
        Controls.MessageBox.TrackWindow(this);

        this.Title = $"{Settings.SchoolName} - {DateTime.Now:yyyy년 M월 d일 dddd}";

        // 저장된 테마 복원 — 없으면 다크로 바꿔 두고 앱을 껐다 켰을 때 라이트로 돌아온다.
        Helpers.ThemeHelper.Apply(this);

        // ✅ 창 크기 복원 (Settings에서 로드)
        InitializeWindowSize();

        // 초기 페이지 로드
        NavView.SelectedItem = NavView.MenuItems[0];
        WorkFrame.Navigate(typeof(TodayPage));
        SetAppIcon();
    }

    /// <summary>
    /// 페이지 안에서 다른 메뉴로 넘어갈 때 쓴다 — 화면과 상단 메뉴 표시를 함께 옮긴다.
    ///
    /// <para><c>Frame.Navigate</c> 만 부르면 화면은 바뀌는데 상단 메뉴는 원래 있던 항목을
    /// 계속 파랗게 물고 있어서, 지금 어디에 있는지가 어긋난다(빈 화면 안내판의
    /// [학생 추가하기] 처럼 페이지가 스스로 옮겨 가는 경우).</para>
    ///
    /// <para><c>SelectedItem</c> 을 넣는 것으로는 <c>ItemInvoked</c> 가 뜨지 않으므로
    /// 화면 이동은 여기서 직접 한다.</para>
    /// </summary>
    /// <param name="pageType">넘어갈 페이지</param>
    /// <param name="navTag">상단 메뉴에서 고를 항목의 Tag</param>
    public void NavigateTo(Type pageType, string navTag)
    {
        WorkFrame.BackStack.Clear();
        WorkFrame.Navigate(pageType);

        var target = FindNavItem(NavView.MenuItems, navTag);
        if (target != null)
            NavView.SelectedItem = target;
    }

    /// <summary>
    /// 페이지 쪽에서 부르는 <see cref="NavigateTo"/>. 메인 창을 찾으면 상단 메뉴 표시까지
    /// 옮기고, 못 찾으면 페이지가 놓인 Frame 만 옮긴다.
    /// </summary>
    public static void NavigateFromPage(Frame? frame, Type pageType, string navTag)
    {
        if (App.MainWindow is MainWindow main)
            main.NavigateTo(pageType, navTag);
        else
            frame?.Navigate(pageType);
    }

    /// <summary>Tag 로 메뉴 항목을 찾는다(하위 메뉴까지).</summary>
    private static NavigationViewItem? FindNavItem(System.Collections.Generic.IList<object> items, string tag)
    {
        foreach (var entry in items)
        {
            if (entry is not NavigationViewItem item) continue;

            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal))
                return item;

            var found = FindNavItem(item.MenuItems, tag);
            if (found != null)
                return found;
        }

        return null;
    }

    #region 전역 알림 InfoBar (백그라운드 동기화 실패 등)

    /// <summary>
    /// 백그라운드 Google 동기화 실패를 하단 InfoBar 로 알림. 어느 스레드에서 호출해도 안전.
    /// </summary>
    /// <param name="message">실패 요약 (첫 오류 + 건수)</param>
    /// <param name="retryAction">'다시 시도' 버튼 동작 — 새 동기화를 수행하고 결과를 반환</param>
    public void ShowSyncFailure(string message, Func<Task<Google.SyncResult>> retryAction)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ShowSyncFailure(message, retryAction));
            return;
        }

        _infoBarRetryAction = retryAction;
        GlobalInfoBar.Severity = InfoBarSeverity.Warning;
        GlobalInfoBar.Title = "Google 동기화 실패";
        GlobalInfoBar.Message = message;
        InfoBarRetryButton.Visibility = Visibility.Visible;
        InfoBarRetryButton.IsEnabled = true;
        GlobalInfoBar.IsOpen = true;
    }

    /// <summary>
    /// 재시도 버튼 없는 일반 경고를 전역 InfoBar 로 알린다(예: 홈 화면 일부 섹션 로드 실패).
    /// 어느 스레드에서 호출해도 안전.
    /// </summary>
    public void ShowGlobalWarning(string title, string message)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ShowGlobalWarning(title, message));
            return;
        }

        _infoBarRetryAction = null;
        GlobalInfoBar.Severity = InfoBarSeverity.Warning;
        GlobalInfoBar.Title = title;
        GlobalInfoBar.Message = message;
        InfoBarRetryButton.Visibility = Visibility.Collapsed;
        GlobalInfoBar.IsOpen = true;
    }

    private async void OnInfoBarRetryClicked(object sender, RoutedEventArgs e)
    {
        var retry = _infoBarRetryAction;
        if (retry == null) return;

        InfoBarRetryButton.IsEnabled = false;
        GlobalInfoBar.Message = "다시 동기화하는 중...";

        try
        {
            var result = await retry();
            if (result.Success)
            {
                // 성공: 잠깐 성공 표시 후 자동 닫기
                GlobalInfoBar.Severity = InfoBarSeverity.Success;
                GlobalInfoBar.Title = "Google 동기화";
                GlobalInfoBar.Message = result.Summary;
                InfoBarRetryButton.Visibility = Visibility.Collapsed;

                var timer = DispatcherQueue.CreateTimer();
                timer.Interval = TimeSpan.FromSeconds(3);
                timer.IsRepeating = false;
                timer.Tick += (_, _) => GlobalInfoBar.IsOpen = false;
                timer.Start();
            }
            else
            {
                GlobalInfoBar.Message = SummarizeSyncErrors(result);
                InfoBarRetryButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            GlobalInfoBar.Message = ex.Message;
            InfoBarRetryButton.IsEnabled = true;
        }
    }

    /// <summary>실패 요약 문자열 — 첫 오류 메시지 + 나머지 건수</summary>
    public static string SummarizeSyncErrors(Google.SyncResult result)
    {
        if (result.ErrorMessages.Count == 0) return "알 수 없는 오류가 발생했습니다.";
        var first = result.ErrorMessages[0];
        return result.ErrorMessages.Count == 1 ? first : $"{first} 외 {result.ErrorMessages.Count - 1}건";
    }

    #endregion

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

        // 저장된 "항상 위에" 설정 적용
        if (_appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = Settings.TopMost.Value;

        // 창 크기 변경 이벤트 등록
        _appWindow.Changed += AppWindow_Changed;
    }

    /// <summary>
    /// 지정한 창의 "항상 위에" 상태를 설정 (설정 페이지 토글에서 호출)
    /// </summary>
    public static void SetAlwaysOnTop(Window? window, bool onTop)
    {
        if (window == null) return;
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = onTop;
    }

    /// <summary>크기 변경이 멎은 뒤에 한 번만 저장하기 위한 타이머(디바운스).</summary>
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _saveWindowSizeTimer;

    /// <summary>타이머가 깨어났을 때 저장할 크기 — 마지막 이벤트의 값.</summary>
    private Windows.Graphics.SizeInt32 _pendingWindowSize;

    /// <summary>
    /// 창 크기 변경 시 저장.
    ///
    /// <para><b>최대화는 걸러야 한다.</b> 최대화한 창도 Presenter 는 여전히 Overlapped 라
    /// Kind 만 보면 통과한다 — 그래서 최대화한 채 끄면 다음 실행에 '화면만 한 창'이
    /// 최대화도 아닌 상태로 떴고, 더 작은 모니터로 옮기면 화면 밖으로 넘쳤다.
    /// 복원 크기로 되돌릴 자리는 <c>OverlappedPresenter.State</c> 가 알려 준다.</para>
    ///
    /// <para>저장은 <b>변경이 멎은 뒤 한 번</b>만 한다. 창을 한 번 끌면 이 이벤트가 수십 번 오는데,
    /// 그때마다 SQLite 연결을 새로 열어 UPSERT 하고 있었다.</para>
    /// </summary>
    private void AppWindow_Changed(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange) return;
        if (sender.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter) return;
        if (presenter.State != Microsoft.UI.Windowing.OverlappedPresenterState.Restored) return;

        var size = sender.Size;
        if (size.Width <= 0 || size.Height <= 0) return;

        _pendingWindowSize = size;

        if (_saveWindowSizeTimer == null)
        {
            _saveWindowSizeTimer = DispatcherQueue.CreateTimer();
            _saveWindowSizeTimer.Interval = System.TimeSpan.FromMilliseconds(500);
            _saveWindowSizeTimer.IsRepeating = false;
            _saveWindowSizeTimer.Tick += (_, _) =>
            {
                Settings.WindowWidth.Set(_pendingWindowSize.Width);
                Settings.WindowHeight.Set(_pendingWindowSize.Height);
            };
        }

        // 이미 돌고 있으면 처음부터 다시 — 끄는 손이 멈춘 뒤 500ms 에 한 번만 저장된다.
        _saveWindowSizeTimer.Stop();
        _saveWindowSizeTimer.Start();
    }

    /// <summary>
    /// 아직 타이머에 걸려 있는 창 크기를 지금 저장한다(창을 닫을 때 호출).
    /// 크기를 바꾸자마자 창을 닫으면 타이머가 깨어나기 전에 프로세스가 끝나기 때문이다.
    /// </summary>
    public void FlushPendingWindowSize()
    {
        if (_saveWindowSizeTimer is not { IsRunning: true }) return;

        _saveWindowSizeTimer.Stop();
        Settings.WindowWidth.Set(_pendingWindowSize.Width);
        Settings.WindowHeight.Set(_pendingWindowSize.Height);
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
                case "LessonActivity":
                    // 수업 관리 (신규)
                    WorkFrame.Navigate(typeof(LessonActivityPage));
                    break;
                case "CourseSpec":
                    // 학생부 기록 (교과세특, 과목/강의실 필터)
                    WorkFrame.Navigate(typeof(CourseSpecPage));
                    break;

                case "Timetable_ClassManagement":
                    // 학급 시간표 관리 (신규)
                    WorkFrame.Navigate(typeof(ClassTimetableManagementPage));
                    break;

                case "ClubActivity":
                    // 동아리 활동 기록
                    WorkFrame.Navigate(typeof(ClubActivityPage));
                    break;

                case "LessonJournal":
                    // 수업 일지 — 수업 카테고리 안의 전용 게시판.
                    // UseLessonJournalTemplate 을 켜면 새 글 버튼이 게시판 편집기로 넘어가지 않고
                    // 전용 창(LessonJournalWindow)을 연다 — 머리 정보·본문·첨부를 한 번에 받는다.
                    WorkFrame.Navigate(typeof(PostListPage), new PostListPageParameter
                    {
                        Category = "수업",
                        Subject = "수업일지",
                        AllowCategoryChange = false,
                        UseLessonJournalTemplate = true
                    });
                    break;
                case "LessonBoard":
                    // 수업 게시판
                    WorkFrame.Navigate(typeof(PostListPage), new PostListPageParameter
                    {
                        Category = "수업",
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
                        AllowCategoryChange = false,
                        ShowSubjectFilter = true
                    });
                    break;
                    //archive
                case "Archive":
                    WorkFrame.Navigate(typeof(PostListPage), new PostListPageParameter
                    {
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
                    await OpenHelpInBrowserAsync();
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
    /// <summary>
    /// 도움말(help.html)을 기본 웹 브라우저로 연다. 앱 내 WebView2 호스팅을 제거하여
    /// WebView2 런타임 의존을 없앴다 — help.html 은 Content 로 그대로 배포된다.
    /// </summary>
    private static async Task OpenHelpInBrowserAsync()
    {
        try
        {
            var helpPath = Path.Combine(AppContext.BaseDirectory, "Assets", "help.html");
            if (!File.Exists(helpPath))
            {
                System.Diagnostics.Debug.WriteLine($"[Help] help.html 없음: {helpPath}");
                return;
            }
            var file = await StorageFile.GetFileFromPathAsync(helpPath);
            await Launcher.LaunchFileAsync(file);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Help] 브라우저 열기 실패: {ex.Message}");
        }
    }

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

        // ProgressDialog를 잠깐 표시했다가 결과 나오면 닫는다.
        //
        // ⚠ MessageBox.ShowDialogAsync 를 거친다. 예전에는 ContentDialog.ShowAsync 를 직접
        //    `_ =` 로 버려서 (1) 다른 대화상자가 열려 있으면 나는 예외가 미관측 태스크 예외가 되고
        //    (2) 대화상자 직렬화 게이트를 우회해 바로 뒤의 결과 대화상자와 충돌할 수 있었다.
        //
        // ⚠ 표시가 늦어질 수 있다는 것도 함께 다뤄야 한다. ShowDialogAsync 는 게이트를 먼저
        //    기다리므로, 다른 대화상자가 열려 있으면 이 창은 아래 Hide() 보다 늦게 뜬다.
        //    그러면 Hide() 가 헛돌아 "업데이트를 확인하고 있습니다..." 가 화면에 남고, 그것이
        //    게이트를 문 채라 정작 결과 대화상자가 뜨지 못한다. 닫으라는 표시를 남겨 두고
        //    Opened 에서 곧바로 닫는다.
        bool progressDone = false;
        progressDialog.Opened += (s, _) => { if (progressDone) s.Hide(); };

        var showTask = MessageBox.ShowDialogAsync(progressDialog);

        var result = await checkTask;

        progressDone = true;
        progressDialog.Hide();

        // 게이트가 풀린 뒤에 결과 대화상자로 넘어간다(늦게 떴다 닫히는 경우까지 기다린다).
        await showTask;

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
            await MessageBox.ShowDialogAsync(errorDialog);
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

            if (await MessageBox.ShowDialogAsync(updateDialog) == ContentDialogResult.Primary)
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
            await MessageBox.ShowDialogAsync(upToDateDialog);
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
