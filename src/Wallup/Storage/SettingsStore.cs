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

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
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
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Log.Error($"Could not write {_path}.", ex);
        }
    }
}
