using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Wallup.Models;

/// <summary>
/// A single task. Each one owns its position on the desktop, because a task is its own
/// little window rather than a line in a shared list.
/// </summary>
internal sealed class TaskItem : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private bool _isDone;
    private double _x;
    private double _y;
    private DateTimeOffset? _alarmAt;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    public bool IsDone
    {
        get => _isDone;
        set
        {
            if (_isDone == value)
            {
                return;
            }

            // Stamp the time *before* announcing the change. Saving is driven by
            // PropertyChanged, so setting it afterwards wrote a finished task with no
            // finish time and only corrected itself on the next save.
            _isDone = value;
            CompletedAt = value ? DateTimeOffset.Now : null;
            Raise(nameof(IsDone));
        }
    }

    /// <summary>Where the chip sits, in device-independent pixels from the desktop origin.</summary>
    public double X
    {
        get => _x;
        set => Set(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => Set(ref _y, value);
    }

    /// <summary>When set, the chip announces itself at this time and highlights.</summary>
    public DateTimeOffset? AlarmAt
    {
        get => _alarmAt;
        set
        {
            if (Set(ref _alarmAt, value))
            {
                HasFired = false;
                Raise(nameof(HasAlarm));
            }
        }
    }

    public bool HasAlarm => AlarmAt is not null;

    /// <summary>Stops one alarm being announced on every timer tick after it comes due.</summary>
    public bool HasFired { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public DateTimeOffset? CompletedAt { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }

    private void Raise(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
