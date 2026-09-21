using System.Runtime.InteropServices;
using System.Text;
using Wallup.Diagnostics;
using static Wallup.Interop.NativeMethods;

namespace Wallup.Interop;

/// <summary>
/// Finds a place in the shell window tree where we can paint on the wallpaper, behind the
/// desktop icons. Two layouts exist in the wild and we support both.
///
/// Classic (Windows 7 - Windows 10): the 0x052C message splits the desktop into two
/// top-level windows.
///
///   WorkerW          hosts SHELLDLL_DefView (the icons)
///   WorkerW          empty; this is the wallpaper layer, parent into it
///   Progman
///
/// Modern (Windows 11, confirmed on build 26200): no split happens at all. Progman keeps
/// the icons and grows a wallpaper child of its own.
///
///   Progman
///     +-- SHELLDLL_DefView   the icons
///     +-- WorkerW            the wallpaper
///
/// In the modern layout we parent into Progman and then insert ourselves directly behind
/// SHELLDLL_DefView, which puts us above the wallpaper and below the icons.
/// </summary>
internal static class DesktopLayer
{
    /// <summary>What the shell looks like right now, and where we decided to attach.</summary>
    internal sealed record Probe(
        IntPtr Progman,
        IntPtr DefView,
        IntPtr WorkerW,
        IntPtr Target,
        IntPtr InsertAfter,
        string Strategy)
    {
        internal bool CanAttach => Target != IntPtr.Zero;
    }

    internal static Probe Resolve()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            Log.Warn("Progman not found - the shell is not running as expected.");
            return new Probe(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, "no-shell");
        }

        SpawnWorkerW(progman);

        // Classic layout: a top-level WorkerW that does not host the icons.
        var (defViewOwner, defView) = FindDefView();
        var topLevelWorkerW = FindEmptyWorkerWAfter(defViewOwner);

        if (topLevelWorkerW != IntPtr.Zero)
        {
            // That window already sits behind the icons, so z-order needs no further help.
            return new Probe(progman, defView, topLevelWorkerW, topLevelWorkerW, IntPtr.Zero, "workerw");
        }

        // Modern layout: the icons and the wallpaper are both children of Progman.
        if (defViewOwner == progman)
        {
            var wallpaperChild = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
            return new Probe(progman, defView, wallpaperChild, progman, defView, "progman-child");
        }

        Log.Warn("Could not resolve any wallpaper host window.");
        return new Probe(progman, defView, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, "unresolved");
    }

    /// <summary>
    /// Sends the undocumented Progman message that asks the shell to split the desktop.
    /// The payload differs between shell generations, so we try the known variants in turn.
    /// On Windows 11 none of them split anything, which is expected and not an error.
    /// </summary>
    private static void SpawnWorkerW(IntPtr progman)
    {
        (IntPtr W, IntPtr L)[] variants =
        [
            (IntPtr.Zero, IntPtr.Zero),           // classic Win7 - Win10
            (new IntPtr(0x0D), new IntPtr(0x01)), // seen on later Win10 shells
            (new IntPtr(0x0D), IntPtr.Zero),
        ];

        foreach (var (w, l) in variants)
        {
            SendMessageTimeout(progman, WM_SPAWN_WORKER, w, l, SMTO_NORMAL, 1000, out _);

            var (owner, _) = FindDefView();
            if (FindEmptyWorkerWAfter(owner) != IntPtr.Zero)
            {
                Log.Info($"WorkerW split succeeded with wParam=0x{w.ToInt64():X}, lParam=0x{l.ToInt64():X}.");
                return;
            }
        }

        Log.Info("No top-level WorkerW after any spawn variant; assuming the modern layout.");
    }

    /// <summary>Finds the icon view and whichever top-level window owns it.</summary>
    private static (IntPtr Owner, IntPtr DefView) FindDefView()
    {
        var result = (Owner: IntPtr.Zero, DefView: IntPtr.Zero);

        EnumWindows((hWnd, _) =>
        {
            var defView = FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero)
            {
                return true;
            }

            result = (hWnd, defView);
            return false;
        }, IntPtr.Zero);

        return result;
    }

    /// <summary>
    /// Walks z-order forward from the icon host and returns the first WorkerW that does
    /// not itself contain the icon view. Never index into the WorkerW list positionally;
    /// the count and ordering differ between builds.
    /// </summary>
    private static IntPtr FindEmptyWorkerWAfter(IntPtr defViewOwner)
    {
        if (defViewOwner == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        for (var hWnd = GetWindow(defViewOwner, GW_HWNDNEXT);
             hWnd != IntPtr.Zero;
             hWnd = GetWindow(hWnd, GW_HWNDNEXT))
        {
            if (ClassNameOf(hWnd) == "WorkerW" &&
                FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
            {
                return hWnd;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>Parents a window into the wallpaper layer so it paints behind the icons.</summary>
    internal static bool AttachToWallpaper(IntPtr child, Probe probe)
    {
        if (child == IntPtr.Zero || !probe.CanAttach)
        {
            return false;
        }

        // A no-activate tool window keeps us out of Alt+Tab and stops us stealing focus.
        var exStyle = GetWindowLong(child, GWL_EXSTYLE);
        SetWindowLong(child, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        // SetParent returns the previous parent, which is legitimately NULL for a
        // top-level window, so its return value cannot tell us whether it worked. And
        // GetParent reports the owner for windows without WS_CHILD, so it reports NULL
        // too. GetAncestor(GA_PARENT) is the only call that gives a straight answer.
        SetParent(child, probe.Target);
        var error = Marshal.GetLastWin32Error();
        var parent = ParentOf(child);

        Log.Info($"SetParent(0x{child.ToInt64():X}, 0x{probe.Target.ToInt64():X}) -> " +
                 $"parent is now 0x{parent.ToInt64():X} (win32 error {error}).");

        if (parent != probe.Target)
        {
            Log.Warn("Reparent did not stick.");
            return false;
        }

        // In the modern layout we land on top of our new siblings, which means on top of
        // the icons. Drop in directly behind the icon view instead.
        if (probe.InsertAfter != IntPtr.Zero)
        {
            SetWindowPos(child, probe.InsertAfter, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            Log.Info($"Inserted behind the icon view (0x{probe.InsertAfter.ToInt64():X}).");
        }

        return true;
    }

    /// <summary>Dumps the top-level shell windows. Used by <c>--diagnose</c>.</summary>
    internal static string DescribeShellTree()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Top-level shell windows (z-order, topmost first):");

        EnumWindows((hWnd, _) =>
        {
            var cls = ClassNameOf(hWnd);
            if (cls is not ("Progman" or "WorkerW"))
            {
                return true;
            }

            var defView = FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            sb.AppendLine(
                $"  0x{hWnd.ToInt64():X8} {cls,-8} visible={IsWindowVisible(hWnd),-5} " +
                $"defView={(defView != IntPtr.Zero ? $"0x{defView.ToInt64():X8}" : "-"),-10} " +
                $"title=\"{TitleOf(hWnd)}\"");

            return true;
        }, IntPtr.Zero);

        return sb.ToString();
    }

    /// <summary>
    /// Reports where an attached window ended up relative to the desktop icons. Attaching
    /// can succeed while still leaving us painted on the wrong side of SHELLDLL_DefView,
    /// and that difference is invisible from inside the process.
    /// </summary>
    internal static string DescribeAttachment(IntPtr child)
    {
        var sb = new StringBuilder();
        var parent = ParentOf(child);

        sb.AppendLine($"Attached window : 0x{child.ToInt64():X8}");
        sb.AppendLine($"Parent          : 0x{parent.ToInt64():X8} ({ClassNameOf(parent)})");

        if (parent == IntPtr.Zero)
        {
            sb.AppendLine("Verdict         : FAIL - not parented into the shell at all.");
            return sb.ToString();
        }

        // EnumChildWindows walks topmost first, so a larger index means further back.
        var siblings = new List<IntPtr>();
        EnumChildWindows(parent, (hWnd, _) =>
        {
            if (ParentOf(hWnd) == parent)
            {
                siblings.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);

        var ourIndex = siblings.IndexOf(child);
        var iconIndex = siblings.FindIndex(h => ClassNameOf(h) == "SHELLDLL_DefView");

        sb.AppendLine($"Siblings        : {siblings.Count} (z-order, topmost first)");

        for (var i = 0; i < siblings.Count; i++)
        {
            GetWindowRect(siblings[i], out var rect);
            var marker = siblings[i] == child ? "  <-- us" : string.Empty;
            sb.AppendLine(
                $"  [{i}] 0x{siblings[i].ToInt64():X8} {ClassNameOf(siblings[i]),-26} " +
                $"visible={IsWindowVisible(siblings[i]),-5} " +
                $"{rect.Width}x{rect.Height} @ {rect.Left},{rect.Top}{marker}");
        }

        sb.AppendLine($"Verdict         : {AttachmentVerdict(ourIndex, iconIndex)}");
        return sb.ToString();
    }

    private static string AttachmentVerdict(int ourIndex, int iconIndex) => (ourIndex, iconIndex) switch
    {
        (< 0, _) => "FAIL - our window is not a child of the host we attached to.",
        (_, < 0) => "PARTIAL - on the wallpaper layer, but the icon view is not a sibling.",
        var (us, icons) when us > icons => "OK - behind the desktop icons.",
        _ => "IN FRONT - on the wallpaper layer but painted over the icons.",
    };
}
