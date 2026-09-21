using System.IO;

namespace Wallup.Diagnostics;

/// <summary>
/// Append-only text log under %LOCALAPPDATA%\Wallup\logs. The interop layer fails in ways
/// that are invisible on screen - a window parented into the wrong place simply never
/// appears - so every probe result goes here.
/// </summary>
internal static class Log
{
    private static readonly object Gate = new();

    internal static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Wallup", "logs");

    internal static string FilePath { get; } = Path.Combine(Directory, "wallup.log");

    internal static void Info(string message) => Write("INFO ", message);

    internal static void Warn(string message) => Write("WARN ", message);

    internal static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    /// <summary>Writes a block verbatim, for multi-line dumps like the shell tree.</summary>
    internal static void Raw(string block)
    {
        lock (Gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(FilePath, block + Environment.NewLine);
            }
            catch (IOException)
            {
                // Logging must never take the app down with it.
            }
        }
    }

    private static void Write(string level, string message) =>
        Raw($"{DateTime.Now:HH:mm:ss.fff} {level} {message}");
}
