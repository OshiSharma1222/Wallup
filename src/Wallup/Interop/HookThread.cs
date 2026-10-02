using System.Windows.Threading;

namespace Wallup.Interop;

/// <summary>
/// A thread that does nothing but run the global input hooks.
///
/// A low-level hook is called on the thread that installed it, and Windows gives that
/// thread a fraction of a second to answer. Miss it once and the hook is removed -
/// silently, with no error and no callback, so the gestures simply stop working until the
/// app is restarted. On the UI thread that was easy to miss: dragging a chip re-renders
/// its glass, a display change or a resume from sleep reloads the wallpaper, and the
/// pointer moving at that moment is enough. Both hooks died together, which is what gave
/// it away.
///
/// Here nothing else ever runs, so the hooks answer at once however busy the UI is.
/// </summary>
internal sealed class HookThread : IDisposable
{
    private readonly Dispatcher _dispatcher;

    internal HookThread()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Wallup input hooks",
            // Under load - signing in, waking up - a normal-priority thread can still be
            // starved past the deadline.
            Priority = ThreadPriority.AboveNormal,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        _dispatcher = dispatcher!;
    }

    /// <summary>Runs on the hook thread and waits for it. Hooks must be made, installed and removed here.</summary>
    internal T Invoke<T>(Func<T> work) => _dispatcher.Invoke(work);

    /// <inheritdoc cref="Invoke{T}(Func{T})"/>
    internal void Invoke(Action work) => _dispatcher.Invoke(work);

    public void Dispose() => _dispatcher.InvokeShutdown();
}
