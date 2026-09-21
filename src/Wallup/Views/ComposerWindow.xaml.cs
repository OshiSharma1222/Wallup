using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wallup.Interop;

namespace Wallup.Views;

/// <summary>
/// The transient box the right-click gesture opens. Takes one line of text and gets out of
/// the way - it is not where tasks live, it is only where they are born.
/// </summary>
internal partial class ComposerWindow : Window
{
    /// <summary>
    /// Deactivation right after opening is not the user dismissing us. The shell often
    /// takes focus back once during the show, and hiding on that makes the window flash
    /// and vanish.
    /// </summary>
    private DateTime _shownAt = DateTime.MinValue;

    internal ComposerWindow()
    {
        InitializeComponent();
    }

    /// <summary>Raised with the task text and the desktop point it should land on.</summary>
    internal event Action<string, Point>? Committed;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        AdaptiveGlass.Apply(this);
    }

    /// <summary>Opens the composer at a screen point given in physical pixels.</summary>
    internal void ShowAt(int screenX, int screenY)
    {
        Input.Clear();
        Show();

        var scale = VisualTreeHelper.GetDpi(this);
        var left = screenX / scale.DpiScaleX;
        var top = screenY / scale.DpiScaleY;

        // Keep the whole box on screen when the click lands near an edge.
        var bounds = SystemParameters.WorkArea;
        Left = Math.Clamp(left, bounds.Left, Math.Max(bounds.Left, bounds.Right - Width));
        Top = Math.Clamp(top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - ActualHeight));

        _shownAt = DateTime.Now;

        // OnSourceInitialized ran before this move, so it sampled the wallpaper under the
        // window's default spot. Now that it is where the click was, ask again.
        AdaptiveGlass.Apply(this);

        Activate();
        Input.Focus();
        Keyboard.Focus(Input);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        if (DateTime.Now - _shownAt < TimeSpan.FromMilliseconds(400))
        {
            // Still settling after the show; the user has not dismissed anything yet.
            Dispatcher.BeginInvoke(() =>
            {
                if (IsVisible)
                {
                    Activate();
                    Keyboard.Focus(Input);
                }
            });
            return;
        }

        Hide();
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                var text = Input.Text.Trim();
                if (text.Length > 0)
                {
                    Committed?.Invoke(text, new Point(Left, Top));
                }

                Input.Clear();
                Hide();
                e.Handled = true;
                break;

            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
        }
    }
}
