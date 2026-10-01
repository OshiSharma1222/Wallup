using Microsoft.Win32;
using Wallup.Diagnostics;

namespace Wallup.Storage;

/// <summary>
/// Whether Wallup starts with Windows: a value under the per-user Run key. The registry
/// is the only record of it, not settings.json, so the checkbox and Task Manager's
/// startup list can never disagree.
///
/// Task Manager does not delete the Run value when you disable an app there. It leaves
/// it and records the choice under StartupApproved, where an odd first byte means off.
/// Reading only the Run key would show the box ticked for an app that no longer starts.
/// </summary>
internal static class StartupEntry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "Wallup";

    /// <summary>Passed by the Run entry, so a start at sign-in stays in the tray.</summary>
    internal const string StartupArg = "--startup";

    private static string Command => $"\"{Environment.ProcessPath}\" {StartupArg}";

    internal static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(ValueName) is not string)
            {
                return false;
            }

            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
        }
        set
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value)
                {
                    key.SetValue(ValueName, Command);
                }
                else
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }

                // Either way, Task Manager's old verdict no longer applies: ticking the
                // box must actually turn it on.
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
                approved?.DeleteValue(ValueName, throwOnMissingValue: false);

                Log.Info($"Start with Windows {(value ? "on" : "off")}.");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                Log.Error("Could not change the startup entry.", ex);
            }
        }
    }

    /// <summary>
    /// Points an existing entry at this copy of Wallup. Moving or rebuilding the app
    /// would otherwise leave Windows launching a path that no longer exists. Only the
    /// path changes: an entry switched off in Task Manager stays off.
    /// </summary>
    internal static void RefreshPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is string current && current != Command)
            {
                key.SetValue(ValueName, Command);
                Log.Info($"Startup entry moved from {current} to {Command}.");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Error("Could not update the startup entry.", ex);
        }
    }
}
