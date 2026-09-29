using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Wallup.Models;

/// <summary>What opens the box for a new task.</summary>
internal enum OpenGesture
{
    /// <summary>A double right-click on empty desktop, opening the box where you clicked.</summary>
    RightClick,

    /// <summary>Ctrl and Alt pressed together on their own, opening the box at the pointer.</summary>
    CtrlAlt,

    Both,
}

/// <summary>
/// User-facing customization, reachable from a settings panel rather than a config file.
/// Position is no longer here: every chip carries its own, because you place them by
/// dragging rather than by typing coordinates.
/// </summary>
internal sealed class AppSettings : INotifyPropertyChanged
{
    private double _opacity = 1.0;
    private double _fontSize = 14;
    private double _chipWidth = 300;
    private OpenGesture _openWith = OpenGesture.Both;

    /// <summary>What opens the box for a new task.</summary>
    public OpenGesture OpenWith
    {
        get => _openWith;
        set => Set(ref _openWith, value);
    }

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

    /// <summary>Width of the glass you can see, not counting the room for its shadow.</summary>
    public double ChipWidth
    {
        get => _chipWidth;
        set => Set(ref _chipWidth, Math.Clamp(value, 140, 520));
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
