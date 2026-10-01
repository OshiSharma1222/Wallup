using System.Windows.Interop;
using System.Windows.Threading;
using Wallup.Diagnostics;
using static Wallup.Interop.NativeMethods;

namespace Wallup.Interop;

/// <summary>
/// Notices Explorer coming back after a crash or a restart, which takes the desktop -
/// Progman, the icons, the wallpaper window - down with it and builds a new one.
///
/// The chips survive, because Windows will not destroy another thread's windows: when an
/// owner dies, its owned windows from elsewhere are simply left without an owner. That is
/// the quiet failure. A chip with no owner no longer rides along when Show Desktop raises
/// the desktop, so it ends up under the new wallpaper the first time the user looks.
///
/// The new Explorer announces itself by broadcasting "TaskbarCreated", the message tray
/// icons use to put themselves back. Broadcasts only reach top-level windows, not
/// message-only ones, so this listens on an ordinary window that is never shown.
/// </summary>
internal sealed class ExplorerWatcher : IDisposable
{
    /// <summary>How long to wait for the new desktop after the taskbar says it is back.</summary>
    private const int MaxChecks = 40;

    private static readonly uint TaskbarCreated = RegisterWindowMessage("TaskbarCreated");

    private readonly HwndSource _window;
    private readonly DispatcherTimer _wait;
    private int _checks;

    /// <summary>Raised on the UI thread once a desktop is there to pin to again.</summary>
    internal event Action? Restarted;

    internal ExplorerWatcher()
    {
        _window = new HwndSource(new HwndSourceParameters("Wallup.ExplorerWatcher")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        });
        _window.AddHook(OnMessage);

        _wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _wait.Tick += CheckForDesktop;
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (TaskbarCreated != 0 && (uint)msg == TaskbarCreated)
        {
            Log.Info("Explorer started; waiting for the desktop.");
            _checks = 0;
            _wait.Start();
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// The taskbar can announce itself before the desktop window exists, and pinning to
    /// nothing would be no better than what we have. Wait until there is a Progman.
    /// </summary>
    private void CheckForDesktop(object? sender, EventArgs e)
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero || !IsWindowVisible(progman))
        {
            if (++_checks >= MaxChecks)
            {
                _wait.Stop();
                Log.Warn("Explorer came back without a desktop; chips are left as they are.");
            }

            return;
        }

        _wait.Stop();
        Restarted?.Invoke();
    }

    public void Dispose()
    {
        _wait.Stop();
        _window.RemoveHook(OnMessage);
        _window.Dispose();
    }
}
