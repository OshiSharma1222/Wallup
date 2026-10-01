using System.Runtime.InteropServices;
using Wallup.Diagnostics;
using static Wallup.Interop.NativeMethods;

namespace Wallup.Interop;

/// <summary>
/// The mouse gesture. A global low-level mouse hook watches for a double right-click on
/// empty desktop, which opens the composer; see OnRightButtonDown. The other gesture,
/// Ctrl+Alt, lives in <see cref="CtrlAltHook"/>.
///
/// A plain left-click no longer opens anything. It took over every click on the desktop,
/// deselecting icons and rubber-band selection included. It is only watched while the
/// composer is open, when clicking the desktop is how you put the box away.
///
/// Both the button-down AND the button-up have to be swallowed. Letting the up through
/// hands focus straight back to the shell, which deactivates the composer the instant it
/// opens - it flashes and vanishes, and the next click appears to open it late.
///
/// Holding Shift passes any click through untouched.
/// </summary>
internal sealed class DesktopClickHook : IDisposable
{
    // The delegate must be rooted for as long as the hook lives, or the GC collects the
    // thunk and the next click faults inside user32.
    private readonly LowLevelMouseProc _proc;
    private IntPtr _hook = IntPtr.Zero;

    /// <summary>True once we have swallowed a down and still owe its matching up.</summary>
    private bool _swallowingClick;

    /// <summary>The same debt for the right button.</summary>
    private bool _swallowingRightClick;

    /// <summary>
    /// A single right-click held back in case a second one makes it a double. If none
    /// comes in time, <see cref="_replay"/> gives it back to the desktop.
    /// </summary>
    private bool _rightPending;
    private uint _lastRightTime;
    private POINT _lastRightPoint;
    private readonly System.Windows.Threading.DispatcherTimer _replay;

    /// <summary>Tags the right-click we replay, so the hook lets its own echo through.</summary>
    private static readonly IntPtr ReplayMarker = new(0x57A11);

    /// <summary>Raised on a left-click on empty desktop while the composer is open.</summary>
    internal event Action? DesktopClickedWhileComposing;

    /// <summary>Raised on a double right-click on empty desktop, in physical pixels.</summary>
    internal event Action<int, int>? DesktopRightDoubleClicked;

    /// <summary>
    /// Whether the composer is open. A held-back right-click is then simply dropped rather
    /// than replayed: handing it to the desktop would take focus away and close the box,
    /// text and all, under the desktop menu. The hook runs on the UI thread, so reading
    /// window state here is safe.
    /// </summary>
    internal Func<bool>? IsComposing { get; set; }

    /// <summary>
    /// Whether a double right-click opens the composer. Off, every right-click goes
    /// straight to the desktop, without the beat of waiting for a second one.
    /// </summary>
    internal bool RightClickEnabled { get; set; } = true;

    internal DesktopClickHook()
    {
        _proc = OnMouseEvent;
        _replay = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime()),
        };
        _replay.Tick += (_, _) => ReplayRightClick();
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

        Log.Info("Desktop click hook installed.");
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

        if (message == WM_LBUTTONUP && _swallowingClick)
        {
            _swallowingClick = false;
            return new IntPtr(1);
        }

        if (message == WM_RBUTTONUP && _swallowingRightClick
            && Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam).dwExtraInfo != ReplayMarker)
        {
            _swallowingRightClick = false;
            return new IntPtr(1);
        }

        if (message == WM_RBUTTONDOWN)
        {
            return OnRightButtonDown(nCode, wParam, lParam);
        }

        if (message != WM_LBUTTONDOWN)
        {
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if (IsComposing?.Invoke() != true || (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0)
        {
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        var hit = WindowFromPoint(data.pt);
        var onDesktop = IsEmptyDesktopAt(data.pt);

        // Without this there is no way to tell "the hook never fired" from "the hook
        // decided this was not desktop".
        Log.Info($"Click at {data.pt.X},{data.pt.Y} over \"{ClassNameOf(hit)}\" " +
                 $"(root \"{ClassNameOf(GetAncestor(hit, GA_ROOT))}\") -> " +
                 $"{(onDesktop ? "closing composer" : "passing through")}.");

        if (!onDesktop)
        {
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        _swallowingClick = true;
        DesktopClickedWhileComposing?.Invoke();
        return new IntPtr(1);
    }

    /// <summary>
    /// A double right-click on empty desktop opens the composer there. Telling a double
    /// from a single means holding the first click back for the double-click time; a
    /// single is then replayed, so the desktop menu still appears, just a beat later.
    /// </summary>
    private IntPtr OnRightButtonDown(int nCode, IntPtr wParam, IntPtr lParam)
    {
        var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

        if (!RightClickEnabled || data.dwExtraInfo == ReplayMarker
            || (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0
            || !IsEmptyDesktopAt(data.pt))
        {
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var isDouble = _rightPending
            && data.time - _lastRightTime <= GetDoubleClickTime()
            && Math.Abs(data.pt.X - _lastRightPoint.X) <= GetSystemMetrics(SM_CXDOUBLECLK) / 2
            && Math.Abs(data.pt.Y - _lastRightPoint.Y) <= GetSystemMetrics(SM_CYDOUBLECLK) / 2;

        _swallowingRightClick = true;
        _replay.Stop();

        if (isDouble)
        {
            _rightPending = false;
            Log.Info($"Double right-click at {data.pt.X},{data.pt.Y} -> " +
                     $"{(IsComposing?.Invoke() == true ? "cancelling composer" : "opening composer")}.");
            DesktopRightDoubleClicked?.Invoke(data.pt.X, data.pt.Y);
            return new IntPtr(1);
        }

        _rightPending = true;
        _lastRightTime = data.time;
        _lastRightPoint = data.pt;
        _replay.Start();
        return new IntPtr(1);
    }

    /// <summary>No second click came, so the held-back one was a plain right-click.</summary>
    private void ReplayRightClick()
    {
        _replay.Stop();
        if (!_rightPending)
        {
            return;
        }

        _rightPending = false;

        if (IsComposing?.Invoke() == true)
        {
            return;
        }

        var size = Marshal.SizeOf<INPUT>();
        SendInput(2,
        [
            new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_RIGHTDOWN, dwExtraInfo = ReplayMarker } },
            new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_RIGHTUP, dwExtraInfo = ReplayMarker } },
        ], size);
    }

    /// <summary>
    /// True when the point is over bare desktop. The icon list view covers the whole
    /// desktop, so it is asked whether there is an icon under the point; double
    /// right-clicking an icon used to open the composer instead of the icon's menu.
    /// </summary>
    private static bool IsEmptyDesktopAt(POINT pt)
    {
        var hWnd = WindowFromPoint(pt);
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        var className = ClassNameOf(hWnd);
        if (className == "SysListView32")
        {
            return !DesktopIcons.IsIconAt(hWnd, pt);
        }

        if (className is "SHELLDLL_DefView" or "WorkerW" or "Progman")
        {
            return true;
        }

        var root = GetAncestor(hWnd, GA_ROOT);
        return root != IntPtr.Zero && ClassNameOf(root) is "WorkerW" or "Progman";
    }

    public void Dispose()
    {
        _replay.Stop();

        if (_hook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        Log.Info("Desktop click hook removed.");
    }
}
