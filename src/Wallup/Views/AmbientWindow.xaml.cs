using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using Wallup.Diagnostics;
using Wallup.Interop;
using Wallup.Models;
using Wallup.ViewModels;
using static Wallup.Interop.NativeMethods;

namespace Wallup.Views;

/// <summary>
/// The wallpaper layer. Parented into WorkerW on first show so it paints underneath the
/// desktop icons and stays put when the user alt-tabs or minimises everything.
/// </summary>
internal partial class AmbientWindow : Window
{
    private readonly TaskListViewModel _viewModel;
    private IntPtr _handle = IntPtr.Zero;

    internal AmbientWindow(TaskListViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        viewModel.Settings.PropertyChanged += OnSettingsChanged;
    }

    /// <summary>Which strategy the shell probe settled on, for the diagnostics log.</summary>
    internal string AttachStrategy { get; private set; } = "not-attached";

    internal bool IsAttached { get; private set; }

    /// <summary>The attached HWND, or zero before the window has a source.</summary>
    internal IntPtr Handle => _handle;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _handle = new WindowInteropHelper(this).Handle;

        var probe = DesktopLayer.Resolve();
        AttachStrategy = probe.Strategy;
        Log.Raw(DesktopLayer.DescribeShellTree());
        Log.Info($"Shell probe: strategy={probe.Strategy} " +
                 $"progman=0x{probe.Progman.ToInt64():X} " +
                 $"defView=0x{probe.DefView.ToInt64():X} " +
                 $"workerW=0x{probe.WorkerW.ToInt64():X} " +
                 $"target=0x{probe.Target.ToInt64():X}");

        IsAttached = DesktopLayer.AttachToWallpaper(_handle, probe);

        if (!IsAttached)
        {
            // Better a visible box floating above the wallpaper than no product at all.
            Log.Warn("Falling back to an unparented bottom-most window.");
            Topmost = false;
        }

        ApplyPlacement();
    }

    /// <summary>
    /// Positions the box in physical pixels. Once we are a child of WorkerW, WPF's own
    /// Left/Top are measured against a parent it does not know about, so we drive the
    /// HWND directly instead.
    /// </summary>
    internal void ApplyPlacement()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        var settings = _viewModel.Settings;
        var desktop = DesktopBounds();

        var x = settings.AnchorX >= 0
            ? settings.AnchorX
            : Math.Max(0, desktop.Width - settings.BoxWidth - 48);

        SetWindowPos(_handle, IntPtr.Zero,
            x, settings.AnchorY, settings.BoxWidth, settings.BoxHeight,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private static RECT DesktopBounds()
    {
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero && GetWindowRect(progman, out var rect) && rect.Width > 0)
        {
            return rect;
        }

        return new RECT { Right = 1920, Bottom = 1080 };
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.AnchorX):
            case nameof(AppSettings.AnchorY):
            case nameof(AppSettings.BoxWidth):
            case nameof(AppSettings.BoxHeight):
                ApplyPlacement();
                break;

            case nameof(AppSettings.HideCompleted):
                _viewModel.RefreshView();
                break;
        }
    }
}
