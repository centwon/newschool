using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace NewSchool.Controls;

/// <summary>
/// 고치던 것을 두고 나가려 할 때 묻는 자리 — 52차(닫을 때 축).
///
/// <para>이 앱에는 두 가지 저장 방식이 함께 있다. 학생카드·학급일지·메모판은 <b>스스로
/// 저장</b>하고(3초 디바운스), 수업 일지 창·메모 편집 창·누가기록 창·학생부 일괄 입력·
/// 게시글 작성은 <b>[저장] 을 눌러야</b> 저장된다. 뒤쪽에서 나가는 길은 여럿인데
/// (버튼, 제목표시줄 X, 왼쪽 메뉴로 이동) <b>길마다 다르게 굴었다</b> — 버튼으로 나가면
/// 묻고 X 로 닫으면 아무 말 없이 사라졌다. 사용자는 어느 길로 나가든 같은 대접을 기대한다.</para>
///
/// <para>여기서 그 규칙을 한 곳에 모은다. 페이지는 <see cref="IUnsavedWork"/> 를 구현하고,
/// 창은 <see cref="AskBeforeClosing"/> 로 X 를 막는다.</para>
/// </summary>
public static class UnsavedWorkGuard
{
    private const string Title = "저장하지 않은 변경";

    /// <summary>
    /// 지금 열려 있는 페이지에 저장하지 않은 편집이 있으면 묻는다.
    /// </summary>
    /// <returns>나가도 되면 true(편집이 없거나 사용자가 나가기를 골랐다).</returns>
    public static async Task<bool> ConfirmLeaveAsync(object? page)
    {
        if (page is not IUnsavedWork work) return true;

        bool dirty;
        try
        {
            dirty = work.HasUnsavedWork;
        }
        catch (Exception ex)
        {
            // 판정에 실패했다고 이동을 막지는 않는다 — 갇히는 쪽이 더 나쁘다.
            Logging.Log.Warning("UnsavedWorkGuard", $"미저장 판정 실패 — 그냥 보낸다: {ex.Message}");
            return true;
        }

        if (!dirty) return true;

        return await MessageBox.ShowConfirmAsync(
            work.UnsavedWorkMessage, Title, "나가기", "계속 편집");
    }

    /// <summary>
    /// 제목표시줄 X 로 닫을 때도 묻게 한다.
    ///
    /// <para><c>Window.Closed</c> 는 이미 닫힌 뒤라 늦다. <c>AppWindow.Closing</c> 은 취소할 수
    /// 있으므로, <b>일단 취소한 뒤</b> 물어보고 사용자가 닫기를 고르면 그때 다시 닫는다.</para>
    /// </summary>
    /// <param name="window">지킬 창.</param>
    /// <param name="hasUnsaved">지금 저장하지 않은 편집이 있는가.</param>
    /// <param name="message">물어볼 때 보여 줄 첫 줄.</param>
    public static void AskBeforeClosing(Window window, Func<bool> hasUnsaved, string message)
    {
        ArgumentNullException.ThrowIfNull(hasUnsaved);

        AskBeforeClosing(window, async () =>
            !hasUnsaved() || await MessageBox.ShowConfirmAsync(message, Title, "닫기", "계속 편집"));
    }

    /// <summary>
    /// 닫아도 되는지 창이 <b>스스로</b> 판단하는 경우(예: "저장 후 닫기 / 저장 안 함 / 취소"
    /// 처럼 고를 것이 셋인 창). 물어보는 방식이 달라도 <b>X 와 닫기 버튼이 같은 길을 지나야</b>
    /// 한다는 규칙은 같다.
    /// </summary>
    /// <param name="confirmCloseAsync">닫아도 되면 true.</param>
    public static void AskBeforeClosing(Window window, Func<Task<bool>> confirmCloseAsync)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(confirmCloseAsync);

        var appWindow = GetAppWindow(window);
        if (appWindow == null) return;   // 창 핸들을 못 얻으면 막지 않는다 — 못 닫는 쪽이 더 나쁘다

        bool confirmed = false;

        appWindow.Closing += async (_, args) =>
        {
            if (confirmed) return;

            // ⚠ 먼저 취소해 두고 묻는다. 물어보는 동안 창이 닫혀 버리면 대답을 받을 자리가 없다.
            args.Cancel = true;

            try
            {
                if (!await confirmCloseAsync()) return;
            }
            catch (Exception ex)
            {
                // 여기서 새어 나가면 창을 영영 닫지 못한다 — 닫아 주고 기록만 남긴다.
                Logging.Log.Error("UnsavedWorkGuard", "닫기 확인 중 오류 — 창을 닫는다", ex);
            }

            confirmed = true;
            window.Close();
        };
    }

    private static Microsoft.UI.Windowing.AppWindow? GetAppWindow(Window window)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            return Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);
        }
        catch (Exception ex)
        {
            Logging.Log.Warning("UnsavedWorkGuard", $"창 핸들을 얻지 못해 닫기 확인을 걸지 못한다: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// 저장하지 않은 편집을 들고 있을 수 있는 화면. 왼쪽 메뉴로 옮겨 갈 때
/// <see cref="UnsavedWorkGuard.ConfirmLeaveAsync"/> 가 이것을 보고 묻는다.
/// </summary>
public interface IUnsavedWork
{
    /// <summary>지금 저장하지 않은 편집이 있는가.</summary>
    bool HasUnsavedWork { get; }

    /// <summary>물어볼 때 보여 줄 첫 줄(무엇이 사라지는지 이름을 대야 한다).</summary>
    string UnsavedWorkMessage { get; }
}
