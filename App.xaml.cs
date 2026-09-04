using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using NewSchool.Controls;
using NewSchool.Google;
using NewSchool.Logging;
using NewSchool.Pages;
using SQLitePCL;

namespace NewSchool;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    public static Window? MainWindow;
    private static GoogleSyncService? _googleSyncService;

    public App()
    {
        Batteries_V2.Init();

        // QuestPDF 라이선스는 정적 전역이라 프로세스당 한 번만 정해 두면 된다.
        //
        // 예전에는 PDF 를 만드는 서비스마다 각자 설정했는데(6곳), 그중
        // StudentCardPrintService.GenerateClassInfoPdfFromDbAsync 하나가 빠져 있었다.
        // 다른 PDF 를 먼저 만들어 본 적이 있으면 값이 이미 남아 있어 넘어가지만,
        // 앱을 켜고 그 메뉴를 가장 먼저 누르면 QuestPDF 가 예외를 던진다.
        // 시작 시 한 번으로 옮겨 그 순서 의존을 없앤다.
        //
        // Community 자격 근거(LICENSE.md v3.0, 2026-07-06 시행): 개인이 만드는
        // 프로젝트이고 연 매출이 100만 달러에 못 미친다(1항). 사용자인 학교는
        // 앱을 쓸 뿐 QuestPDF API 를 직접 부르지 않으므로 7항(전이 의존)에 든다.
        // ⚠ 이 라이선스는 OSI 승인 오픈소스가 아니며 MIT 가 적용되지 않는다.
        // 자격을 잃으면 90일 안에 유료 라이선스를 사야 한다.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        InitializeComponent();

        UnhandledException += (sender, e) =>
        {
            Debug.WriteLine($"[App] ★ UnhandledException: {e.Exception.GetType().Name}");
            Debug.WriteLine($"[App] ★ Message: {e.Exception.Message}");
            Debug.WriteLine($"[App] ★ StackTrace: {e.Exception.StackTrace}");
            if (e.Exception.InnerException != null)
            {
                Debug.WriteLine($"[App] ★ InnerException: {e.Exception.InnerException.Message}");
                Debug.WriteLine($"[App] ★ InnerStackTrace: {e.Exception.InnerException.StackTrace}");
            }

            // 파일 로그에 기록 — 여기서 앱이 곧 종료될 수 있으므로 즉시 디스크로 내린다.
            // (Flush 가 없으면 백그라운드 라이터가 스케줄되기 전에 프로세스가 사라져
            //  정작 원인을 알려줄 로그가 유실된다.)
            FileLogger.Instance.Critical($"[App] UnhandledException: {e.Exception.GetType().Name}", e.Exception);
            FileLogger.Instance.Flush();

            // 사용자에게 알린 뒤 앱이 죽지 않도록 처리(e.Handled = true)
            // 기존에는 조용히 로그만 남겨 사용자가 원인을 알 수 없었다
            try
            {
                _ = Controls.UserErrorReporter.ReportAsync(
                    "앱 실행",
                    e.Exception,
                    "예상치 못한 오류");
                e.Handled = true;
            }
            catch (Exception reportEx)
            {
                Debug.WriteLine($"[App] 오류 알림 실패: {reportEx.Message}");
            }
        };

        // async void / fire-and-forget Task에서 터진 예외를 포착.
        // 이 이벤트는 파이널라이저 스레드에서 발생하므로 ContentDialog(UI 스레드 친화성)를
        // 직접 만들 수 없다 — DispatcherQueue 로 UI 스레드에 넘겨야 알림이 실제로 표시된다.
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            Debug.WriteLine($"[App] ★ UnobservedTaskException: {e.Exception.GetType().Name} - {e.Exception.Message}");
            FileLogger.Instance.Error("[App] UnobservedTaskException", e.Exception);
            e.SetObserved();

            var dispatcher = MainWindow?.DispatcherQueue;
            if (dispatcher == null) return; // 창 없음(시작/종료 중) — 로그로 충분

            var ex = e.Exception;
            dispatcher.TryEnqueue(() =>
                _ = Controls.UserErrorReporter.ReportAsync(
                    "백그라운드 작업",
                    ex,
                    "백그라운드 작업 오류"));
        };

        // AppDomain 치명적 예외 (최종 안전망 — 앱 종료 직전 로그 기록만)
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            FileLogger.Instance.Critical(
                $"[AppDomain] UnhandledException (IsTerminating={e.IsTerminating})",
                ex ?? new Exception("Unknown non-Exception object"));

            // 종료가 확정된 경로 — 반드시 디스크에 내린 뒤 빠져나간다.
            FileLogger.Instance.Flush();
        };

        // 정상 종료 경로에서도 큐 잔여분을 반드시 기록한다.
        AppDomain.CurrentDomain.ProcessExit += (sender, e) => FileLogger.Instance.Dispose();
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // 0-0. 이미 떠 있으면 그 창을 앞으로 보내고 조용히 끝낸다.
        //   DB 를 열기 전에 판단해야 한다 — 두 프로세스가 같은 설정 DB 를 캐시하면
        //   서로의 저장을 못 보고 나중에 쓴 쪽이 앞의 것을 덮는다.
        if (!Helpers.SingleInstance.TryAcquire(Settings.UserDataPath))
        {
            Debug.WriteLine("[App] 이미 실행 중 — 기존 창으로 넘기고 종료");

            // ⚠ Application.Exit() 가 아니라 Environment.Exit 다. 창을 아직 하나도 만들지
            //    않은 시점이라 Exit() 는 곧바로 끝나 준다는 보장이 없고, 안 끝나면 창도 없는
            //    프로세스가 그대로 남는다(사용자 눈에는 아무 일도 없는데 프로세스만 쌓인다).
            //    여기서 정리할 것은 없다 — DB 도 로그 파일도 아직 열지 않았다
            //    (ProcessExit 에 걸어 둔 로그 마무리는 이 경로에서도 그대로 돈다).
            Environment.Exit(0);
            return;
        }

        // 0. 최초 실행 여부 확인 (Settings.db가 없으면 최초 실행)
        bool isFirstRun = !File.Exists(Path.Combine(Settings.UserDataPath, "Settings.db"));

        // 0-1. Settings.db 무결성 점검 — 바로 아래 Settings.Initialize() 보다 먼저 봐야 한다.
        //   이 파일이 깨져 있으면 Initialize() 가 예외로 죽어 손상 안내 자체가 뜨지 못한다.
        //   Settings.db 경로는 실행 위치로만 정해지므로(설정값을 읽지 않는다) 초기화 전에도 알 수 있다.
        var corruptSettings = Helpers.DbIntegrity.FindCorrupt(new[]
        {
            Path.Combine(Settings.UserDataPath, "Settings.db"),
        });
        if (corruptSettings.Count > 0)
        {
            Debug.WriteLine($"[App] 설정 DB 손상 감지: {string.Join(", ", corruptSettings)}");
            FileLogger.Instance.Critical($"[App] 설정 DB 손상 감지: {string.Join(", ", corruptSettings)}");
            await HandleCorruptDatabasesAsync(corruptSettings);
            return;
        }

        // 1. Settings 초기화
        //   설정 DB 를 열지 못하면 그 뒤로 할 수 있는 일이 없다. 여기서 잡지 않으면
        //   async void 를 타고 전역 처리기로 가는데, 그 처리기가 가장 먼저 하는 일이
        //   FileLogger 접근이라 로그 폴더까지 막힌 상황에서는 거기서 또 터진다 —
        //   창도 메시지도 없이 프로세스만 사라진다. 이유를 말하고 끝내는 편이 낫다.
        try
        {
            Settings.Initialize();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] 설정 초기화 실패: {ex}");
            await ShowFatalStartupErrorAsync(
                "데이터 폴더를 쓸 수 없습니다",
                $"설정을 저장할 폴더를 준비하지 못했습니다.\n\n" +
                $"위치: {Settings.UserDataPath}\n\n" +
                $"{Helpers.FileErrorText.Explain(ex) ?? ex.Message}");
            return;
        }

        Debug.WriteLine("[App] Settings 초기화 완료");

        // 1-1. 저장된 로그 레벨 적용
        var logLevel = Settings.LogLevel.Value switch
        {
            "Debug" => Logging.LogLevel.Debug,
            "Info" => Logging.LogLevel.Info,
            "Warning" => Logging.LogLevel.Warning,
            "Error" => Logging.LogLevel.Error,
            _ => Logging.LogLevel.Info
        };
        FileLogger.Instance.SetMinimumLevel(logLevel);
        Debug.WriteLine($"[App] 로그 레벨: {logLevel}");

        // 2. 나머지 DB 무결성 점검 — 초기화(2-1)보다 먼저 해야 한다.
        //   손상된 DB 에 CREATE TABLE 을 걸면 InitAsync 가 그대로 예외를 던져,
        //   복구 안내를 띄우기도 전에 앱이 조용히 죽는다. 자동 백업(3-1)보다도 앞이라
        //   손상된 파일이 백업으로 덮이지도 않는다.
        var corrupt = Helpers.DbIntegrity.FindCorrupt(new[]
        {
            SchoolDatabase.DbPath,
            Path.Combine(Settings.UserDataPath, Settings.Board_DB.Value),
            Path.Combine(Settings.UserDataPath, Settings.SchedulerDB.Value),
        });
        if (corrupt.Count > 0)
        {
            Debug.WriteLine($"[App] DB 손상 감지: {string.Join(", ", corrupt)}");
            FileLogger.Instance.Critical($"[App] DB 손상 감지: {string.Join(", ", corrupt)}");
            await HandleCorruptDatabasesAsync(corrupt);
            return; // 복원(재시작) 또는 종료 — 정상 시작 흐름 진입 안 함
        }

        // 2-1. DB 초기화 (독립적인 3개 DB를 병렬 초기화)
        await Task.WhenAll(
            NewSchool.Board.Board.InitAsync(),
            NewSchool.Scheduler.Scheduler.InitAsync(),
            NewSchool.SchoolDatabase.InitAsync()
        );
        Debug.WriteLine("[App] 데이터베이스 초기화 완료 (Board, Scheduler, School)");

        // 3-1. 자동 백업 (필요 시) — 백그라운드로 밀어 시작 시간 단축
        //   File.Copy 동기 작업으로 1~3초 블로킹될 수 있어 fire-and-forget 으로 처리.
        //   실패 시 FileLogger 및 전역 예외망이 포착하므로 별도 알림 생략.
        _ = Task.Run(() =>
        {
            try
            {
                var backupResult = Settings.RunAutoBackupIfNeeded();
                if (backupResult != null)
                {
                    Debug.WriteLine($"[App] 자동 백업 완료(백그라운드): {backupResult}");
                    FileLogger.Instance.Info($"[App] 자동 백업 완료: {backupResult}");
                }
                else if (Settings.AutoBackup.Value)
                {
                    // 자동 백업을 켜 뒀는데 결과가 없다 = 아직 주기가 안 됐거나 실패했다.
                    // 실패는 Settings.Backup 이 이미 Error 로 남긴다. 여기서는 "켜 놓았는데
                    // 이번 실행에는 백업이 없었다" 는 사실만 남겨, 로그만 보고도 백업이
                    // 실제로 돌고 있는지 판단할 수 있게 한다.
                    Debug.WriteLine("[App] 자동 백업 건너뜀(주기 미도래 또는 실패)");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] 자동 백업 실패(백그라운드): {ex.Message}");
                FileLogger.Instance.Error("[App] 자동 백업 실패", ex);
            }
        });

        // 4. 초기 설정 확인
        if (string.IsNullOrEmpty(Settings.SchoolCode.Value))
        {
            Debug.WriteLine("[App] 초기 설정이 필요합니다.");

            // ⭐ 초기 설정 창을 먼저 표시 (Window로 구현)
            var setupWindow = new InitialSetupWindow();

            setupWindow.Closed += (s, e) =>
            {
                if (setupWindow.IsCompleted)
                {
                    Debug.WriteLine("[App] 초기 설정 완료 - MainWindow 표시");
                    ShowMainWindow();
                }
                else
                {
                    Debug.WriteLine("[App] 초기 설정 취소 - 앱 종료");
                    Application.Current.Exit();
                }
            };

            setupWindow.Activate();
        }
        else
        {
            // 초기 설정이 이미 완료된 경우 바로 MainWindow 표시
            ShowMainWindow();
        }
    }

    /// <summary>
    /// 아직 창이 하나도 없을 때 치명적 실패를 알리고 끝낸다.
    ///
    /// <para><see cref="MessageBox"/> 는 XamlRoot 가 있어야 뜨므로, 손상 DB 안내와 같은
    /// 방식으로 임시 창을 하나 띄워 그 자리를 만든다. 이것이 없으면 시작이 막혔을 때
    /// 사용자가 볼 수 있는 것은 "아무 일도 일어나지 않음" 뿐이다.</para>
    /// </summary>
    private static async Task ShowFatalStartupErrorAsync(string title, string message)
    {
        try
        {
            var root = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = title,
                Margin = new Thickness(24),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            };
            var host = new Window { Content = root, Title = $"NewSchool — {title}" };
            host.Activate();

            if (root.XamlRoot is null)
            {
                var tcs = new TaskCompletionSource();
                root.Loaded += (_, _) => tcs.TrySetResult();
                await tcs.Task;
            }
            MessageBox.Initialize(root.XamlRoot!);
            MessageBox.TrackWindow(host);

            await MessageBox.ShowAsync(message, title);
        }
        catch (Exception ex)
        {
            // 안내조차 못 띄우는 상황 — 그래도 조용히 사라지지는 않게 남긴다.
            Debug.WriteLine($"[App] 시작 실패 안내 표시 실패: {ex.Message}");
        }
        finally
        {
            Application.Current.Exit();
        }
    }

    /// <summary>
    /// 시작 시 손상 DB 감지 → 백업 복원(성공 시 재시작) 또는 종료.
    /// </summary>
    private static async Task HandleCorruptDatabasesAsync(System.Collections.Generic.List<string> corruptFiles)
    {
        // ContentDialog·피커가 쓸 XamlRoot 확보용 호스트 창
        var root = new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = "데이터 파일 손상이 감지되었습니다.",
            Margin = new Thickness(24),
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
        };
        var host = new Window { Content = root, Title = "NewSchool — 데이터베이스 손상 감지" };
        host.Activate();

        // Content 가 시각 트리에 로드되어 XamlRoot 가 생길 때까지 대기
        if (root.XamlRoot is null)
        {
            var tcs = new TaskCompletionSource();
            root.Loaded += (_, _) => tcs.TrySetResult();
            await tcs.Task;
        }
        MessageBox.Initialize(root.XamlRoot!); // Loaded 이후 XamlRoot 보장
        MessageBox.TrackWindow(host);          // 이 창이 닫히면 XamlRoot 도 함께 놓는다

        bool restore = await MessageBox.ShowConfirmAsync(
            $"데이터 파일이 손상되었습니다: {string.Join(", ", corruptFiles)}\n\n" +
            $"백업(ZIP)을 선택해 복원하시겠습니까?\n자동 백업 위치: {Settings.BackupDirectory}\n\n" +
            "'종료'를 누르면 데이터를 건드리지 않고 앱을 닫습니다.",
            "데이터베이스 손상 감지", "백업에서 복원", "종료");

        if (restore && await TryRestoreFromPickerAsync(host))
        {
            // 옛 연결·캐시가 새 DB 에 섞이지 않도록 깨끗한 프로세스로 재시작
            Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
        }

        Application.Current.Exit();
    }

    /// <summary>백업 파일 선택 → Settings.Restore. AppSettingsPage 복원과 동일한 경로 규칙.</summary>
    private static async Task<bool> TryRestoreFromPickerAsync(Window owner)
    {
        try
        {
            // ZIP 단일 파일(신규)·backup_* 폴더 안의 .db(구버전) 모두 지원
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".zip");
            picker.FileTypeFilter.Add(".db");

            var file = await picker.PickSingleFileAsync();
            if (file is null) return false;

            string restorePath = file.Path;
            var parent = Path.GetDirectoryName(file.Path);
            if (file.Path.EndsWith(".db", StringComparison.OrdinalIgnoreCase) &&
                parent != null &&
                Path.GetFileName(parent).StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
            {
                restorePath = parent;
            }

            if (Settings.Restore(restorePath)) return true;

            await MessageBox.ShowAsync(
                "백업에서 복원하지 못했습니다. 올바른 백업(ZIP 또는 backup_* 폴더의 .db)인지 확인하세요.",
                "복원 실패");
            return false;
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync(ex.Message, "복원 오류");
            return false;
        }
    }

    /// <summary>
    /// MainWindow 생성 및 표시
    /// </summary>
    private void ShowMainWindow()
    {
        _window = new MainWindow();
        MainWindow = _window;
        MessageBox.Initialize(_window);
        _window.Closed += (s, e) =>
        {
            // 크기를 바꾸자마자 닫으면 디바운스 타이머가 깨어나지 못한다 — 남은 값을 지금 내린다.
            (s as MainWindow)?.FlushPendingWindowSize();

            _googleSyncService?.Dispose();
            _googleSyncService = null;

            // 창이 닫히면 프로세스가 곧 끝난다 — 큐에 남은 로그를 확정 기록.
            FileLogger.Instance.Dispose();
        };
        _window.Activate();

        // 뒤늦게 실행된 쪽이 보내는 "창 좀 띄워 달라" 신호를 받는다(창이 생긴 뒤여야 한다).
        Helpers.SingleInstance.ListenForShowRequests(() =>
            _window?.DispatcherQueue.TryEnqueue(() => BringToFront(_window)));

        Debug.WriteLine("[App] 앱 시작 완료");
        PrintSettings();

        // Google Calendar 시작 시 동기화 (비동기, fire-and-forget)
        _ = TryStartGoogleSyncAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
                Debug.WriteLine($"[App] Google sync failed: {t.Exception?.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// 앱 시작 시 Google Calendar 토큰 갱신 + 자동 동기화
    /// </summary>
    private static async Task TryStartGoogleSyncAsync()
    {
        try
        {
            if (!Settings.UseGoogle.Value) return;

            var authService = new GoogleAuthService();
            if (!authService.IsAuthenticated) return;

            // 토큰 갱신
            var token = await authService.GetValidAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                Debug.WriteLine("[App] Google 토큰 갱신 실패 — 동기화 건너뜀");
                return;
            }

            Debug.WriteLine("[App] Google 토큰 유효 — 동기화 시작");

            var apiClient = new GoogleCalendarApiClient(authService);
            _googleSyncService?.Dispose();
            _googleSyncService = new GoogleSyncService(authService, apiClient);
            _googleSyncService.SyncCompleted += OnBackgroundSyncCompleted;
            var result = await _googleSyncService.SyncAllAsync();

            Debug.WriteLine($"[App] Google 시작 동기화 완료: {result.Summary}");

            // 자동 동기화 활성화 시 주기적 동기화 시작
            if (Settings.GoogleAutoSync.Value)
            {
                int intervalMinutes = Settings.GoogleSyncIntervalMinutes.Value;
                if (intervalMinutes < 5) intervalMinutes = 15;
                _googleSyncService.StartPeriodicSync(TimeSpan.FromMinutes(intervalMinutes));
                Debug.WriteLine($"[App] Google 자동 동기화 시작: {intervalMinutes}분 간격");
            }
            else
            {
                _googleSyncService.Dispose();
                _googleSyncService = null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Google 동기화 시작 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 창을 앞으로 불러낸다 — 앱을 다시 실행했을 때 이미 떠 있는 창을 보여 주는 용도.
    /// 최소화되어 있으면 먼저 되살린다(그냥 Activate 만 하면 작업 표시줄에서 깜박이기만 한다).
    /// </summary>
    private static void BringToFront(Window window)
    {
        try
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

            if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter &&
                presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
            {
                presenter.Restore();
            }

            window.Activate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] 창 띄우기 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 일정 설정에서 구글 동기화 설정을 바꾼 뒤 호출 — <b>지금 세션에 바로 반영한다.</b>
    ///
    /// <para>예전에는 <see cref="TryStartGoogleSyncAsync"/> 가 앱 시작 때 한 번 읽는 게 전부였다.
    /// 그래서 자동 동기화를 켜도 그 세션에는 돌지 않았고, 꺼도 이미 도는 타이머는 멈추지 않았으며,
    /// 간격을 바꿔도 다음 실행까지 옛 간격으로 돌았다. 화면 어디에도 "재시작해야 한다" 는 말은 없었다.</para>
    /// </summary>
    internal static void ApplyGoogleSyncSettings()
    {
        // 연동 자체를 껐으면 자동 동기화도 없다(설정 화면도 연동이 꺼지면 이 칸들을 잠근다).
        if (!Settings.UseGoogle.Value || !Settings.GoogleAutoSync.Value)
        {
            _googleSyncService?.Dispose();   // 내부에서 StopPeriodicSync 까지 한다
            _googleSyncService = null;
            Debug.WriteLine("[App] Google 자동 동기화 중지(설정 변경)");
            return;
        }

        int intervalMinutes = Settings.GoogleSyncIntervalMinutes.Value;
        if (intervalMinutes < 5) intervalMinutes = 15;

        if (_googleSyncService != null)
        {
            // StartPeriodicSync 가 먼저 StopPeriodicSync 를 부르므로 간격 변경도 이 한 줄로 끝난다.
            _googleSyncService.StartPeriodicSync(TimeSpan.FromMinutes(intervalMinutes));
            Debug.WriteLine($"[App] Google 자동 동기화 재설정: {intervalMinutes}분 간격");
            return;
        }

        // 시작할 때는 꺼져 있어 서비스 자체가 없다 — 토큰 확인부터 다시 밟는다.
        _ = TryStartGoogleSyncAsync();
    }

    /// <summary>
    /// 백그라운드(시작 시·주기적) Google 동기화 결과 처리 — 실패 시 MainWindow InfoBar 로 알림.
    /// 수동 동기화(CalendarSettingsDialog)는 자체 UI 로 결과를 보여주므로 여기를 거치지 않는다.
    /// </summary>
    private static void OnBackgroundSyncCompleted(object? sender, SyncResult result)
    {
        if (result.Success) return;

        (MainWindow as MainWindow)?.ShowSyncFailure(
            NewSchool.MainWindow.SummarizeSyncErrors(result),
            RetryGoogleSyncAsync);
    }

    /// <summary>
    /// InfoBar '다시 시도' 용 — 기존 서비스가 살아 있으면 재사용, 없으면(자동 동기화 꺼짐 등) 새로 생성.
    /// 새로 만든 서비스는 이벤트를 구독하지 않으므로 결과는 호출자(InfoBar)가 직접 처리한다.
    /// </summary>
    private static async Task<SyncResult> RetryGoogleSyncAsync()
    {
        var existing = _googleSyncService;
        if (existing != null)
        {
            // 기존 서비스 경유 — SyncCompleted 이벤트도 함께 발생하지만
            // 실패 시 InfoBar 메시지를 갱신할 뿐이라 중복 표시는 없음
            return await existing.SyncAllAsync();
        }

        using var authService = new GoogleAuthService();
        var apiClient = new GoogleCalendarApiClient(authService);
        using var service = new GoogleSyncService(authService, apiClient);
        return await service.SyncAllAsync();
    }

    /// <summary>
    /// 설정 정보 로그 출력
    /// </summary>
    private static void PrintSettings()
    {
        Debug.WriteLine("========================================");
        Debug.WriteLine("[App] 현재 설정 정보:");
        Debug.WriteLine($"  - 데이터 경로: {Settings.UserDataPath}");
        Debug.WriteLine($"  - 포터블 모드: {Settings.IsPortableMode}");
        Debug.WriteLine($"  - School DB: {SchoolDatabase.DbPath}");
        Debug.WriteLine($"  - School DB 존재: {File.Exists(SchoolDatabase.DbPath)}");
        Debug.WriteLine($"  - 학교명: {Settings.SchoolName.Value}");
        Debug.WriteLine($"  - 학교코드: '{Settings.SchoolCode.Value}'");
        Debug.WriteLine($"  - 사용자: {Settings.UserName.Value} ({Settings.User.Value})");
        Debug.WriteLine($"  - 학년도/학기: {Settings.WorkYear.Value}년 {Settings.WorkSemester.Value}학기");
        Debug.WriteLine($"  - 담임반: {Settings.HomeGrade.Value}학년 {Settings.HomeRoom.Value}반");
        Debug.WriteLine("========================================");
    }

    // GetCurrentWindow 는 호출부가 없어 지웠다(39차) — 창이 필요한 곳은 App.MainWindow 를
    // 직접 쓰거나, 대화상자라면 MessageBox 가 활성 창을 찾아 준다.
}
