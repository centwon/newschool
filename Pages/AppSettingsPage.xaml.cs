using System;
using System.Diagnostics;
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

        LogLevelComboBox.SelectedIndex = Settings.LogLevel.Value switch
        {
            "Debug" => 0,
            "Info" => 1,
            "Warning" => 2,
            "Error" => 3,
            _ => 1
        };

        _isInitialized = true;
    }

    #region 일반

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
                await MessageBox.ShowAsync($"백업이 완료되었습니다.\n경로: {backupPath}", "백업 완료");
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
            var picker = new FileOpenPicker();
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            picker.ViewMode = PickerViewMode.List;
            picker.FileTypeFilter.Add(".db");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                bool success = Settings.Restore(file.Path);
                if (success)
                {
                    AppSettingsPage_Loaded(this, new RoutedEventArgs());
                    await MessageBox.ShowAsync("설정이 복원되었습니다.", "복원 완료");
                }
                else
                {
                    await MessageBox.ShowAsync("설정 복원 중 오류가 발생했습니다.", "복원 실패");
                }
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync(ex.Message, "복원 오류");
        }
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

    private async void OnPrintSettingsClick(object sender, RoutedEventArgs e)
    {
        Settings.PrintAll();
        await MessageBox.ShowAsync("설정 정보가 디버그 콘솔에 출력되었습니다.", "설정 출력");
    }

    #endregion
}
