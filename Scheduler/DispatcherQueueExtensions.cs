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

    /// <summary>
    /// DispatcherQueue에서 비동기 Task를 실행하고 완료를 대기합니다.
    /// </summary>
    public static Task EnqueueAsync(this DispatcherQueue dispatcher, Func<Task> asyncAction)
    {
        var tcs = new TaskCompletionSource<bool>();

        bool enqueued = dispatcher.TryEnqueue(async () =>
        {
            try
            {
                await asyncAction();
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

    /// <summary>
    /// DispatcherQueue에서 비동기 작업을 실행하고 결과를 반환합니다.
    /// </summary>
    public static Task<T> EnqueueAsync<T>(this DispatcherQueue dispatcher, Func<T> function)
    {
        var tcs = new TaskCompletionSource<T>();

        bool enqueued = dispatcher.TryEnqueue(() =>
        {
            try
            {
                T result = function();
                tcs.TrySetResult(result);
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

    /// <summary>
    /// DispatcherQueue에서 비동기 Task를 실행하고 결과를 반환합니다.
    /// </summary>
    public static Task<T> EnqueueAsync<T>(this DispatcherQueue dispatcher, Func<Task<T>> asyncFunction)
    {
        var tcs = new TaskCompletionSource<T>();

        bool enqueued = dispatcher.TryEnqueue(async () =>
        {
            try
            {
                T result = await asyncFunction();
                tcs.TrySetResult(result);
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
}
