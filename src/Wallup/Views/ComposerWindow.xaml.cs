using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Wallup.Interop;

namespace Wallup.Views;

/// <summary>
/// The transient box the desktop click opens. Takes one line of text and gets out of the
/// way - it is not where tasks live, it is only where they are born.
/// </summary>
internal partial class ComposerWindow : Window
{
    /// <summary>Room for the cast shadow, matching the root margin in the XAML.</summary>
    private const double Halo = 14;

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

        // Build the window before showing it, so it has a DPI to convert with and can be
        // put in the right place on its first frame instead of jumping there afterwards.
        new WindowInteropHelper(this).EnsureHandle();

        var scale = VisualTreeHelper.GetDpi(this);
        MoveTo(screenX / scale.DpiScaleX - Halo, screenY / scale.DpiScaleY - Halo);

        Show();

        // SizeToContent has measured by now, so the bottom edge can be checked properly.
        MoveTo(Left, Top);

        _shownAt = DateTime.Now;

        // The handle existed before the move, so the first sample read the wallpaper under
        // the old spot. Now that it is where the click was, ask again.
        AdaptiveGlass.Apply(this);

        ForegroundWindow.Take(this);
        Input.Focus();
        Keyboard.Focus(Input);
    }

    /// <summary>Places the window, keeping the whole box on screen near an edge.</summary>
    private void MoveTo(double left, double top)
    {
        var bounds = SystemParameters.WorkArea;
        var height = ActualHeight > 0 ? ActualHeight : 110;

        Left = Math.Clamp(left, bounds.Left, Math.Max(bounds.Left, bounds.Right - Width));
        Top = Math.Clamp(top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - height));
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
                    ForegroundWindow.Take(this);
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
                DropTask();
                e.Handled = true;
                break;

            case Key.Escape:
                Cancel();
                e.Handled = true;
                break;
        }
    }

    /// <summary>A double right-click anywhere on the box cancels it, like Escape.</summary>
    private void OnRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            Cancel();
        }

        e.Handled = true;
    }

    /// <summary>Closes the box without making a task.</summary>
    internal void Cancel() => Hide();

    /// <summary>Hands the typed task over to become a chip, and closes the box.</summary>
    private void DropTask()
    {
        var text = Input.Text.Trim();
        if (text.Length > 0)
        {
            // The task lands where the glass was, not where the window was: the halo
            // around it is empty space.
            Committed?.Invoke(text, new Point(Left + Halo, Top + Halo));
        }

        Input.Clear();
        Hide();
    }
}
