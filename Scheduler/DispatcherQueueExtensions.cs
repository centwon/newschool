using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace NewSchool.Scheduler;

/// <summary>
/// DispatcherQueue 확장 메서드
/// </summary>
public static class DispatcherQueueExtensions
{
    /// <summary>
    /// DispatcherQueue에서 비동기 작업을 실행하고 완료를 대기합니다.
    /// </summary>
    public static Task EnqueueAsync(this DispatcherQueue dispatcher, Action action)
    {
        var tcs = new TaskCompletionSource<bool>();

        bool enqueued = dispatcher.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        if (!enqueued)
        {
            tcs.TrySetException(new InvalidOperationException("Failed to enqueue operation"));
        }

        return tcs.Task;
    }

    // 나머지 오버로드 셋(Func<Task>·Func<T>·Func<Task<T>>)은 호출부가 없어 지웠다(2026-09-28).
    // 쓰는 곳은 DayCell 의 Action 판 하나뿐이다. ⚠ Func<Task> 판을 되살릴 때는 람다가
    // async 이면 이 Action 판이 아니라 그쪽으로 묶이는지 확인할 것(async void 가 되면 예외를 놓친다).
}
