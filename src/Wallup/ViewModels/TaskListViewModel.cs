using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using Wallup.Models;
using Wallup.Storage;

namespace Wallup.ViewModels;

/// <summary>
/// The single source of truth for the task list. Both the ambient wallpaper layer and the
/// interactive task box bind to this instance, which is why an edit in the box shows up on
/// the wallpaper without anything being regenerated.
/// </summary>
internal sealed class TaskListViewModel : INotifyPropertyChanged
{
    private readonly TaskStore _store;
    private readonly SettingsStore _settingsStore;
    private string _draft = string.Empty;

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

        // The wallpaper view can hide finished work; the task box always shows everything.
        VisibleTasks = new CollectionViewSource { Source = Tasks }.View;
        VisibleTasks.Filter = o => !Settings.HideCompleted || o is not TaskItem { IsDone: true };

        AddCommand = new RelayCommand(_ => Add(Draft), _ => !string.IsNullOrWhiteSpace(Draft));
        DeleteCommand = new RelayCommand(o => Delete(o as TaskItem));
        ClearCompletedCommand = new RelayCommand(_ => ClearCompleted(), _ => Tasks.Any(t => t.IsDone));
    }

    public ObservableCollection<TaskItem> Tasks { get; }

    public ICollectionView VisibleTasks { get; }

    public AppSettings Settings { get; }

    /// <summary>Text currently typed into the task box but not yet committed.</summary>
    public string Draft
    {
        get => _draft;
        set
        {
            if (_draft == value)
            {
                return;
            }

            _draft = value;
            Raise();
        }
    }

    public ICommand AddCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand ClearCompletedCommand { get; }

    public void Add(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        var task = new TaskItem { Text = text };
        task.PropertyChanged += OnTaskChanged;
        Tasks.Insert(0, task);
        Draft = string.Empty;
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

    /// <summary>Re-applies the wallpaper filter after a settings change.</summary>
    public void RefreshView() => VisibleTasks.Refresh();

    public void SaveSettings() => _settingsStore.Save(Settings);

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Persist();

    private void OnTaskChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskItem.IsDone) && Settings.HideCompleted)
        {
            VisibleTasks.Refresh();
        }

        Persist();
    }

    private void Persist() => _store.Save(Tasks);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
