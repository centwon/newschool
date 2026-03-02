using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NewSchool.Controls;

public enum MessageBoxButton
{
    OK,
    OKCancel,
    YesNo,
    YesNoCancel
}

public enum MessageBoxResult
{
    None,
    OK,
    Cancel,
    Yes,
    No
}

public enum MessageBoxDefaultButton
{
    Button1,    // Primary Button (기본값)
    Button2,    // Secondary Button
    Button3     // Close Button
}

public static class MessageBox
{
    private static XamlRoot? _xamlRoot;

    // XamlRoot 설정 (앱 시작 시 한 번 설정)
    public static void Initialize(XamlRoot xamlRoot)
    {
        _xamlRoot = xamlRoot;
    }

    // Window에서 자동으로 XamlRoot 추출하여 초기화
    public static void Initialize(Window window)
    {
        if (window?.Content?.XamlRoot != null)
        {
            _xamlRoot = window.Content.XamlRoot;
        }
    }

    // XamlRoot 자동 탐지 및 설정 (수정된 버전)
    private static bool TryAutoInitialize()
    {
        try
        {
            // App.MainWindow에서 XamlRoot 찾기 시도
            if (App.MainWindow?.Content?.XamlRoot != null)
            {
                _xamlRoot = App.MainWindow.Content.XamlRoot;
                return true;
            }

            // App.GetCurrentWindow()에서 찾기 시도 (App에 이 메서드가 있다면)
            try
            {
                var currentWindow = App.GetCurrentWindow();
                if (currentWindow?.Content?.XamlRoot != null)
                {
                    _xamlRoot = currentWindow.Content.XamlRoot;
                    return true;
                }
            }
            catch
            {
                // App.GetCurrentWindow가 없을 수 있음
            }

            // Application의 모든 창 검색
            foreach (var window in WindowHelper.GetActiveWindows())
            {
                if (window?.Content?.XamlRoot != null)
                {
                    _xamlRoot = window.Content.XamlRoot;
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"XamlRoot 자동 탐지 실패: {ex.Message}");
        }

        return false;
    }

    // 기본 메시지박스 (WPF와 동일한 사용법)
    public static async Task<MessageBoxResult> ShowAsync(string message)
    {
        return await ShowAsync(message, "알림", MessageBoxButton.OK);
    }

    // 제목이 있는 메시지박스 (WPF와 동일한 사용법)
    public static async Task<MessageBoxResult> ShowAsync(string message, string title)
    {
        return await ShowAsync(message, title, MessageBoxButton.OK);
    }

    // 완전한 메시지박스 (WPF와 동일한 사용법)
    public static async Task<MessageBoxResult> ShowAsync(string message, string title, MessageBoxButton button)
    {
        return await ShowAsync(message, title, button, MessageBoxDefaultButton.Button1);
    }

    // Yes/No 메시지박스 (간편 메서드)
    public static async Task<ContentDialogResult> ShowYesNoAsync(string message, string title)
    {
        var result = await ShowAsync(message, title, MessageBoxButton.YesNo);
        return result == MessageBoxResult.Yes ? ContentDialogResult.Primary : ContentDialogResult.Secondary;
    }

    // 기본 버튼 설정이 있는 메시지박스
    public static async Task<MessageBoxResult> ShowAsync(string message, string title, MessageBoxButton button, MessageBoxDefaultButton defaultButton)
    {
        // XamlRoot가 없으면 자동 탐지 시도
        if (_xamlRoot == null)
        {
            if (!TryAutoInitialize())
            {
                // 마지막 수단: ContentDialog without XamlRoot (일부 시나리오에서 작동)
                System.Diagnostics.Debug.WriteLine("MessageBox 경고: XamlRoot를 찾을 수 없어 기본 설정으로 표시합니다.");
                return await ShowFallbackAsync(message, title, button);
            }
        }

        var dialog = new ContentDialog()
        {
            Title = title,
            Content = message,
            XamlRoot = _xamlRoot
        };

        // 버튼 타입에 따라 설정
        SetupButtons(dialog, button);

        // 기본 버튼 설정
        SetDefaultButton(dialog, defaultButton);

        // ESC 키 처리
        SetupKeyHandling(dialog, button);

        var result = await dialog.ShowAsync();

        // 결과를 MessageBoxResult로 변환
        return ConvertResult(result, button);
    }

    // 에러 메시지 전용 (제목 "오류" 고정)
    public static async Task ShowErrorAsync(string message, Exception? ex = null)
    {
        var content = ex != null ? $"{message}\n{ex.Message}" : message;
        await ShowAsync(content, "오류", MessageBoxButton.OK);
    }

    // 확인 다이얼로그 (bool 반환, 커스텀 버튼 텍스트 지원)
    public static async Task<bool> ShowConfirmAsync(string message, string title,
        string confirmText = "확인", string cancelText = "취소")
    {
        if (_xamlRoot == null && !TryAutoInitialize())
        {
            System.Diagnostics.Debug.WriteLine("MessageBox 경고: XamlRoot를 찾을 수 없어 기본 설정으로 표시합니다.");
            return false;
        }

        var dialog = new ContentDialog()
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = cancelText,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = _xamlRoot
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    // 버튼 설정
    private static void SetupButtons(ContentDialog dialog, MessageBoxButton button)
    {
        switch (button)
        {
            case MessageBoxButton.OK:
                dialog.PrimaryButtonText = "확인";
                break;

            case MessageBoxButton.OKCancel:
                dialog.PrimaryButtonText = "확인";
                dialog.SecondaryButtonText = "취소";
                break;

            case MessageBoxButton.YesNo:
                dialog.PrimaryButtonText = "예";
                dialog.SecondaryButtonText = "아니오";
                break;

            case MessageBoxButton.YesNoCancel:
                dialog.PrimaryButtonText = "예";
                dialog.SecondaryButtonText = "아니오";
                dialog.CloseButtonText = "취소";
                break;
        }
    }

    // 기본 버튼 설정
    private static void SetDefaultButton(ContentDialog dialog, MessageBoxDefaultButton defaultButton)
    {
        switch (defaultButton)
        {
            case MessageBoxDefaultButton.Button1:
                dialog.DefaultButton = ContentDialogButton.Primary;
                break;
            case MessageBoxDefaultButton.Button2:
                dialog.DefaultButton = ContentDialogButton.Secondary;
                break;
            case MessageBoxDefaultButton.Button3:
                dialog.DefaultButton = ContentDialogButton.Close;
                break;
        }
    }

    // 키보드 처리 설정
    private static void SetupKeyHandling(ContentDialog dialog, MessageBoxButton button)
    {
        dialog.KeyDown += (sender, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                var escapeResult = GetEscapeResult(button);
                if (escapeResult != ContentDialogResult.None)
                {
                    dialog.Hide();
                }
            }
        };
    }

    // ESC 키에 대한 적절한 결과 반환
    private static ContentDialogResult GetEscapeResult(MessageBoxButton button)
    {
        return button switch
        {
            MessageBoxButton.OK => ContentDialogResult.Primary,// OK 버튼
            MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel => ContentDialogResult.None,// Cancel/Close 버튼
            MessageBoxButton.YesNo => ContentDialogResult.Secondary,// No 버튼 (일반적으로 안전한 선택)
            _ => ContentDialogResult.None,
        };
    }

    // 결과 변환
    private static MessageBoxResult ConvertResult(ContentDialogResult result, MessageBoxButton button)
    {
        return result switch
        {
            ContentDialogResult.Primary => button == MessageBoxButton.YesNo || button == MessageBoxButton.YesNoCancel
                                ? MessageBoxResult.Yes : MessageBoxResult.OK,
            ContentDialogResult.Secondary => button == MessageBoxButton.YesNo || button == MessageBoxButton.YesNoCancel
                                ? MessageBoxResult.No : MessageBoxResult.Cancel,
            _ => button == MessageBoxButton.YesNoCancel
                                ? MessageBoxResult.Cancel : MessageBoxResult.None,
        };
    }

    // 폴백 메시지 표시 (XamlRoot 없이)
    private static async Task<MessageBoxResult> ShowFallbackAsync(string message, string title, MessageBoxButton button)
    {
        try
        {
            // 디버그 출력으로 대체
            System.Diagnostics.Debug.WriteLine($"MessageBox: {title} - {message}");

            // 기본적으로 OK 반환
            return MessageBoxResult.OK;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"폴백 메시지 표시 실패: {ex.Message}");
            return MessageBoxResult.None;
        }
    }

    // 초기화 상태 확인
    public static bool IsInitialized => _xamlRoot != null;
}

// Window 헬퍼 클래스 (수정된 버전)
internal static class WindowHelper
{
    public static System.Collections.Generic.IEnumerable<Window> GetActiveWindows()
    {
        var windows = new System.Collections.Generic.List<Window>();

        try
        {
            // App.MainWindow 추가
            if (App.MainWindow != null)
            {
                windows.Add(App.MainWindow);
            }

            // 추가적인 창들이 있다면 여기에 추가
            // 예: 다이얼로그, 팝업 창들
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"활성 창 목록 가져오기 실패: {ex.Message}");
        }

        return windows;
    }
}
