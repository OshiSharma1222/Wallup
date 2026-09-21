using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wallup.ViewModels;

namespace Wallup.Views;

/// <summary>
/// The interactive half of the gesture. Appears at the cursor on a desktop right-click,
/// takes focus, and closes the moment it loses it. It is a normal top-level window - the
/// wallpaper layer cannot receive input, so editing has to happen above the icons.
/// </summary>
internal partial class TaskBoxWindow : Window
{
    private readonly TaskListViewModel _viewModel;

    internal TaskBoxWindow(TaskListViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    /// <summary>Opens the box with its top-left near a screen point given in physical pixels.</summary>
    internal void ShowAt(int screenX, int screenY)
    {
        Show();

        // Left/Top are device-independent, but the hook hands us raw pixels.
        var scale = VisualTreeHelper.GetDpi(this);
        var left = screenX / scale.DpiScaleX;
        var top = screenY / scale.DpiScaleY;

        // Keep the whole box on screen when the click lands near an edge.
        var bounds = SystemParameters.WorkArea;
        Left = Math.Clamp(left, bounds.Left, Math.Max(bounds.Left, bounds.Right - Width));
        Top = Math.Clamp(top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - ActualHeight));

        Activate();
        Input.Focus();
        Keyboard.Focus(Input);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        Hide();
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                _viewModel.Add(_viewModel.Draft);
                e.Handled = true;
                break;

            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        Hide();
        SettingsRequested?.Invoke();
    }

    internal event Action? SettingsRequested;
}
