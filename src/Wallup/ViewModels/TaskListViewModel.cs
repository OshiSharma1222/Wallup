using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Wallup.Models;
using Wallup.Storage;

namespace Wallup.ViewModels;

/// <summary>
/// The task list, and the only source of truth. Chip windows are created and destroyed to
/// follow it, so adding here puts a chip on the desktop and removing here takes it away.
/// </summary>
internal sealed class TaskListViewModel
{
    private readonly TaskStore _store;
    private readonly SettingsStore _settingsStore;

    internal TaskListViewModel(TaskStore store, SettingsStore settingsStore, AppSettings settings)
    {
        _store = store;
        _settingsStore = settingsStore;
        Settings = settings;

        Tasks = new ObservableCollection<TaskItem>(_store.Load());
        Tasks.CollectionChanged += OnCollectionChanged;

        foreach (var task in Tasks)
        {
            task.PropertyChanged += OnTaskChanged;
        }
    }

    public ObservableCollection<TaskItem> Tasks { get; }

    public AppSettings Settings { get; }

    /// <summary>Creates a task at a point on the desktop, in device-independent pixels.</summary>
    public TaskItem? Add(string text, double x, double y)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var task = new TaskItem { Text = text, X = x, Y = y };
        task.PropertyChanged += OnTaskChanged;
        Tasks.Add(task);
        return task;
    }

    public void Delete(TaskItem? task)
    {
        if (task is null)
        {
            return;
        }

        task.PropertyChanged -= OnTaskChanged;
        Tasks.Remove(task);
    }

    public void ClearCompleted()
    {
        foreach (var done in Tasks.Where(t => t.IsDone).ToList())
        {
            Delete(done);
        }
    }

    /// <summary>Writes the list out. Chips call this after a drag or an edit.</summary>
    public void Persist() => _store.Save(Tasks);

    public void SaveSettings() => _settingsStore.Save(Settings);

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Persist();

    private void OnTaskChanged(object? sender, PropertyChangedEventArgs e) => Persist();
}
