using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Wallup.Models;

/// <summary>
/// User-facing customization, reachable from a settings panel rather than a config file.
/// Position is no longer here: every chip carries its own, because you place them by
/// dragging rather than by typing coordinates.
/// </summary>
internal sealed class AppSettings : INotifyPropertyChanged
{
    private double _opacity = 1.0;
    private double _fontSize = 14;
    private double _chipWidth = 240;
    private bool _hideCompleted;

    /// <summary>Opacity of each chip on the desktop.</summary>
    public double Opacity
    {
        get => _opacity;
        set => Set(ref _opacity, Math.Clamp(value, 0.25, 1.0));
    }

    /// <summary>Task text size, in device-independent pixels.</summary>
    public double FontSize
    {
        get => _fontSize;
        set => Set(ref _fontSize, Math.Clamp(value, 9, 32));
    }

    public double ChipWidth
    {
        get => _chipWidth;
        set => Set(ref _chipWidth, Math.Clamp(value, 140, 520));
    }

    /// <summary>Take finished tasks off the desktop instead of striking them through.</summary>
    public bool HideCompleted
    {
        get => _hideCompleted;
        set => Set(ref _hideCompleted, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
