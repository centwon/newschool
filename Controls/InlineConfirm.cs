using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NewSchool.Controls;

/// <summary>
/// 대화상자(ContentDialog) <b>안에서</b> 묻는 확인 — 누른 단추에 붙는 작은 팝업이다.
///
/// <para>⚠ 열린 대화상자 안에서는 <see cref="MessageBox"/> 를 부르지 말 것. 대화상자는 한 번에 하나만
/// 뜨므로 <c>MessageBox</c> 가 게이트에서 바깥 창이 닫히기를 기다린다 — 눌러도 아무 반응이 없다가
/// 창을 닫은 뒤에야 확인이 떴다(수업 변경 목록 [삭제]·수업 편집 [강의실 다시 정하기], 2026-09-30).
/// 팝업(Flyout)은 대화상자가 아니라 그 위에 바로 뜬다. 알림은 창 안 InfoBar 로 한다.</para>
/// </summary>
public static class InlineConfirm
{
    /// <summary>
    /// <paramref name="anchor"/> 옆에 확인 팝업을 띄우고 답을 기다린다.
    /// </summary>
    /// <returns>확인 단추를 눌렀으면 true. 팝업 밖을 누르거나 Esc 로 닫으면 false.</returns>
    public static Task<bool> AskAsync(FrameworkElement anchor, string message, string confirmText)
    {
        var answer = new TaskCompletionSource<bool>();

        var confirm = new Button
        {
            Content = confirmText,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 12,
                MaxWidth = 360,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    confirm
                }
            }
        };

        confirm.Click += (_, _) =>
        {
            answer.TrySetResult(true);
            flyout.Hide();
        };
        flyout.Closed += (_, _) => answer.TrySetResult(false);

        flyout.ShowAt(anchor);
        return answer.Task;
    }
}
