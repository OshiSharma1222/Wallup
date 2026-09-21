using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static Wallup.Interop.NativeMethods;

namespace Wallup.Interop;

/// <summary>
/// Keeps a window living on the desktop: above the wallpaper and the icons, below every
/// real application window, and out of Alt+Tab.
///
/// We do NOT use HWND_BOTTOM for this. Bottom of the z-order is below Progman, which would
/// put the window underneath the desktop itself and make it invisible. Instead we insert
/// directly above Progman and re-assert that on every position change, because clicking a
/// window normally raises it to the top.
/// </summary>
internal static class DesktopWindow
{
    private const int WmWindowPosChanging = 0x0046;

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

        Lower(handle);
        HwndSource.FromHwnd(handle)?.AddHook(KeepOnDesktop);
    }

    private static void Lower(IntPtr handle)
    {
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            SetWindowPos(handle, progman, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    private static IntPtr KeepOnDesktop(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmWindowPosChanging)
        {
            return IntPtr.Zero;
        }

        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        // Rewrite the requested z-order back to "just above the desktop" before it lands.
        var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
        pos.hwndInsertAfter = progman;
        pos.flags &= ~SWP_NOZORDER;
        Marshal.StructureToPtr(pos, lParam, false);

        return IntPtr.Zero;
    }
}
