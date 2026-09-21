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

            {tree}
            Verdict       : {Verdict(probe)}
            """;

        Console.WriteLine(report);
        Log.Raw(report);

        return probe.Target == IntPtr.Zero ? 1 : 0;
    }

    private static string Verdict(DesktopLayer.Probe probe) => probe.Strategy switch
    {
        "workerw" => "OK - classic layout; a dedicated top-level WorkerW is available.",
        "progman-child" => "OK - modern layout; parenting into Progman behind the icon view.",
        _ => "FAIL - no paintable wallpaper host on this build.",
    };
}
