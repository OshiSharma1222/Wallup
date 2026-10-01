using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
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

        // A finished task is kept for the rest of its day, so the window can show what got
        // done today. After that it has served its purpose and goes.
        var today = DateTimeOffset.Now.Date;
        Tasks = new ObservableCollection<TaskItem>(
            _store.Load().Where(t => !t.IsDone || t.CompletedAt?.Date >= today));
        Tasks.CollectionChanged += OnCollectionChanged;

        foreach (var task in Tasks)
        {
            task.PropertyChanged += OnTaskChanged;
        }

        ToDo = LiveView(t => !t.IsDone);
        DoneToday = LiveView(t => t.IsDone && t.CompletedAt?.Date == DateTimeOffset.Now.Date);
    }

    public ObservableCollection<TaskItem> Tasks { get; }

    public AppSettings Settings { get; }

    /// <summary>Kept in the registry rather than in <see cref="Settings"/>; see <see cref="StartupEntry"/>.</summary>
    public bool StartWithWindows
    {
        get => StartupEntry.IsEnabled;
        set => StartupEntry.IsEnabled = value;
    }

    /// <summary>Everything not yet ticked, whichever day it was made.</summary>
    public ListCollectionView ToDo { get; }

    /// <summary>What was ticked today. It left the desktop, but it still counts.</summary>
    public ListCollectionView DoneToday { get; }

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

    /// <summary>
    /// Brings both lists up to date with the clock. Ticking a task moves it across on its
    /// own; this is for midnight, when yesterday's finished tasks stop being today's.
    /// </summary>
    public void RefreshDay() => DoneToday.Refresh();

    /// <summary>Writes the list out. Chips call this after a drag or an edit.</summary>
    public void Persist() => _store.Save(Tasks);

    public void SaveSettings() => _settingsStore.Save(Settings);

    /// <summary>A filtered view that moves a task across the moment it is ticked or unticked.</summary>
    private ListCollectionView LiveView(Predicate<TaskItem> keep)
    {
        var view = new ListCollectionView(Tasks)
        {
            Filter = o => keep((TaskItem)o),
            IsLiveFiltering = true,
        };
        view.LiveFilteringProperties.Add(nameof(TaskItem.IsDone));
        view.SortDescriptions.Add(new SortDescription(nameof(TaskItem.CreatedAt), ListSortDirection.Ascending));
        return view;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Persist();

    private void OnTaskChanged(object? sender, PropertyChangedEventArgs e) => Persist();
}
