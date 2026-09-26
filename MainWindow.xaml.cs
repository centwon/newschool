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

    /// <summary>마지막으로 실제 이동한 메뉴 항목. 나가기를 취소했을 때 표시를 되돌린다(52차).</summary>
    private NavigationViewItem? _currentNavItem;

    public MainWindow()
    {
        InitializeComponent();

        // 보조 창이 닫힌 뒤 대화상자가 돌아올 자리. 등록해 두면 활성 창을 따라간다.
        Controls.MessageBox.TrackWindow(this);

        UpdateTitle();

        // 저장된 테마 복원 — 없으면 다크로 바꿔 두고 앱을 껐다 켰을 때 라이트로 돌아온다.
        Helpers.ThemeHelper.Apply(this);

        // ✅ 창 크기 복원 (Settings에서 로드)
        InitializeWindowSize();

        // 앱을 그냥 닫을 때도 메뉴로 나갈 때와 같은 것을 묻는다(52차) — 작성 중인 글이나
        // 바꿔 놓은 자리를 두고 X 를 누르면 예전에는 아무 말 없이 사라졌다.
        Controls.UnsavedWorkGuard.AskBeforeClosing(this, () => ConfirmCloseAsync());

        // 초기 페이지 로드
        NavView.SelectedItem = NavView.MenuItems[0];
        _currentNavItem = NavView.MenuItems[0] as NavigationViewItem;
        WorkFrame.Navigate(typeof(TodayPage));
        SetAppIcon();

        CheckWorkTerm();
        StartDayWatch();
    }

    #region 날짜가 바뀔 때

    /// <summary>마지막으로 확인한 "오늘" — 앱을 켜 둔 채 자정을 넘겼는지 가린다.</summary>
    private DateTime _knownToday = DateTime.Today;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _dayTimer;

    /// <summary>제목 표시줄의 학교 이름과 오늘 날짜.</summary>
    private void UpdateTitle()
        => this.Title = $"{Settings.SchoolName} - {DateTime.Today:yyyy년 M월 d일 dddd}";

    /// <summary>
    /// 1분마다 날짜가 바뀌었는지 본다. 예전에는 제목의 날짜를 창을 만들 때 한 번만 적어서,
    /// 켜 둔 채 밤을 넘기면 다음 날에도 전날 날짜가 제목 표시줄과 작업 표시줄에 남았다.
    /// 화면 안의 "오늘" 은 각 화면(오늘·수업 홈)이 스스로 다시 읽는다.
    /// </summary>
    private void StartDayWatch()
    {
        _dayTimer = DispatcherQueue.CreateTimer();
        _dayTimer.Interval = TimeSpan.FromMinutes(1);
        _dayTimer.Tick += (_, _) =>
        {
            if (_knownToday == DateTime.Today) return;

            _knownToday = DateTime.Today;
            UpdateTitle();
            CheckWorkTerm();
        };
        _dayTimer.Start();
        Closed += (_, _) => _dayTimer?.Stop();
    }

    /// <summary>
    /// 작업 학년도·학기가 오늘보다 뒤처졌으면 위쪽 알림으로 묻는다(<see cref="Helpers.WorkTerm"/>).
    /// 3월·9월이 지나도 설정은 그대로라, 모르고 쓰면 오늘 시간표가 지난 학기 것으로 뜨고
    /// 새 기록이 지난 학기로 저장된다.
    /// </summary>
    public void CheckWorkTerm()
    {
        var term = Helpers.WorkTerm.BehindToday(
            Settings.WorkYear.Value, Settings.WorkSemester.Value,
            Settings.WorkTermNoticeDismissed.Value, DateTime.Today);

        if (term is not { } t)
        {
            WorkTermInfoBar.IsOpen = false;
            return;
        }

        WorkTermInfoBar.Message =
            $"작업 학년도·학기가 {Settings.WorkYear.Value}학년도 {Settings.WorkSemester.Value}학기로 되어 있습니다. " +
            "이대로 쓰면 시간표·기록이 그 학기 기준으로 보이고 저장됩니다.";
        BtnWorkTermApply.Content = $"{t.Year}학년도 {t.Semester}학기로 바꾸기";
        BtnWorkTermApply.Tag = t;
        WorkTermInfoBar.IsOpen = true;
    }

    private void OnWorkTermApplyClick(object sender, RoutedEventArgs e)
    {
        if (BtnWorkTermApply.Tag is not ValueTuple<int, int> t) return;

        Settings.WorkYear.Set(t.Item1);
        Settings.WorkSemester.Set(t.Item2);
        WorkTermInfoBar.IsOpen = false;

        // 이미 떠 있는 화면은 옛 학기로 읽어 둔 것이다. 읽기만 하는 첫 화면들은 새로 연다 —
        // 편집 중일 수 있는 다른 화면은 건드리지 않는다(다시 열면 새 학기로 보인다).
        if (WorkFrame.Content is TodayPage or LessonHomePage)
        {
            WorkFrame.Navigate(WorkFrame.Content.GetType());
            WorkFrame.BackStack.Clear();
        }
    }

    private void OnWorkTermKeepClick(object sender, RoutedEventArgs e)
    {
        if (BtnWorkTermApply.Tag is ValueTuple<int, int> t)
            Settings.WorkTermNoticeDismissed.Set(Helpers.WorkTerm.KeyOf(t.Item1, t.Item2));

        WorkTermInfoBar.IsOpen = false;
    }

    #endregion

    /// <summary>
    /// 메뉴로 옮겨 갈 때 지난 화면 기록을 버린다 — 뒤로 가기 목록과 <b>페이지 캐시</b> 둘 다.
    ///
    /// <para>⚠ <c>BackStack.Clear()</c> 만으로는 캐시가 비지 않는다. 게시판 목록(<c>PostListPage</c>)은
    /// 글을 보고 [목록으로] 돌아올 때 보던 쪽·검색을 살리려고 <c>NavigationCacheMode.Enabled</c> 인데,
    /// 메뉴로 게시판에 들어갈 때마다 새 목록이 만들어지고 옛것은 프레임 캐시에 남았다 —
    /// <c>CacheSize</c>(10)도 넘겨 끝없이 쌓였다(게시판 14번 드나들어 19개 생존, 2026-09-25 실측).
    /// 글 보기에서 곧장 메뉴로 떠나면 남은 목록이 다음 진입 때 되살아나 옛 쪽·검색어를 보였다.</para>
    ///
    /// <para><c>CacheSize</c> 를 0 으로 내렸다 되돌리면 프레임이 캐시를 비운다. 게시판 안의
    /// 목록→글→목록 은 메뉴를 거치지 않으므로 그대로 캐시를 쓴다.</para>
    /// </summary>
    private void ForgetHistory()
    {
        WorkFrame.BackStack.Clear();
        int cacheSize = WorkFrame.CacheSize;
        WorkFrame.CacheSize = 0;
        WorkFrame.CacheSize = cacheSize;
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
        ForgetHistory();
        WorkFrame.Navigate(pageType);

        var target = FindNavItem(NavView.MenuItems, navTag);
        if (target != null)
        {
            NavView.SelectedItem = target;
            _currentNavItem = target;   // 되돌릴 자리도 함께 옮긴다(52차)
        }
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

    /// <summary>
    /// 이 항목을 품고 있는 <b>맨 위 메뉴 항목</b>(자기 자신일 수도 있다). 상단 막대의 밑줄이
    /// 그려지는 자리가 그것이라, 고르기를 되돌릴 때는 이쪽을 넣어야 표시가 돌아온다(52차).
    /// </summary>
    private NavigationViewItem? TopLevelOwnerOf(object? item)
    {
        if (item is not NavigationViewItem target) return null;

        foreach (var entry in NavView.MenuItems)
        {
            if (entry is not NavigationViewItem top) continue;
            if (ReferenceEquals(top, target) || Contains(top, target)) return top;
        }

        return null;

        static bool Contains(NavigationViewItem parent, NavigationViewItem target)
        {
            foreach (var child in parent.MenuItems)
            {
                if (child is not NavigationViewItem item) continue;
                if (ReferenceEquals(item, target) || Contains(item, target)) return true;
            }
            return false;
        }
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
    /// 앱을 닫아도 되는가 — 지금 화면이 저장하지 않은 편집을 들고 있으면 묻는다.
    /// 메뉴로 옮겨 갈 때와 <b>같은 판정</b>을 쓴다(<see cref="Controls.IUnsavedWork"/>).
    /// </summary>
    private async Task<bool> ConfirmCloseAsync()
    {
        // 열려 있는 보조 창(누가기록·수업 일지·메모 편집 …)부터 그 창의 확인을 거쳐 닫는다.
        // 그냥 두면 메인 창만 닫히고 보조 창이 혼자 남는다(UnsavedWorkGuard.CloseOthersAsync 주석).
        if (!await Controls.UnsavedWorkGuard.CloseOthersAsync(this))
            return false;

        // 누가기록 목록을 놓은 화면은 떠날 때 스스로 묻는데, 그게 창이 닫힌 뒤(Unloaded)라
        // 앱을 닫는 길에서는 물을 곳이 없다 — 창이 살아 있는 지금 먼저 묻게 한다.
        if (WorkFrame.Content is Controls.IAsksBeforeLeaving asks)
            await asks.AskBeforeLeavingAsync();

        return await Controls.UnsavedWorkGuard.ConfirmLeaveAsync(WorkFrame.Content);
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

            // 화면을 옮기지 않는 항목 — 나갈지 묻지도, 기록을 버리지도 않는다.
            // 예전에는 아래 절차를 다 탔다: 게시판 글을 보다가 하위 메뉴를 펴 보려고 [설정] 만 눌러도
            // 뒤로 가기 기록이 사라져, [목록으로] 가 보던 게시판이 아니라 모든 게시판이 섞인 목록을
            // 열었다. 묶음 항목(학급·수업·업무·설정 — Tag 없음)도 누르면 ItemInvoked 가 온다(실측).
            // ⚠ 이 항목들에 SelectsOnInvoked="False" 를 주면 안 된다 — 누를 때 지금 골라진 항목의
            //   ItemInvoked 가 한 번 더 와서 보던 화면이 새로 열리고, 하위 항목은 두 번 불리며
            //   펼친 메뉴도 닫히지 않았다(2026-09-25 실측).
            if (tag.Length == 0) return;
            if (tag is "Help" or "CheckUpdate")
            {
                // 고르기는 이미 이 항목으로 옮겨 가 있다 — 보던 화면의 메뉴로 되돌린다(아래 [계속 편집] 과 같은 방식).
                object? keep = TopLevelOwnerOf(_currentNavItem) ?? _currentNavItem;
                if (keep != null)
                    DispatcherQueue.TryEnqueue(() => NavView.SelectedItem = keep);

                if (tag == "Help") await OpenHelpInBrowserAsync();
                else await CheckForUpdateAsync();
                return;
            }

            // ⚠ 고치던 것을 두고 나가려는 것인지 먼저 묻는다(52차). 예전에는 작성 중인 글도
            //   바꿔 놓은 자리도 메뉴를 누르는 순간 아무 말 없이 사라졌다 — 그 화면의
            //   [취소] 버튼은 물어봤는데 메뉴로 나가는 길만 묻지 않았다.
            // ⚠ 되돌릴 자리를 <c>NavView.SelectedItem</c> 에서 읽으면 안 된다 — 이 이벤트가
            //   올 때 고르기는 <b>이미 새 항목으로 옮겨 가 있다</b>(문서와 달리 실측이 그랬다.
            //   그래서 되돌려도 방금 누른 항목이 그대로 남았다). 마지막으로 실제 이동한
            //   항목을 직접 들고 있는다.
            if (!await Controls.UnsavedWorkGuard.ConfirmLeaveAsync(WorkFrame.Content))
            {
                // ⚠ 큐에 넣어 되돌린다. 여기서 바로 대입하면 <b>표시가 그대로 넘어간 채</b>
                //   화면만 남는다 — NavigationView 가 이 핸들러(async void)가 첫 await 에서
                //   돌아간 뒤에 자기 고르기를 확정하므로, 그보다 먼저 넣은 값은 덮인다.
                //   실제로 [계속 편집] 을 골랐는데 메뉴만 [홈] 으로 옮겨 가 있었다(52차 실측).
                //   (SelectedItem 대입은 ItemInvoked 를 다시 일으키지 않는다 — NavigateTo 주석 참고.)
                //
                // ⚠ 되돌릴 때는 <b>맨 위 항목</b>으로 넣는다. 상단 막대의 밑줄은 하위 항목이
                //   골라져 있어도 그 부모 밑에 그려지는데, 하위 항목을 그대로 다시 넣으면
                //   (그 하위 메뉴가 접혀 있어서인지) 밑줄이 돌아오지 않는다 — 실측으로 확인했다.
                object? restore = TopLevelOwnerOf(_currentNavItem) ?? _currentNavItem;
                if (restore != null)
                    DispatcherQueue.TryEnqueue(() => NavView.SelectedItem = restore);
                return;
            }

            _currentNavItem = item;

            // 메뉴 네비게이션 시 뒤로 가기 기록·페이지 캐시 정리 (메모리 절약)
            ForgetHistory();

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
            // ⚠ 사용자가 [도움말] 을 누른 결과다. 예전에는 파일이 없어도, 브라우저가 뜨지
            //   않아도 조용히 끝나서 "눌러도 아무 일이 없다" 로만 보였다.
            //   LaunchFileAsync 는 실패를 예외가 아니라 false 로 낸다 — 그래서 더 잘 샌다.
            var helpPath = Path.Combine(AppContext.BaseDirectory, "Assets", "help.html");
            if (!File.Exists(helpPath))
            {
                NewSchool.Logging.Log.Error("Help", $"help.html 이 없다: {helpPath}");
                await MessageBox.ShowAsync(
                    "도움말 파일을 찾지 못했습니다.\n프로그램을 다시 설치하면 복구됩니다.", "도움말");
                return;
            }

            var file = await StorageFile.GetFileFromPathAsync(helpPath);
            if (!await Launcher.LaunchFileAsync(file))
            {
                NewSchool.Logging.Log.Error("Help", $"도움말을 열 프로그램이 응답하지 않았다: {helpPath}");
                await MessageBox.ShowAsync(
                    "도움말을 열지 못했습니다. 웹 브라우저에서 직접 열어 주세요.\n\n" + helpPath, "도움말");
            }
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Error("Help", "도움말 열기 실패", ex);
            await MessageBox.ShowAsync(
                "도움말을 열지 못했습니다.\n" +
                (Helpers.FileErrorText.Explain(ex) ?? ex.Message), "도움말");
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
