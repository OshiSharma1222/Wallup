using System.IO;
using System.Text.Json;
using Wallup.Diagnostics;
using Wallup.Models;

namespace Wallup.Storage;

/// <summary>Persists <see cref="AppSettings"/> next to the task list.</summary>
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    internal SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Wallup", "settings.json");
    }

    internal AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            // An interrupted write leaves a zero-byte file, which is not valid JSON. That
            // is a fresh start, not an error worth a stack trace on every launch.
            var json = File.ReadAllText(_path);
            return string.IsNullOrWhiteSpace(json)
                ? new AppSettings()
                : JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Log.Error($"Could not read {_path}; using defaults.", ex);
            return new AppSettings();
        }
    }

    internal void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            // Same temp-then-replace dance as the task list: a crash mid-write must not
            // leave a truncated file behind.
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Log.Error($"Could not write {_path}.", ex);
        }
    }
}
