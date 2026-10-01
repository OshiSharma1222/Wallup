using Wallup.Interop;

namespace Wallup.Diagnostics;

/// <summary>
/// Headless shell probe behind <c>Wallup.exe --diagnose</c>. The WorkerW layout differs
/// between Windows builds, so before debugging an invisible window it is worth confirming
/// what the shell actually looks like on this machine.
/// </summary>
internal static class DesktopProbe
{
    internal static int Run()
    {
        NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);

        var probe = DesktopLayer.Resolve();
        var tree = DesktopLayer.DescribeShellTree();

        var report = $"""
            Wallup desktop probe
            OS            : {Environment.OSVersion.VersionString}
            Progman       : 0x{probe.Progman.ToInt64():X8}
            Icon view     : 0x{probe.DefView.ToInt64():X8}
            WorkerW       : 0x{probe.WorkerW.ToInt64():X8}
            Target        : 0x{probe.Target.ToInt64():X8}
            Strategy      : {probe.Strategy}
            Icon hit-test : {IconCheck(probe.DefView)}

            {tree}
            Verdict       : {Verdict(probe)}
            """;

        Console.WriteLine(report);
        Log.Raw(report);

        return probe.Target == IntPtr.Zero ? 1 : 0;
    }

    /// <summary>
    /// Hit-tests the centre of every desktop icon, which is the check the double
    /// right-click relies on to leave icons alone. Every one should come back as an icon.
    /// </summary>
    private static string IconCheck(IntPtr defView)
    {
        var listView = NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        if (listView == IntPtr.Zero)
        {
            return "no icon view (desktop icons hidden?)";
        }

        var centres = DesktopIcons.IconCentres(listView);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var hits = centres.Count(c => DesktopIcons.IsIconAt(listView, c));

        // The other half: a hit-test that said "icon" everywhere would pass the first.
        // Spots well clear of every icon must come back empty.
        var empty = EmptySpots(listView, centres);
        var misses = empty.Count(p => !DesktopIcons.IsIconAt(listView, p));

        // It runs inside the mouse hook, so how long it takes is part of whether it works.
        var each = clock.Elapsed.TotalMilliseconds / Math.Max(1, centres.Count + empty.Count);

        return $"{hits}/{centres.Count} icons found, {misses}/{empty.Count} empty spots left alone, " +
               $"{each:0.00} ms each";
    }

    /// <summary>Points on a coarse grid over the desktop that are nowhere near an icon.</summary>
    private static List<NativeMethods.POINT> EmptySpots(IntPtr listView, IReadOnlyList<NativeMethods.POINT> icons)
    {
        const int Step = 160;
        const int Clearance = 150;

        NativeMethods.GetWindowRect(listView, out var bounds);
        var spots = new List<NativeMethods.POINT>();

        for (var y = bounds.Top + Step / 2; y < bounds.Bottom; y += Step)
        {
            for (var x = bounds.Left + Step / 2; x < bounds.Right; x += Step)
            {
                if (icons.All(i => Math.Abs(i.X - x) > Clearance || Math.Abs(i.Y - y) > Clearance))
                {
                    spots.Add(new NativeMethods.POINT { X = x, Y = y });
                }
            }
        }

        return spots;
    }

    private static string Verdict(DesktopLayer.Probe probe) => probe.Strategy switch
    {
        "workerw" => "OK - classic layout; a dedicated top-level WorkerW is available.",
        "progman-child" => "OK - modern layout; parenting into Progman behind the icon view.",
        _ => "FAIL - no paintable wallpaper host on this build.",
    };
}
