using System.Windows;
using System.Windows.Input;
using Wallup.Interop;
using Wallup.ViewModels;

namespace Wallup.Views;

internal partial class SettingsWindow : Window
{
    private readonly TaskListViewModel _viewModel;

    internal SettingsWindow(TaskListViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // The only window that floats over other apps, so the only one that wants the
        // system's blur rather than a refracted wallpaper.
        AdaptiveGlass.Apply(this, acrylic: true);
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnDoneClick(object sender, RoutedEventArgs e) => Hide();

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        _viewModel.SaveSettings();
    }
}
