using System.Collections.Specialized;
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

        foreach (var task in _viewModel.Tasks)
        {
            Add(task);
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
            Add(task);
        }

        foreach (var task in e.OldItems?.OfType<TaskItem>() ?? [])
        {
            Remove(task);
        }
    }

    private void Add(TaskItem task)
    {
        if (_chips.ContainsKey(task.Id))
        {
            return;
        }

        var chip = new ChipWindow(task);
        chip.Deleted += t => _viewModel.Delete(t);
        chip.Changed += _viewModel.Persist;

        _chips[task.Id] = chip;
        chip.Show();
    }

    private void Remove(TaskItem task)
    {
        if (!_chips.Remove(task.Id, out var chip))
        {
            return;
        }

        chip.Close();
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

        foreach (var (_, chip) in _chips)
        {
            chip.Close();
        }

        _chips.Clear();
    }
}
