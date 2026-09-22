using System.IO;
using System.Text.Json;
using Wallup.Diagnostics;
using Wallup.Models;

namespace Wallup.Storage;

/// <summary>
/// Persists tasks as JSON under %APPDATA%\Wallup. Writes go through a temp file and a
/// replace so a crash mid-save cannot leave the user with an empty list.
/// </summary>
internal sealed class TaskStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly object _gate = new();

    internal TaskStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Wallup", "tasks.json");
    }

    internal IReadOnlyList<TaskItem> Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return [];
                }

                // An interrupted write leaves a zero-byte file, which is not valid JSON.
                var json = File.ReadAllText(_path);
                return string.IsNullOrWhiteSpace(json)
                    ? []
                    : JsonSerializer.Deserialize<List<TaskItem>>(json, Options) ?? [];
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                Log.Error($"Could not read {_path}; starting with an empty list.", ex);
                return [];
            }
        }
    }

    internal void Save(IEnumerable<TaskItem> tasks)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

                var temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(tasks, Options));
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                Log.Error($"Could not write {_path}; this session's changes are not persisted.", ex);
            }
        }
    }
}
