using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Wallup.Models;

/// <summary>
/// User-facing customization. Deliberately small for v0.1 - opacity, type size, and where
/// the box sits - because the point of the product is that these are reachable from a
/// settings panel rather than a config file. Change notification is what lets the settings
/// panel update the wallpaper live while you drag a slider.
/// </summary>
internal sealed class AppSettings : INotifyPropertyChanged
{
    private double _opacity = 0.92;
    private double _fontSize = 15;
    private int _anchorX = -1; // -1 means "work it out from the screen size"
    private int _anchorY = 64;
    private int _boxWidth = 380;
    private int _boxHeight = 520;
    private bool _hideCompleted;

    /// <summary>Opacity of the ambient layer.</summary>
    public double Opacity
    {
        get => _opacity;
        set => Set(ref _opacity, Math.Clamp(value, 0.15, 1.0));
    }

    /// <summary>Base type size for task text, in device-independent pixels.</summary>
    public double FontSize
    {
        get => _fontSize;
        set => Set(ref _fontSize, Math.Clamp(value, 9, 48));
    }

    /// <summary>Ambient box position in physical pixels, relative to the desktop origin.</summary>
    public int AnchorX
    {
        get => _anchorX;
        set => Set(ref _anchorX, value);
    }

    public int AnchorY
    {
        get => _anchorY;
        set => Set(ref _anchorY, value);
    }

    public int BoxWidth
    {
        get => _boxWidth;
        set => Set(ref _boxWidth, Math.Clamp(value, 220, 1200));
    }

    public int BoxHeight
    {
        get => _boxHeight;
        set => Set(ref _boxHeight, Math.Clamp(value, 160, 2000));
    }

    /// <summary>Hide finished tasks from the wallpaper instead of striking them through.</summary>
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
