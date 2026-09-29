using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Wallup.Models;
using Wallup.ViewModels;

namespace Wallup.Views;

/// <summary>
/// Keeps one <see cref="ChipWindow"/> alive per task and watches for alarms coming due.
/// The task list is the source of truth; windows are just its shadow on the desktop.
/// </summary>
internal sealed class ChipHost : IDisposable
{
    private readonly TaskListViewModel _viewModel;
    private readonly Dictionary<Guid, ChipWindow> _chips = [];
    private readonly DispatcherTimer _alarmTimer;

    internal ChipHost(TaskListViewModel viewModel)
    {
        _viewModel = viewModel;
        _viewModel.Tasks.CollectionChanged += OnTasksChanged;
        _viewModel.Settings.PropertyChanged += OnSettingsChanged;

        foreach (var task in _viewModel.Tasks)
        {
            Watch(task);
        }

        _alarmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _alarmTimer.Tick += CheckAlarms;
        _alarmTimer.Start();
    }

    /// <summary>Raised when an alarm comes due, so the tray can say something.</summary>
    internal event Action<TaskItem>? AlarmDue;

    private void OnTasksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var task in e.NewItems?.OfType<TaskItem>() ?? [])
        {
            Watch(task);
        }

        foreach (var task in e.OldItems?.OfType<TaskItem>() ?? [])
        {
            Unwatch(task);
        }
    }

    /// <summary>
    /// Every task is watched, finished ones too: unticking one in the window is what
    /// brings its chip back. Only unfinished tasks have a chip.
    /// </summary>
    private void Watch(TaskItem task)
    {
        task.PropertyChanged += OnTaskChanged;

        if (!task.IsDone)
        {
            ShowChip(task);
        }
    }

    private void Unwatch(TaskItem task)
    {
        task.PropertyChanged -= OnTaskChanged;
        HideChip(task);
    }

    private void ShowChip(TaskItem task)
    {
        if (_chips.ContainsKey(task.Id))
        {
            return;
        }

        var chip = new ChipWindow(task, _viewModel.Settings);
        chip.Deleted += t => _viewModel.Delete(t);
        chip.Changed += _viewModel.Persist;

        _chips[task.Id] = chip;
        chip.Show();
    }

    private void HideChip(TaskItem task)
    {
        if (_chips.Remove(task.Id, out var chip))
        {
            chip.Close();
        }
    }

    /// <summary>
    /// A ticked task has done its job, so its chip drops off the desktop. The task itself
    /// stays in the list for the rest of the day, under done in the window.
    /// </summary>
    private void OnTaskChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TaskItem.IsDone) || sender is not TaskItem task)
        {
            return;
        }

        _chips.TryGetValue(task.Id, out var chip);

        if (!task.IsDone)
        {
            // Unticked mid-fade keeps the chip; unticked from the window brings it back.
            if (chip is not null)
            {
                chip.CancelFade();
            }
            else
            {
                ShowChip(task);
            }

            return;
        }

        // Checked again here because unticking during the fade takes the task back.
        chip?.FadeOut(() =>
        {
            if (task.IsDone)
            {
                HideChip(task);
            }
        });
    }

    /// <summary>A slider moved in the settings panel, so every chip follows it live.</summary>
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var (_, chip) in _chips)
        {
            chip.ApplySettings(_viewModel.Settings);
        }
    }

    private void CheckAlarms(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.Now;

        foreach (var task in _viewModel.Tasks.ToList())
        {
            if (task.AlarmAt is not { } due || task.HasFired || due > now || task.IsDone)
            {
                continue;
            }

            task.HasFired = true;

            if (_chips.TryGetValue(task.Id, out var chip))
            {
                chip.Announce();
            }

            AlarmDue?.Invoke(task);
        }
    }

    /// <summary>Drops every chip back onto the visible work area, for a resolution change.</summary>
    internal void ReflowOntoScreen()
    {
        var bounds = SystemParameters.WorkArea;

        foreach (var (_, chip) in _chips)
        {
            chip.Left = Math.Clamp(chip.Left, bounds.Left, Math.Max(bounds.Left, bounds.Right - chip.Width));
            chip.Top = Math.Clamp(chip.Top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - chip.ActualHeight));
        }
    }

    public void Dispose()
    {
        _alarmTimer.Stop();
        _viewModel.Tasks.CollectionChanged -= OnTasksChanged;
        _viewModel.Settings.PropertyChanged -= OnSettingsChanged;

        foreach (var (_, chip) in _chips)
        {
            chip.Close();
        }

        _chips.Clear();
    }
}
