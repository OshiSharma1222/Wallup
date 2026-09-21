using System.Runtime.InteropServices;
using Wallup.Diagnostics;
using static Wallup.Interop.NativeMethods;

namespace Wallup.Interop;

/// <summary>
/// The gesture. A global low-level mouse hook watches for a right-click on empty desktop,
/// swallows it, and raises <see cref="DesktopRightClicked"/> instead.
///
/// Both the button-down AND the button-up have to be swallowed. Letting the up through
/// hands focus straight back to the shell, which deactivates the composer the instant it
/// opens - it flashes and vanishes, and the next left-click appears to open it late.
///
/// Holding Shift passes the click through to the normal Windows menu, so the user is never
/// locked out of their own desktop.
/// </summary>
internal sealed class DesktopRightClickHook : IDisposable
{
    // The delegate must be rooted for as long as the hook lives, or the GC collects the
    // thunk and the next click faults inside user32.
    private readonly LowLevelMouseProc _proc;
    private IntPtr _hook = IntPtr.Zero;

    /// <summary>True once we have swallowed a down and still owe its matching up.</summary>
    private bool _swallowingClick;

    /// <summary>Raised with the screen-space click point, in physical pixels.</summary>
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
        // called from a thread with a message pump - the WPF UI thread.
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
        // Anything slow here stalls system-wide input, and Windows silently evicts a hook
        // that blows its timeout. Do the minimum, then get out.
        if (nCode < 0)
        {
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();

        if (message == WM_RBUTTONUP && _swallowingClick)
        {
            _swallowingClick = false;
            return new IntPtr(1);
        }

        if (message != WM_RBUTTONDOWN)
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

        _swallowingClick = true;
        DesktopRightClicked?.Invoke(data.pt.X, data.pt.Y);
        return new IntPtr(1);
    }

    /// <summary>
    /// True when the point is over bare desktop. The icon list view covers the whole
    /// desktop, so an actual icon currently counts too - telling them apart needs
    /// LVM_HITTEST and is tracked as a known gap.
    /// </summary>
    private static bool IsEmptyDesktopAt(POINT pt)
    {
        var hWnd = WindowFromPoint(pt);
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        if (ClassNameOf(hWnd) is "SysListView32" or "SHELLDLL_DefView" or "WorkerW" or "Progman")
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
