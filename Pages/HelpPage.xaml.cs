using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;

namespace NewSchool.Pages;

public sealed partial class HelpPage : Page
{
    private const string VirtualHostName = "newschool.help";
    private bool _initialized;

    public HelpPage()
    {
        InitializeComponent();
        this.Loaded += async (_, _) =>
        {
            if (_initialized) return;
            try
            {
                var userDataFolder = Path.Combine(
                    Path.GetTempPath(), "NewSchool", "WebView2");
                var env = await CoreWebView2Environment.CreateWithOptionsAsync(
                    null, userDataFolder, new CoreWebView2EnvironmentOptions());
                await HelpWebView.EnsureCoreWebView2Async(env);

                HelpWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                HelpWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                HelpWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                string assetsPath = GetAssetsPath();
                HelpWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    VirtualHostName,
                    assetsPath,
                    CoreWebView2HostResourceAccessKind.Allow);

                HelpWebView.CoreWebView2.Navigate($"https://{VirtualHostName}/help.html");
                _initialized = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HelpPage] WebView2 초기화 오류: {ex.Message}");
            }
        };
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        try
        {
            if (HelpWebView?.CoreWebView2 != null)
                HelpWebView.CoreWebView2.ClearVirtualHostNameToFolderMapping(VirtualHostName);
            HelpWebView?.Close();
            _initialized = false;
        }
        catch { }
    }

    private static string GetAssetsPath()
    {
        var devPath = Path.Combine(AppContext.BaseDirectory, "Assets");
        if (Directory.Exists(devPath))
            return devPath;
        return AppContext.BaseDirectory;
    }
}
