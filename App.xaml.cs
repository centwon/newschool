using System;
using Microsoft.UI.Xaml;
using SQLitePCL;
using System.Diagnostics;
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
        };
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
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

        // 2. DB 초기화
        if (!Settings.Board_Inited.Value)
        {
            await NewSchool.Board.Board.InitAsync();
            Debug.WriteLine("[App] Board 데이터베이스 초기화 완료");
        }
        // 3. scheduler 초기화 — 항상 실행 (신규 테이블 자동 반영)
        await NewSchool.Scheduler.Scheduler.InitAsync();
        Debug.WriteLine("[App] Scheduler 데이터베이스 초기화 완료");

        if (!Settings.School_Inited.Value)
        {
            await NewSchool.SchoolDatabase.InitAsync();
            Debug.WriteLine("[App] School 데이터베이스 초기화 완료");
        }

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
    /// MainWindow 생성 및 표시
    /// </summary>
    private void ShowMainWindow()
    {
        _window = new MainWindow();
        MainWindow = _window;
        MessageBox.Initialize(_window);
        _window.Activate();

        Debug.WriteLine("[App] 앱 시작 완료");
        PrintSettings();

        // Google Calendar 시작 시 동기화 (비동기, fire-and-forget)
        _ = TryStartGoogleSyncAsync();
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
            var syncService = new GoogleSyncService(authService, apiClient);
            var result = await syncService.SyncAllAsync();

            Debug.WriteLine($"[App] Google 시작 동기화 완료: {result.Summary}");

            // 자동 동기화 활성화 시 주기적 동기화 시작
            if (Settings.GoogleAutoSync.Value)
            {
                int intervalMinutes = Settings.GoogleSyncIntervalMinutes.Value;
                if (intervalMinutes < 5) intervalMinutes = 15;
                syncService.StartPeriodicSync(TimeSpan.FromMinutes(intervalMinutes));
                Debug.WriteLine($"[App] Google 자동 동기화 시작: {intervalMinutes}분 간격");
                // 참고: syncService를 Dispose하지 않음 — 앱 수명 동안 유지
            }
            else
            {
                syncService.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Google 동기화 시작 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 설정 정보 로그 출력
    /// </summary>
    private static void PrintSettings()
    {
        Debug.WriteLine("========================================");
        Debug.WriteLine("[App] 현재 설정 정보:");
        Debug.WriteLine($"  - 학교명: {Settings.SchoolName.Value}");
        Debug.WriteLine($"  - 학교코드: {Settings.SchoolCode.Value}");
        Debug.WriteLine($"  - 사용자: {Settings.UserName.Value} ({Settings.User.Value})");
        Debug.WriteLine($"  - 학년도/학기: {Settings.WorkYear.Value}년 {Settings.WorkSemester.Value}학기");
        Debug.WriteLine($"  - 담임반: {Settings.HomeGrade.Value}학년 {Settings.HomeRoom.Value}반");
        Debug.WriteLine("========================================");
    }

    public static Window? GetCurrentWindow() => MainWindow;
}
