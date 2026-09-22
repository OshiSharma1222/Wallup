using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static Wallup.Interop.NativeMethods;

namespace Wallup.Interop;

/// <summary>
/// Keeps a window living on the desktop: above the wallpaper and the icons, below every
/// real application window, and out of Alt+Tab.
///
/// The subtlety that ate the chips: <c>SetWindowPos</c>'s second argument names the window
/// that will *precede* ours in the z-order, which means ours lands directly *below* it. So
/// passing Progman - the window that paints the wallpaper full-screen - buried every chip
/// behind the desktop, and re-asserting that on WM_WINDOWPOSCHANGING meant a chip vanished
/// the moment it was clicked. HWND_BOTTOM has the same problem for the same reason.
///
/// What we want is the slot directly *above* the desktop stack, so we find the window
/// sitting on top of it and insert after that one instead.
/// </summary>
internal static class DesktopWindow
{
    private const int WmWindowPosChanging = 0x0046;

    private static readonly IntPtr HwndTop = IntPtr.Zero;

    /// <summary>Classes that make up the desktop itself: wallpaper, icons, and their hosts.</summary>
    private static readonly string[] DesktopClasses = ["Progman", "WorkerW"];

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    /// <summary>Pins a window to the desktop layer. Call from OnSourceInitialized.</summary>
    internal static void Pin(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // Tool window keeps it out of Alt+Tab and off the taskbar.
        var exStyle = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

        Sink(handle);
        HwndSource.FromHwnd(handle)?.AddHook(KeepOnDesktop);
    }

    /// <summary>Drops a window into the slot immediately above the desktop.</summary>
    private static void Sink(IntPtr handle) =>
        SetWindowPos(handle, JustAboveTheDesktop(handle), 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    private static IntPtr KeepOnDesktop(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmWindowPosChanging)
        {
            return IntPtr.Zero;
        }

        // Clicking a window normally raises it to the top. Rewrite that back to
        // "just above the desktop" before it lands.
        var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
        pos.hwndInsertAfter = JustAboveTheDesktop(hwnd);
        pos.flags &= ~SWP_NOZORDER;
        Marshal.StructureToPtr(pos, lParam, false);

        return IntPtr.Zero;
    }

    /// <summary>
    /// The window to insert after so we end up directly on top of the desktop: whatever is
    /// currently the lowest window that is not part of the desktop itself.
    ///
    /// Walking the z-order beats naming Progman outright, because the wallpaper can be
    /// painted by a WorkerW sitting above it - slideshows and Explorer restarts both do
    /// that, and anchoring to Progman alone would leave us underneath.
    /// </summary>
    private static IntPtr JustAboveTheDesktop(IntPtr self)
    {
        // GW_HWNDLAST from any top-level window is the bottom of the z-order; from there
        // GW_HWNDPREV climbs back up towards the foreground.
        var bottom = GetWindow(self, GW_HWNDLAST);
        var candidate = HwndTop;

        for (var h = bottom; h != IntPtr.Zero; h = GetWindow(h, GW_HWNDPREV))
        {
            if (h == self || !IsWindowVisible(h) || IsDesktop(h))
            {
                continue;
            }

            candidate = h;
            break;
        }

        return candidate;
    }

    private static bool IsDesktop(IntPtr hWnd) => DesktopClasses.Contains(ClassNameOf(hWnd));
}
