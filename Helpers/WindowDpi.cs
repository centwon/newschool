using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace NewSchool.Helpers;

/// <summary>
/// 창 크기를 DIP(100% 배율 기준 픽셀)로 다룬다.
///
/// <para><c>AppWindow.Resize</c>·<c>AppWindow.Size</c> 는 <b>물리 픽셀</b>이다. 앱이 DPI 비인식으로 돌던 때는
/// Windows 가 창을 통째로 늘려 줘서 둘이 같았지만, 매니페스트를 넣어 PerMonitorV2 가 된 뒤로는
/// (2026-10-18) 150% 배율에서 900 을 주면 600 DIP 짜리 작은 창이 된다. 크기는 DIP 로 정하고 저장한다.</para>
/// </summary>
public static class WindowDpi
{
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    /// <summary>이 창이 놓인 모니터의 배율(1.0 = 100%).</summary>
    /// <remarks>창의 DPI(<c>GetDpiForWindow</c>)가 아니라 <b>모니터의</b> DPI 를 본다 — 아직 띄우지 않은
    /// 보조 창은 생성자에서 96 을 돌려줘, 150% 에서도 크기가 그대로였다(2026-10-18 실측).</remarks>
    public static double Scale(Window window)
    {
        IntPtr monitor = MonitorFromWindow(WinRT.Interop.WindowNative.GetWindowHandle(window), MONITOR_DEFAULTTONEAREST);
        return GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 && dpi > 0
            ? dpi / 96.0
            : 1.0;
    }

    /// <summary>DIP 로 준 크기로 창을 맞춘다.</summary>
    public static void ResizeDip(Window window, int width, int height)
    {
        double scale = Scale(window);
        window.AppWindow.Resize(new SizeInt32(
            (int)Math.Round(width * scale), (int)Math.Round(height * scale)));
    }

    /// <summary>물리 픽셀 크기를 DIP 로 — 저장할 때 쓴다.</summary>
    public static SizeInt32 ToDip(Window window, SizeInt32 physical)
    {
        double scale = Scale(window);
        return new SizeInt32(
            (int)Math.Round(physical.Width / scale), (int)Math.Round(physical.Height / scale));
    }
}
