using System.Runtime.InteropServices;
using Wallup.Diagnostics;
using static Wallup.Interop.NativeMethods;

namespace Wallup.Interop;

/// <summary>
/// The keyboard gesture: Ctrl and Alt pressed together and let go, with nothing else in
/// between. A global low-level keyboard hook watches for it and raises
/// <see cref="Pressed"/>.
///
/// It fires on the release, not the press, because Ctrl+Alt is also the start of a great
/// many real shortcuts - Ctrl+Alt+Del among them. Any other key pressed while the two are
/// held means the user was on the way to one of those, and the gesture is off. No key is
/// ever swallowed; the hook only listens.
/// </summary>
internal sealed class CtrlAltHook : IDisposable
{
    /// <summary>
    /// The scan code of the left Ctrl that Windows fakes whenever AltGr is pressed. Without
    /// telling it apart, AltGr on its own would read as Ctrl+Alt on every keyboard layout
    /// that has one.
    /// </summary>
    private const uint AltGrFakeCtrl = 0x21D;

    // Rooted for the life of the hook, for the same reason as the mouse hook's.
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hook = IntPtr.Zero;

    private bool _ctrlDown;
    private bool _altDown;

    /// <summary>Both keys have been down together since they were last both up.</summary>
    private bool _armed;

    /// <summary>Some other key joined in, so this is a shortcut, not the gesture.</summary>
    private bool _spoiled;

    internal CtrlAltHook()
    {
        _proc = OnKeyEvent;
    }

    /// <summary>Raised on the hook thread once Ctrl+Alt is pressed and released on its own.</summary>
    internal event Action? Pressed;

    internal bool Install()
    {
        if (_hook != IntPtr.Zero)
        {
            return true;
        }

        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);

        if (_hook == IntPtr.Zero)
        {
            Log.Warn($"SetWindowsHookEx (keyboard) failed (win32 error {Marshal.GetLastWin32Error()}).");
            return false;
        }

        Log.Info("Ctrl+Alt hook installed.");
        return true;
    }

    /// <summary>Hooks again from scratch; see <see cref="DesktopClickHook.Rearm"/>.</summary>
    internal void Rearm()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _ctrlDown = _altDown = _armed = _spoiled = false;
        Install();
    }

    private IntPtr OnKeyEvent(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var key = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
            var isUp = message is WM_KEYUP or WM_SYSKEYUP;

            if (isDown)
            {
                OnDown(key);
            }
            else if (isUp)
            {
                OnUp(key);
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void OnDown(KBDLLHOOKSTRUCT key)
    {
        if (IsCtrl(key))
        {
            _ctrlDown = true;
        }
        else if (IsAlt(key))
        {
            _altDown = true;
        }
        else if (_ctrlDown || _altDown)
        {
            // A key-up can go missing - one released on the lock screen or the Ctrl+Alt+Del
            // screen never reaches the hook - and a key that looks held forever would spoil
            // every gesture after it. Ask Windows before believing it.
            _ctrlDown &= IsHeld(VK_LCONTROL) || IsHeld(VK_RCONTROL);
            _altDown &= IsHeld(VK_LMENU) || IsHeld(VK_RMENU);
            _spoiled = _ctrlDown || _altDown;
        }

        if (_ctrlDown && _altDown)
        {
            _armed = true;
        }
    }

    private void OnUp(KBDLLHOOKSTRUCT key)
    {
        if (!IsCtrl(key) && !IsAlt(key))
        {
            return;
        }

        // The first of the two to come up completes the gesture.
        if (_armed && !_spoiled)
        {
            _armed = false;
            Log.InfoSoon("Ctrl+Alt pressed on its own -> toggling composer.");
            Pressed?.Invoke();
        }

        if (IsCtrl(key))
        {
            _ctrlDown = false;
        }
        else
        {
            _altDown = false;
        }

        // Only a clean start - both keys up - clears the slate for the next attempt.
        if (!_ctrlDown && !_altDown)
        {
            _armed = false;
            _spoiled = false;
        }
    }

    private static bool IsCtrl(KBDLLHOOKSTRUCT key) =>
        key.vkCode == VK_RCONTROL || (key.vkCode == VK_LCONTROL && key.scanCode != AltGrFakeCtrl);

    private static bool IsAlt(KBDLLHOOKSTRUCT key) =>
        key.vkCode is VK_LMENU or VK_RMENU;

    private static bool IsHeld(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        Log.Info("Ctrl+Alt hook removed.");
    }
}
