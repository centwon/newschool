using Microsoft.UI.Xaml;

namespace NewSchool.Helpers;

/// <summary>
/// 저장된 테마 설정(<c>Settings.Theme</c>)을 창에 입힌다.
///
/// 예전에는 설정 화면의 콤보가 <b>그 자리에서 메인 창에만</b> 테마를 걸었다.
/// 값은 저장됐지만 시작할 때 다시 읽는 곳이 없어 앱을 껐다 켜면 라이트로 돌아왔고,
/// 새로 연 보조 창(편집기·수업 일지·학생부 일괄 등)도 메인 창과 테마가 어긋났다.
/// 창 크기·'항상 위에' 는 시작 시 복원되는데 테마만 빠져 있던 셈이다.
///
/// 그래서 창을 만드는 쪽이 <see cref="Apply"/> 한 줄을 부르도록 통일했다.
/// 부르는 자리는 <c>MainWindow.SetAlwaysOnTop</c> 을 부르던 그 자리다.
/// </summary>
public static class ThemeHelper
{
    /// <summary>설정에 저장된 테마. 알 수 없는 값이면 시스템 설정을 따른다.</summary>
    public static ElementTheme Current => Settings.Theme?.Value switch
    {
        "Light" => ElementTheme.Light,
        "Dark" => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    /// <summary>
    /// 창의 루트 요소에 저장된 테마를 건다.
    /// <c>Content</c> 가 아직 없거나 <see cref="FrameworkElement"/> 가 아니면 아무 일도 하지 않는다.
    /// </summary>
    public static void Apply(Window? window)
    {
        if (window?.Content is FrameworkElement root)
            root.RequestedTheme = Current;
    }
}
