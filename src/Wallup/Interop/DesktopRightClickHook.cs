using System.Runtime.InteropServices;
using Wallup.Diagnostics;
using static Wallup.Interop.NativeMethods;

namespace Wallup.Interop;

/// <summary>
/// The gesture. A global low-level mouse hook watches for a right-click that lands on
/// empty desktop, swallows it, and raises <see cref="DesktopRightClicked"/> instead.
///
/// We need a hook rather than a window message because the wallpaper layer sits behind
/// SHELLDLL_DefView, which covers the whole desktop and eats every click before it can
/// reach us. Holding Shift passes the click through to the normal Windows menu, so the
/// user is never locked out of the shell.
/// </summary>
internal sealed class DesktopRightClickHook : IDisposable
{
    // The delegate must be rooted for as long as the hook lives, or the GC collects the
    // thunk and the next click faults inside user32.
    private readonly LowLevelMouseProc _proc;
    private IntPtr _hook = IntPtr.Zero;

    /// <summary>Raised on the hook thread with the screen-space click point.</summary>
    internal event Action<int, int>? DesktopRightClicked;

    internal DesktopRightClickHook()
    {
        _proc = OnMouseEvent;
    }

    internal bool IsInstalled => _hook != IntPtr.Zero;

    internal bool Install()
    {
        if (_hook != IntPtr.Zero)
        {
            return true;
        }

        // A low-level hook is global but runs on the installing thread, so this must be
        // called from a thread with a message pump - i.e. the WPF UI thread.
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);

        if (_hook == IntPtr.Zero)
        {
            Log.Warn($"SetWindowsHookEx failed (win32 error {Marshal.GetLastWin32Error()}).");
            return false;
        }

        Log.Info("Desktop right-click hook installed.");
        return true;
    }

    private IntPtr OnMouseEvent(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Anything slow in here stalls the whole system's input, and Windows will
        // silently evict the hook if we blow the timeout. Do the minimum, then bail.
        if (nCode < 0 || wParam != WM_RBUTTONDOWN)
        {
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0)
        {
            return CallNextHookEx(_hook, nCode, wParam, lParam); // escape hatch
        }

        var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        if (!IsEmptyDesktopAt(data.pt))
        {
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        DesktopRightClicked?.Invoke(data.pt.X, data.pt.Y);

        // Swallow the click so the shell never shows its own context menu. The matching
        // WM_RBUTTONUP is harmless on its own, so we let it through.
        return new IntPtr(1);
    }

    /// <summary>
    /// True when the point is over bare desktop. We accept the icon list view because
    /// that is what covers empty desktop space; clicking an actual icon still lands on
    /// the same HWND, which is a known limitation tracked for the next iteration.
    /// </summary>
    private static bool IsEmptyDesktopAt(POINT pt)
    {
        var hWnd = WindowFromPoint(pt);
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        var cls = ClassNameOf(hWnd);
        if (cls is "SysListView32" or "SHELLDLL_DefView" or "WorkerW" or "Progman")
        {
            return true;
        }

        var root = GetAncestor(hWnd, GA_ROOT);
        return root != IntPtr.Zero && ClassNameOf(root) is "WorkerW" or "Progman";
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        Log.Info("Desktop right-click hook removed.");
    }
}
