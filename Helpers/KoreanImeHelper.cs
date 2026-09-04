using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NewSchool.Helpers;

/// <summary>
/// TextBox 포커스 진입 시 IME 를 한글 조합 모드로 전환하는 헬퍼.
/// WinUI 3 TextBox 는 TSF(Text Services Framework) 로 입력을 처리하므로
/// ImmSetConversionStatus 호출이 수락되어도 실제로는 적용되지 않는다.
/// 따라서 <c>keybd_event(VK_HANGUL)</c> 로 한/영 키를 시뮬레이션하여 실제 전환을 유도한다.
/// 현재 상태 감지는 ImmGetConversionStatus 의 NATIVE 비트를 참고해 이미 한글이면 skip.
/// </summary>
public static class KoreanImeHelper
{
    #region Win32 Interop

    private const int IME_CMODE_NATIVE = 0x0001;  // 한글 조합

    // Virtual-Key codes
    private const ushort VK_HANGUL = 0x15;  // 한/영 토글
    private const ushort VK_IME_ON = 0x16;  // IME 명시 ON (Win10 1903+)

    // SendInput flags
    private const uint INPUT_KEYBOARD  = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint   dwFlags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    /// <summary>
    /// Win32 <c>MOUSEINPUT</c>. 공용체에서 가장 큰 멤버라 <c>INPUT</c> 전체 크기를 결정한다.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int    dx;
        public int    dy;
        public uint   mouseData;
        public uint   dwFlags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint   uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    /// <summary>
    /// Win32 <c>INPUT</c>.
    ///
    /// <para>예전에는 <c>LayoutKind.Explicit</c> 에 <c>KEYBDINPUT</c> 하나만 얹어 두었다.
    /// 그러면 x64 에서 크기가 <b>32바이트</b>로 잡히는데 Win32 가 요구하는 값은
    /// 가장 큰 멤버(<c>MOUSEINPUT</c>) 기준 <b>40바이트</b>다. 크기가 어긋나면
    /// <c>SendInput</c> 이 <c>ERROR_INVALID_PARAMETER(87)</c> 로 0 을 돌려주고 키를 하나도
    /// 보내지 못한다. 폴백(<c>VK_HANGUL</c>)도 같은 구조체를 쓰므로 한글 자동 전환이
    /// 통째로 죽어 있었다.</para>
    ///
    /// <para>크기를 40 으로 못 박지 않고 공용체를 제대로 선언한다 — 이 앱은
    /// win-x86·win-x64·win-arm64 를 모두 게시하고 32비트의 <c>INPUT</c> 은 28바이트라
    /// 상수로 박으면 x86 에서 같은 증상이 재현된다. 바깥을 <c>Sequential</c> 로 두면
    /// 런타임이 플랫폼별 패딩을 알아서 넣는다.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint       type;
        public InputUnion U;
    }

    // mi·hi 는 읽지 않는다. 공용체의 크기를 Win32 와 맞추기 위해 존재한다.
#pragma warning disable CS0649
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT    mi;
        [FieldOffset(0)] public KEYBDINPUT    ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }
#pragma warning restore CS0649

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmGetConversionStatus(IntPtr hIMC, out int conversion, out int sentence);

    #endregion

    #region Attached Property: UseHangul

    public static bool GetUseHangul(DependencyObject obj)
        => (bool)obj.GetValue(UseHangulProperty);

    public static void SetUseHangul(DependencyObject obj, bool value)
        => obj.SetValue(UseHangulProperty, value);

    public static readonly DependencyProperty UseHangulProperty =
        DependencyProperty.RegisterAttached(
            "UseHangul",
            typeof(bool),
            typeof(KoreanImeHelper),
            new PropertyMetadata(false, OnUseHangulChanged));

    private static void OnUseHangulChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;

        tb.GotFocus -= TextBox_GotFocus;
        if (e.NewValue is bool enable && enable)
        {
            tb.GotFocus += TextBox_GotFocus;
        }
    }

    private static void TextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;

        // GotFocus 시점엔 Win32 포커스 이동이 완료되지 않았을 수 있음 → 다음 틱으로 지연.
        var queue = tb.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        if (queue == null)
        {
            ApplyHangulMode();
            return;
        }
        queue.TryEnqueue(DispatcherQueuePriority.Low, ApplyHangulMode);
    }

    private static void ApplyHangulMode()
    {
        try
        {
            IntPtr hwnd = ResolveInputHwnd();
            if (hwnd == IntPtr.Zero) return;

            // 현재 IME 변환 상태 확인 — IMM 이 TSF 와 동기화되어 있지 않을 수 있으나,
            // "사용자가 영문 상태에서 방금 전환한" 일반적인 시나리오 기준 참고값으로 사용.
            bool isHangul = false;
            IntPtr hIMC = ImmGetContext(hwnd);
            if (hIMC != IntPtr.Zero)
            {
                try
                {
                    if (ImmGetConversionStatus(hIMC, out int conv, out _))
                    {
                        isHangul = (conv & IME_CMODE_NATIVE) != 0;
                    }
                }
                finally
                {
                    ImmReleaseContext(hwnd, hIMC);
                }
            }

            System.Diagnostics.Debug.WriteLine($"[KoreanImeHelper] currently Hangul={isHangul}");
            if (isHangul) return;

            // 1차: VK_IME_ON (Win10 1903+) — 명시적 "IME ON", 토글 아님
            uint sent = SendKey(VK_IME_ON);
            System.Diagnostics.Debug.WriteLine($"[KoreanImeHelper] VK_IME_ON SendInput count={sent}");

            if (sent == 0)
            {
                // 2차: VK_HANGUL (레거시 토글) — 이미 영문 상태이므로 한 번 눌러서 한글로
                uint sent2 = SendKey(VK_HANGUL);
                System.Diagnostics.Debug.WriteLine($"[KoreanImeHelper] VK_HANGUL SendInput count={sent2}");
            }
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Warning("KoreanImeHelper", $"한글 입력 모드로 바꾸지 못했다: {ex.Message}");
        }
    }

    /// <summary>
    /// 입력 포커스 HWND 를 찾는다. WinUI 3 TextBox 는 composition 호스트 HWND 에 IME 가 묶이며,
    /// GetFocus() 는 같은 스레드 HWND 만 반환하므로 Main window HWND 를 예비로 시도.
    /// </summary>
    private static IntPtr ResolveInputHwnd()
    {
        IntPtr focus = GetFocus();
        if (focus != IntPtr.Zero) return focus;

        var win = App.MainWindow;
        if (win == null) return IntPtr.Zero;
        try
        {
            return WinRT.Interop.WindowNative.GetWindowHandle(win);
        }
        catch { return IntPtr.Zero; }
    }

    private static uint SendKey(ushort vk)
    {
        var inputs = new INPUT[2];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].U.ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = 0, time = 0, dwExtraInfo = IntPtr.Zero };
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].U.ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    #endregion
}
