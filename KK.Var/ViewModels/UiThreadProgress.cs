using System;
using Avalonia.Threading;

namespace KK.Var.ViewModels;

internal sealed class UiThreadProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            handler(value);
            return;
        }

        Dispatcher.UIThread.Post(() => handler(value));
    }
}
