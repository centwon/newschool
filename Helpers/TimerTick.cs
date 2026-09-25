using System;
using Microsoft.UI.Dispatching;

namespace NewSchool.Helpers;

/// <summary>
/// 화면(페이지·컨트롤)이 가진 <see cref="DispatcherQueueTimer"/> 의 <c>Tick</c> 을 <b>약하게</b> 잇는다.
///
/// <para>⚠ <c>timer.Tick += (_, _) =&gt; DoWork();</c> 로 이으면 그 화면은 <b>영영 수거되지 않는다.</b>
/// 이 타이머는 XAML 요소가 아니라서 WinUI 의 참조 추적에 끼지 않는다 — 네이티브 타이머가 쥔
/// 처리기가 GC 뿌리가 되어, 처리기가 붙잡은 화면을 통째로 살려 둔다. <c>Stop()</c> 해도 풀리지 않는다.
/// 홈 화면이 이렇게 새서, 홈을 드나들 때마다 메모판(메모 줄마다 체크박스)째 페이지가 쌓였다 —
/// 메모 500개 모래상자에서 홈 한 번에 약 240MB, 세 번이면 작업 집합 1GB(2026-09-25 실측).
/// 학생 카드·학급 일지 칸·수업홈도 같은 모양으로 샜다.</para>
///
/// <para>여기서는 처리기가 화면을 약한 참조로만 가진다. 화면이 수거되면 다음 틱에서 타이머가
/// 스스로 멈춘다. <paramref name="onTick"/> 은 <c>static</c> 람다로 넘길 것 — 바깥의 <c>this</c> 를
/// 붙잡으면 약한 참조가 헛것이 된다(컴파일러가 막아 준다).</para>
/// </summary>
public static class TimerTick
{
    public static void TickWeakly<T>(this DispatcherQueueTimer timer, T owner, Action<T> onTick)
        where T : class
    {
        var weakOwner = new WeakReference<T>(owner);
        timer.Tick += (sender, _) =>
        {
            if (weakOwner.TryGetTarget(out var target)) onTick(target);
            else sender.Stop();
        };
    }
}
