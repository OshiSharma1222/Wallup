using System.Windows;
using System.Windows.Interop;
using static Wallup.Interop.NativeMethods;

namespace Wallup.Interop;

/// <summary>
/// Takes the keyboard, which a background process is normally not allowed to do.
///
/// The composer opens from a click the hook swallowed, so as far as Windows is concerned
/// our process received no input and has not earned the foreground. SetForegroundWindow on
/// its own then quietly does nothing: the box appears, the caret blinks somewhere else,
/// and everything the user types goes to whatever was in front. Joining the current
/// foreground thread's input queue for the length of the call makes the rule pass.
/// </summary>
internal static class ForegroundWindow
{
    /// <summary>Brings a window forward and gives it the keyboard.</summary>
    internal static void Take(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var self = GetCurrentThreadId();
        var owner = GetWindowThreadProcessId(GetForegroundWindow(), out _);

        var attached = owner != 0 && owner != self && AttachThreadInput(self, owner, true);

        SetForegroundWindow(handle);
        SetFocus(handle);

        if (attached)
        {
            AttachThreadInput(self, owner, false);
        }
    }
}
