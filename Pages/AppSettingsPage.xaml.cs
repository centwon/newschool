using System;
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
        LanguageComboBox.SelectedIndex = Settings.Language.Value == "ko-KR" ? 0 : 1;

        EnableCacheToggle.IsOn = Settings.EnableCache.Value;
        DefaultPageSizeNumberBox.Value = Settings.DefaultPageSize.Value;

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

        var window = App.MainWindow;
        if (window != null)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            // TODO: P/Invoke를 사용하여 창을 항상 위에 표시
        }
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (ThemeComboBox.SelectedItem is ComboBoxItem item)
        {
            string? theme = item.Tag as string;
            if (theme != null)
            {
                Settings.Theme.Set(theme);
                var rootElement = App.MainWindow?.Content as FrameworkElement;
                if (rootElement != null)
                {
                    rootElement.RequestedTheme = theme switch
                    {
                        "Light" => ElementTheme.Light,
                        "Dark" => ElementTheme.Dark,
                        _ => ElementTheme.Default
                    };
                }
            }
        }
    }

    private async void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (LanguageComboBox.SelectedItem is ComboBoxItem item)
        {
            string? language = item.Tag?.ToString();
            if (!string.IsNullOrEmpty(language))
            {
                Settings.Language.Set(language);
                await MessageBox.ShowAsync("언어 변경은 앱을 다시 시작한 후 적용됩니다.", "언어 변경");
            }
        }
    }

    #endregion

    #region 성능/캐시

    private void OnEnableCacheToggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        Settings.EnableCache.Set(EnableCacheToggle.IsOn);
    }

    private void OnDefaultPageSizeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;
        if (!double.IsNaN(args.NewValue))
            Settings.DefaultPageSize.Set((int)args.NewValue);
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
            string? backupPath = Settings.Backup();
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
            var picker = new Windows.Storage.Pickers.FolderPicker();
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                var confirmed = await MessageBox.ShowConfirmAsync(
                    $"'{folder.Name}' 백업을 복원하시겠습니까?\n현재 데이터가 덮어씌워집니다.\n복원 후 앱을 재시작해야 합니다.",
                    "복원 확인", "복원", "취소");
                if (!confirmed) return;

                bool success = Settings.Restore(folder.Path);
                if (success)
                {
                    await MessageBox.ShowAsync("복원이 완료되었습니다.\n앱을 재시작해주세요.", "복원 완료");
                }
                else
                {
                    await MessageBox.ShowAsync("복원 중 오류가 발생했습니다.", "복원 실패");
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
        var logDir = System.IO.Path.Combine(Settings.UserDataPath, "Logs");
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
        var confirmed = await MessageBox.ShowConfirmAsync(
            "모든 설정을 기본값으로 초기화하시겠습니까?\n이 작업은 되돌릴 수 없습니다.",
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

    private void OnDownloadLinkClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_downloadUrl))
        {
            Process.Start(new ProcessStartInfo(_downloadUrl) { UseShellExecute = true });
        }
    }

    #endregion
}
