using System;
using System.Threading;
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

    // WinUI 3 는 ContentDialog 를 동시에 하나만 허용하고, 이미 열려 있는데 ShowAsync 를
    // 다시 호출하면 예외를 던진다. await 없이 던지는 호출(`_ = MessageBox.ShowAsync(...)`)이
    // 연달아 발생하면(예: 좌석 미사용 토글 연타 → CheckSeat 경고 2회) 두 번째가 터지면서
    // 정작 안내 메시지는 사라지고 엉뚱한 오류창이 떴다. 게이트로 직렬화한다.
    private static readonly SemaphoreSlim _dialogGate = new(1, 1);

    // 이 클래스를 거치지 않는 ad-hoc ContentDialog 가 열려 있을 수도 있다(게이트가 알 수 없음).
    // 그 경우 ShowAsync 가 실패하므로, 사용자가 해당 대화상자를 닫을 때까지 잠시 재시도한다.
    private const int DialogRetryDelayMs = 250;
    private const int DialogRetryMaxAttempts = 120; // 최대 약 30초

    /// <summary>
    /// ContentDialog 를 앱 전역에서 한 번에 하나만 열리도록 직렬화하여 표시한다.
    /// 직접 만든 ContentDialog 도 이 메서드를 통해 띄우면 동시 표시 예외를 피할 수 있다.
    /// </summary>
    public static async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        // 직접 만든 대화상자가 XamlRoot 를 빠뜨렸으면 지금 앞에 있는 창으로 채운다.
        dialog.XamlRoot ??= ResolveXamlRoot();

        await _dialogGate.WaitAsync();
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return await dialog.ShowAsync();
                }
                catch (Exception ex) when (attempt < DialogRetryMaxAttempts)
                {
                    // 다른 대화상자가 열려 있는 상황 — 닫힐 때까지 대기 후 재시도
                    System.Diagnostics.Debug.WriteLine(
                        $"[MessageBox] 대화상자 표시 재시도({attempt + 1}): {ex.Message}");
                    await Task.Delay(DialogRetryDelayMs);
                }
            }
        }
        catch (Exception ex)
        {
            // 끝내 표시하지 못한 경우 — 앱을 죽이지 않고 로그만 남긴다.
            System.Diagnostics.Debug.WriteLine($"[MessageBox] 대화상자 표시 실패: {ex.Message}");
            return ContentDialogResult.None;
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    /// <summary>
    /// 지금 사용자가 보고 있는 창. <see cref="TrackWindow"/> 로 등록된 창들 중
    /// 마지막으로 활성화된 것이며, 그 창이 닫히면 null 로 돌아간다.
    /// </summary>
    private static Window? _activeWindow;

    /// <summary>
    /// 이 창에서 부른 대화상자가 <b>이 창 위에</b> 뜨도록 등록한다. 창을 만들 때 한 번 부른다.
    ///
    /// ContentDialog 는 XamlRoot 가 가리키는 창의 시각 트리 안에 그려진다. 그래서 등록하지
    /// 않으면 보조 창(수업 일지·메모·누가기록 …)의 안내가 <b>메인 창</b> 위에 떠서 앞에 있는
    /// 보조 창 뒤에 가리고, 사용자에게는 저장을 눌러도 아무 반응이 없는 것처럼 보인다.
    /// 초기 설정 창은 그때 메인 창이 아직 없어 아예 표시조차 되지 않았다.
    /// </summary>
    public static void TrackWindow(Window window)
    {
        if (window == null) return;

        window.Activated += (s, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
                _activeWindow = s as Window;
        };

        window.Closed += (s, _) =>
        {
            // 닫힌 창의 XamlRoot 로는 대화상자를 띄울 수 없다 — 메인 창으로 되돌린다.
            if (ReferenceEquals(_activeWindow, s)) _activeWindow = null;
        };
    }

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

    /// <summary>
    /// 대화상자를 띄울 XamlRoot 를 <b>부를 때마다</b> 새로 정한다.
    /// 한 번 정해 캐시해 두면 그 창이 닫힌 뒤에도 계속 그것을 써서 표시가 통째로 실패한다.
    /// </summary>
    private static XamlRoot? ResolveXamlRoot()
        => _activeWindow?.Content?.XamlRoot
           ?? App.MainWindow?.Content?.XamlRoot
           ?? _xamlRoot;

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
        // 지금 앞에 있는 창을 매번 새로 찾는다 — 창마다 XamlRoot 가 다르고, 창은 닫힌다.
        var xamlRoot = ResolveXamlRoot();
        if (xamlRoot == null)
        {
            // 마지막 수단: 띄울 창이 없다(창이 아직 없거나 모두 닫힌 중).
            System.Diagnostics.Debug.WriteLine("MessageBox 경고: XamlRoot를 찾을 수 없어 기본 설정으로 표시합니다.");
            return await ShowFallbackAsync(message, title, button);
        }

        var dialog = new ContentDialog()
        {
            Title = title,
            Content = message,
            XamlRoot = xamlRoot
        };

        // 버튼 타입에 따라 설정
        SetupButtons(dialog, button);

        // 기본 버튼 설정
        SetDefaultButton(dialog, defaultButton);

        // ESC 키 처리
        SetupKeyHandling(dialog, button);

        var result = await ShowDialogAsync(dialog);

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
        var xamlRoot = ResolveXamlRoot();
        if (xamlRoot == null)
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
            XamlRoot = xamlRoot
        };

        var result = await ShowDialogAsync(dialog);
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
            // None(ESC·바깥 클릭 등 닫힘): 버튼 구성별 "안전한 기본 선택"으로 변환.
            // 기존에는 OK 전용 대화상자에서 ESC 를 누르면 None 이 반환되어
            // GetEscapeResult 의 의도(OK)와 어긋났다.
            _ => button switch
            {
                MessageBoxButton.OK => MessageBoxResult.OK,          // 확인뿐이므로 닫힘 = 확인
                MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
                MessageBoxButton.YesNo => MessageBoxResult.No,       // 안전한 선택
                MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
                _ => MessageBoxResult.None,
            },
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
