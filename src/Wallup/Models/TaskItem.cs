using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Wallup.Models;

/// <summary>A single line on the wallpaper.</summary>
internal sealed class TaskItem : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private bool _isDone;

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
            if (Set(ref _isDone, value))
            {
                CompletedAt = value ? DateTimeOffset.Now : null;
            }
        }
    }

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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
