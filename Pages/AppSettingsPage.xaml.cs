using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace NewSchool.Pages;

public sealed partial class AppSettingsPage : Page
{
    private bool _isInitialized = false;

    public AppSettingsPage()
    {
        this.InitializeComponent();
        this.Loaded += AppSettingsPage_Loaded;
    }

    private void AppSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        StartWithWindowsToggle.IsOn = Settings.IsStartWithWindowsRegistered();
        TopMostToggle.IsOn = Settings.TopMost.Value;
        ThemeComboBox.SelectedIndex = Settings.Theme.Value switch
        {
            "Light" => 0,
            "Dark" => 1,
            "Default" => 2,
            _ => 0
        };
        AutoBackupToggle.IsOn = Settings.AutoBackup.Value;
        AutoBackupIntervalDaysNumberBox.Value = Settings.AutoBackupIntervalDays.Value;
        BackupRetentionCountNumberBox.Value = Settings.BackupRetentionCount.Value;
        UpdateLastBackupTimeText();

        LogLevelComboBox.SelectedIndex = Settings.LogLevel.Value switch
        {
            "Debug" => 0,
            "Info" => 1,
            "Warning" => 2,
            "Error" => 3,
            _ => 1
        };

        DataPathText.Text = Settings.UserDataPath;
        DataModeText.Text = Settings.IsPortableMode ? "포터블 모드 (실행 파일 위치)" : "사용자 폴더 모드";

        CurrentVersionText.Text = $"v{Services.UpdateService.CurrentVersion.ToString(3)}";

        _isInitialized = true;
    }

    #region 일반

    private void OnStartWithWindowsToggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        Settings.SetStartWithWindows(StartWithWindowsToggle.IsOn);
    }

    private void OnTopMostToggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        Settings.TopMost.Set(TopMostToggle.IsOn);

        // 메인 창에 즉시 적용
        MainWindow.SetAlwaysOnTop(App.MainWindow, TopMostToggle.IsOn);
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (ThemeComboBox.SelectedItem is ComboBoxItem item)
        {
            string? theme = item.Tag as string;
            if (theme != null)
            {
                // 저장한 뒤 열려 있는 메인 창에 바로 반영한다.
                // 다음 실행부터는 MainWindow 가, 새로 여는 보조 창은 각자
                // ThemeHelper.Apply 로 같은 값을 집어 온다.
                Settings.Theme.Set(theme);
                Helpers.ThemeHelper.Apply(App.MainWindow);
            }
        }
    }

    #endregion

    #region 백업

    private void OnAutoBackupToggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        Settings.AutoBackup.Set(AutoBackupToggle.IsOn);
    }

    private void OnAutoBackupIntervalDaysChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;
        if (!double.IsNaN(args.NewValue))
            Settings.AutoBackupIntervalDays.Set((int)args.NewValue);
    }

    private void OnBackupRetentionCountChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;
        if (!double.IsNaN(args.NewValue))
            Settings.BackupRetentionCount.Set((int)args.NewValue);
    }

    private async void OnBackupClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // DB 스냅샷 + ZIP 압축은 데이터가 쌓이면 몇 초씩 걸린다. UI 스레드에서 부르면
            // 그동안 창이 얼어붙는다 — 시작 시 자동 백업을 백그라운드로 뺀 것과 같은 이유다.
            string? backupPath = await Task.Run(Settings.Backup);
            if (!string.IsNullOrEmpty(backupPath))
            {
                UpdateLastBackupTimeText();
                await MessageBox.ShowAsync($"백업이 완료되었습니다.\n경로: {backupPath}", "백업 완료");
            }
            else
                await MessageBox.ShowAsync("백업 중 오류가 발생했습니다.", "백업 실패");
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync(ex.Message, "백업 오류");
        }
    }

    private async void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // 신규 백업은 ZIP 단일 파일. 구버전 폴더 백업은 폴더 안의 .db 를 선택하면 폴더째 복원.
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".zip");
            picker.FileTypeFilter.Add(".db");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                // 구버전 폴더 백업(backup_*) 안의 .db 를 선택했다면 폴더 전체 복원
                string restorePath = file.Path;
                var parent = System.IO.Path.GetDirectoryName(file.Path);
                if (file.Path.EndsWith(".db", StringComparison.OrdinalIgnoreCase) &&
                    parent != null &&
                    System.IO.Path.GetFileName(parent).StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
                {
                    restorePath = parent;
                }

                var displayName = System.IO.Path.GetFileName(restorePath);
                var confirmed = await MessageBox.ShowConfirmAsync(
                    $"'{displayName}' 백업을 복원하시겠습니까?\n현재 데이터가 덮어씌워집니다.\n복원 후 앱을 재시작해야 합니다.",
                    "복원 확인", "복원", "취소");
                if (!confirmed) return;

                // 복원도 ZIP 해제 + 파일 복사라 UI 스레드에서 부르면 창이 멎는다.
                bool success = await Task.Run(() => Settings.Restore(restorePath));
                if (success)
                {
                    await MessageBox.ShowAsync("복원이 완료되었습니다.\n앱을 재시작해주세요.", "복원 완료");
                }
                else
                {
                    // 데이터 DB 는 실패하면 되돌리지만, 설정(Settings.db) 만 실패하는 경우가 있어
                    // "아무것도 안 바뀌었다" 고 단정하지 않는다.
                    await MessageBox.ShowAsync(
                        "복원 중 오류가 발생했습니다.\n" +
                        "일부만 복원됐을 수 있으니 앱을 다시 시작한 뒤 데이터를 확인하세요.\n" +
                        "자세한 내용은 [고급] > [로그 폴더 열기] 에서 볼 수 있습니다.",
                        "복원 실패");
                }
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync(ex.Message, "복원 오류");
        }
    }

    private void OnOpenBackupFolderClick(object sender, RoutedEventArgs e)
    {
        var backupDir = Settings.BackupDirectory;
        if (!System.IO.Directory.Exists(backupDir))
            System.IO.Directory.CreateDirectory(backupDir);
        Process.Start(new ProcessStartInfo { FileName = backupDir, UseShellExecute = true });
    }

    private void UpdateLastBackupTimeText()
    {
        var lastBackup = Settings.LastBackupTime.Value;
        if (!string.IsNullOrEmpty(lastBackup) && DateTime.TryParse(lastBackup, out var dt))
            LastBackupTimeText.Text = $"마지막 백업: {dt:yyyy-MM-dd HH:mm}";
        else
            LastBackupTimeText.Text = "마지막 백업: 없음";
    }

    #endregion

    #region 고급

    private void OnLogLevelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (LogLevelComboBox.SelectedItem is ComboBoxItem item)
        {
            string? logLevel = item.Tag?.ToString();
            if (logLevel != null)
            {
                Settings.LogLevel.Set(logLevel);
                ApplyLogLevel(logLevel);
            }
        }
    }

    private static void ApplyLogLevel(string level)
    {
        var logLevel = level switch
        {
            "Debug" => Logging.LogLevel.Debug,
            "Info" => Logging.LogLevel.Info,
            "Warning" => Logging.LogLevel.Warning,
            "Error" => Logging.LogLevel.Error,
            _ => Logging.LogLevel.Info
        };
        Logging.FileLogger.Instance.SetMinimumLevel(logLevel);
    }

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
    {
        var logDir = System.IO.Path.Combine(Settings.RootPath, "Logs");
        if (!System.IO.Directory.Exists(logDir))
            System.IO.Directory.CreateDirectory(logDir);
        Process.Start(new ProcessStartInfo { FileName = logDir, UseShellExecute = true });
    }

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
    {
        var dataDir = Settings.UserDataPath;
        if (!System.IO.Directory.Exists(dataDir))
            System.IO.Directory.CreateDirectory(dataDir);
        Process.Start(new ProcessStartInfo { FileName = dataDir, UseShellExecute = true });
    }

    private async void OnResetSettingsClick(object sender, RoutedEventArgs e)
    {
        // "모든 설정" 에는 학교 정보·NEIS 키·구글 연결도 들어간다(Settings 테이블을 통째로 비운다).
        // 지우고 나면 다시 시작할 때 초기 설정 창부터 다시 밟아야 하므로 그 사실을 먼저 말한다.
        var confirmed = await MessageBox.ShowConfirmAsync(
            "모든 설정을 기본값으로 초기화하시겠습니까?\n\n" +
            "학교 정보·NEIS 인증키·구글 계정 연결·시정표·담임 학급까지 함께 지워지며,\n" +
            "다시 시작하면 초기 설정을 처음부터 다시 해야 합니다.\n" +
            "학생·수업·게시글 등 저장된 데이터는 지워지지 않습니다.\n\n" +
            "이 작업은 되돌릴 수 없습니다.",
            "설정 초기화", "초기화", "취소");
        if (confirmed)
        {
            Settings.ResetToDefaults();
            _isInitialized = false;
            AppSettingsPage_Loaded(this, new RoutedEventArgs());
            await MessageBox.ShowAsync("모든 설정이 기본값으로 초기화되었습니다.", "초기화 완료");
        }
    }

    #endregion

    #region 업데이트

    private string _downloadUrl = "";

    private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateProgressRing.Visibility = Visibility.Visible;
        UpdateProgressRing.IsActive = true;
        UpdateStatusText.Visibility = Visibility.Collapsed;
        DownloadLink.Visibility = Visibility.Collapsed;

        var result = await Services.UpdateService.CheckForUpdateAsync();

        UpdateProgressRing.IsActive = false;
        UpdateProgressRing.Visibility = Visibility.Collapsed;
        CheckUpdateButton.IsEnabled = true;
        UpdateStatusText.Visibility = Visibility.Visible;

        if (!result.IsSuccess)
        {
            UpdateStatusText.Text = result.ErrorMessage;
            return;
        }

        var info = result.Info!;
        if (info.IsUpdateAvailable)
        {
            UpdateStatusText.Text = $"새 버전이 있습니다: v{info.LatestVersion.ToString(3)}"
                + (string.IsNullOrEmpty(info.ReleaseName) ? "" : $"\n{info.ReleaseName}")
                + (string.IsNullOrEmpty(info.ReleaseNotes) ? "" : $"\n\n{info.ReleaseNotes}");
            _downloadUrl = info.DownloadUrl;
            DownloadLink.Visibility = Visibility.Visible;
        }
        else
        {
            UpdateStatusText.Text = "현재 최신 버전을 사용하고 있습니다.";
        }
    }

    private async void OnDownloadLinkClick(object sender, RoutedEventArgs e)
    {
        // ⚠ _downloadUrl 은 GitHub API 응답에서 온 외부 문자열이다. UseShellExecute 로 그대로
        //    넘기면 URL 이 아닌 값이 왔을 때 셸이 그걸 실행해 버린다. http/https 만 연다.
        //    (MainWindow 의 업데이트 안내도 Launcher 로 여는데 여기만 달랐다)
        if (!Uri.TryCreate(_downloadUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            UpdateStatusText.Text = "다운로드 주소가 올바르지 않습니다. 릴리스 페이지에서 직접 받아주세요.";
            return;
        }

        try
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppSettingsPage] 다운로드 링크 열기 실패: {ex}");
            UpdateStatusText.Text = $"브라우저를 열지 못했습니다: {ex.Message}";
        }
    }

    #endregion
}
