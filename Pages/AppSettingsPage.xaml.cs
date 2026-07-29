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

        BuildSpecByteEditors();

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

    #region 학생부 글자 제한

    /// <summary>0 = 모든 학년도(기본), 그 외 = 해당 학년도에만 적용</summary>
    private int SelectedSpecByteYear =>
        CBoxSpecByteYear?.SelectedItem is ComboBoxItem { Tag: string t } && int.TryParse(t, out int y) ? y : 0;

    /// <summary>
    /// 학생부 한도 입력칸을 <see cref="Helpers.NeisHelper.Areas"/> 정의표에서 생성한다.
    /// 영역을 추가·삭제해도 이 화면은 자동으로 따라간다(예전에는 XAML 에 6개가 고정돼 있어
    /// 정의표와 어긋날 수 있었고, 실제로 "봉사활동"이 빠져 있었다).
    /// </summary>
    private void BuildSpecByteEditors()
    {
        // 학년도 선택 목록은 최초 1회만 구성 (올해 기준 ±2년 + '모든 학년도')
        if (CBoxSpecByteYear.Items.Count == 0)
        {
            CBoxSpecByteYear.Items.Add(new ComboBoxItem { Content = "모든 학년도(기본)", Tag = "0" });
            int thisYear = Settings.WorkYear.Value > 0 ? Settings.WorkYear.Value : DateTime.Now.Year;
            for (int y = thisYear + 1; y >= thisYear - 2; y--)
                CBoxSpecByteYear.Items.Add(new ComboBoxItem { Content = $"{y}학년도", Tag = y.ToString() });
            CBoxSpecByteYear.SelectedIndex = 0;
        }

        int year = SelectedSpecByteYear;
        TxtSpecByteScope.Text = year > 0
            ? $"{year}학년도에만 적용됩니다. 비워 둔 영역은 '모든 학년도' 값을 따릅니다."
            : "모든 학년도에 적용됩니다. 특정 학년도만 다르게 하려면 위에서 학년도를 고르세요.";

        PanelSpecBytes.Children.Clear();
        foreach (var area in Helpers.NeisHelper.Areas)
        {
            var box = new NumberBox
            {
                Header = area.Label,
                Tag = area.Key,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                Minimum = 0,
                Maximum = 9000,
                SmallChange = 100,
                Value = Settings.GetSpecMaxBytes(area.Key, year)
            };
            box.ValueChanged += OnSpecByteChanged;
            PanelSpecBytes.Children.Add(box);
        }
    }

    private void OnSpecByteYearChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        BuildSpecByteEditors();   // 선택 학년도 기준으로 현재값 다시 표시
    }

    private void OnSpecByteChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;
        if (sender.Tag is string type && !double.IsNaN(args.NewValue))
            Settings.SetSpecByteOverride(type, (int)args.NewValue, SelectedSpecByteYear);
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

                bool success = Settings.Restore(restorePath);
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
