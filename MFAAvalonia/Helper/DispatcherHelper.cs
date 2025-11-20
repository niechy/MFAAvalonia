using Avalonia;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace MFAAvalonia.Helper;

public static class DispatcherHelper
{
    public static void RunOnMainThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Invoke(action);

    }

    public static T RunOnMainThread<T>(Func<T> func)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return func();
        }

        return Dispatcher.UIThread.Invoke(func);

    }
    public static Task RunOnMainThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        // 不在UI线程：异步投放到UI线程，返回可等待的Task
        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }
    
    public static Task<T> RunOnMainThreadAsync<T>(Func<T> func)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return Task.FromResult(func());
        }

        return Dispatcher.UIThread.InvokeAsync(func).GetTask();
    }

    public static void PostOnMainThread(Action func)
    {
        Dispatcher.UIThread.Post(func);
    }
}
