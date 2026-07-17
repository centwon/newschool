using System;
using System.IO;
using Microsoft.UI.Xaml;
using SQLitePCL;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NewSchool.Logging;
using NewSchool.Pages;
using NewSchool.Controls;
using NewSchool.Google;

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

            // 파일 로그에 기록
            FileLogger.Instance.Critical($"[App] UnhandledException: {e.Exception.GetType().Name}", e.Exception);

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
        };
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // 0. 최초 실행 여부 확인 (Settings.db가 없으면 최초 실행)
        bool isFirstRun = !File.Exists(Path.Combine(Settings.UserDataPath, "Settings.db"));

        // 1. Settings 초기화
        Settings.Initialize();
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

        // 2. DB 초기화 (독립적인 3개 DB를 병렬 초기화)
        await Task.WhenAll(
            NewSchool.Board.Board.InitAsync(),
            NewSchool.Scheduler.Scheduler.InitAsync(),
            NewSchool.SchoolDatabase.InitAsync()
        );
        Debug.WriteLine("[App] 데이터베이스 초기화 완료 (Board, Scheduler, School)");

        // 2-1. DB 무결성 점검 — 손상 감지 시 조용한 크래시 대신 복원/종료 안내 후 조기 반환.
        //   자동 백업(3-1)보다 먼저 실행해 손상된 DB 가 백업으로 덮이는 것을 막는다.
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

        // 3-1. 자동 백업 (필요 시) — 백그라운드로 밀어 시작 시간 단축
        //   File.Copy 동기 작업으로 1~3초 블로킹될 수 있어 fire-and-forget 으로 처리.
        //   실패 시 FileLogger 및 전역 예외망이 포착하므로 별도 알림 생략.
        _ = Task.Run(() =>
        {
            try
            {
                var backupResult = Settings.RunAutoBackupIfNeeded();
                if (backupResult != null)
                    Debug.WriteLine($"[App] 자동 백업 완료(백그라운드): {backupResult}");
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
            _googleSyncService?.Dispose();
            _googleSyncService = null;
        };
        _window.Activate();

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

    public static Window? GetCurrentWindow() => MainWindow;
}
