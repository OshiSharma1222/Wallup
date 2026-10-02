using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Wallup.Interop;

namespace Wallup.Views;

/// <summary>
/// The transient box the gestures open. Takes one line of text and gets out of the way -
/// it is not where tasks live, it is only where they are born.
///
/// It is a Flow bar in the manner of Wispr Flow: always in the same place, centred just
/// above the taskbar, growing out of a sliver when it opens. The task still lands where
/// the gesture happened - the double right-click, or the pointer for Ctrl+Alt - so the bar
/// only borrows the bottom of the screen for as long as you are typing.
/// </summary>
internal partial class ComposerWindow : Window
{
    private const double OpenWidth = 460;
    private const double OpenHeight = 46;
    private const double ClosedWidth = 56;
    private const double ClosedHeight = 10;

    /// <summary>Room a new chip needs, so one dropped by a screen edge lands on screen.</summary>
    private const double ChipRoomWidth = 300;
    private const double ChipRoomHeight = 64;

    /// <summary>
    /// Deactivation right after opening is not the user dismissing us. The shell often
    /// takes focus back once during the show, and hiding on that makes the window flash
    /// and vanish.
    /// </summary>
    private DateTime _shownAt = DateTime.MinValue;

    /// <summary>Where the gesture happened, in physical pixels. The task lands here.</summary>
    private System.Drawing.Point _dropAt;

    /// <summary>Shrinking away; nothing typed now should become a task.</summary>
    private bool _closing;

    private readonly Rectangle[] _bars;
    private readonly DispatcherTimer _wave;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>How hard the waveform is moving, kicked by each keystroke and left to settle.</summary>
    private double _energy;

    internal ComposerWindow()
    {
        InitializeComponent();

        _bars = Wave.Children.OfType<Rectangle>().ToArray();
        _wave = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _wave.Tick += (_, _) => AnimateWave();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _wave.Stop();
            }
        };
    }

    /// <summary>Raised with the task text and the desktop point it should land on.</summary>
    internal event Action<string, Point>? Committed;

    /// <summary>Opens the bar for a task that will land at a screen point given in physical pixels.</summary>
    internal void ShowAt(int screenX, int screenY)
    {
        _closing = false;
        _dropAt = new System.Drawing.Point(screenX, screenY);
        Input.Clear();

        // Build the window before showing it, so it has a DPI to convert with and can be
        // put in the right place on its first frame instead of jumping there afterwards.
        new WindowInteropHelper(this).EnsureHandle();
        PlaceAboveTaskbar();

        Show();

        // Shown, it has the DPI of the monitor it is really on.
        PlaceAboveTaskbar();

        _shownAt = DateTime.Now;
        Expand();

        ForegroundWindow.Take(this);
        Input.Focus();
        Keyboard.Focus(Input);
    }

    /// <summary>Centres the bar just above the taskbar of the monitor the gesture was on.</summary>
    private void PlaceAboveTaskbar()
    {
        var scale = VisualTreeHelper.GetDpi(this);
        var area = System.Windows.Forms.Screen.FromPoint(_dropAt).WorkingArea;

        Left = (area.Left + area.Width / 2.0) / scale.DpiScaleX - Width / 2;
        Top = area.Bottom / scale.DpiScaleY - Height;
    }

    private void Expand()
    {
        // A touch of overshoot on the width is what makes it feel sprung rather than slid.
        Pill.BeginAnimation(WidthProperty, new DoubleAnimation(OpenWidth, Ms(300))
        {
            EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut },
        });
        Pill.BeginAnimation(HeightProperty, new DoubleAnimation(OpenHeight, Ms(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        Face.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Ms(160)) { BeginTime = Ms(110) });

        _energy = 0.6;
        _wave.Start();
    }

    /// <summary>Shrinks back into a sliver, then hides.</summary>
    private void Collapse()
    {
        if (_closing || !IsVisible)
        {
            return;
        }

        _closing = true;
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var width = new DoubleAnimation(ClosedWidth, Ms(170)) { EasingFunction = ease };
        width.Completed += (_, _) =>
        {
            // Opened again while shrinking: that wins.
            if (_closing)
            {
                _closing = false;
                Hide();
            }
        };

        Face.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ms(90)));
        Pill.BeginAnimation(WidthProperty, width);
        Pill.BeginAnimation(HeightProperty, new DoubleAnimation(ClosedHeight, Ms(170)) { EasingFunction = ease });
    }

    private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    /// <summary>A slow breath while idle, and a jump on every keystroke.</summary>
    private void AnimateWave()
    {
        var t = _clock.Elapsed.TotalSeconds;
        _energy *= 0.86;

        for (var i = 0; i < _bars.Length; i++)
        {
            var idle = 4 + 1.6 * (1 + Math.Sin(t * 3.1 + i * 0.9));
            var kick = _energy * 14 * Math.Abs(0.55 + 0.45 * Math.Sin(t * 17 + i * 2.3));
            _bars[i].Height = Math.Clamp(idle + kick, 3, 18);
        }
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _energy = Math.Min(1, _energy + 0.7);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        if (DateTime.Now - _shownAt < TimeSpan.FromMilliseconds(400))
        {
            // Still settling after the show; the user has not dismissed anything yet.
            Dispatcher.BeginInvoke(() =>
            {
                if (IsVisible && !_closing)
                {
                    ForegroundWindow.Take(this);
                    Keyboard.Focus(Input);
                }
            });
            return;
        }

        Collapse();
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

    /// <summary>A double right-click anywhere on the bar cancels it, like Escape.</summary>
    private void OnRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            Cancel();
        }

        e.Handled = true;
    }

    /// <summary>Closes the bar without making a task.</summary>
    internal void Cancel() => Collapse();

    /// <summary>
    /// A desktop click while the bar is open. It closes the bar, keeping anything typed
    /// as a task, the way clicking away from a chip edit keeps the typing. The second
    /// click of a double-click lands here too, so one that follows the opening click too
    /// closely is ignored rather than closing the bar it just opened.
    /// </summary>
    internal void ClickedAway()
    {
        if (DateTime.Now - _shownAt < TimeSpan.FromMilliseconds(NativeMethods.GetDoubleClickTime()))
        {
            return;
        }

        DropTask();
    }

    /// <summary>Hands the typed task over to become a chip, and closes the bar.</summary>
    private void DropTask()
    {
        if (_closing)
        {
            return;
        }

        var text = Input.Text.Trim();
        if (text.Length > 0)
        {
            Committed?.Invoke(text, DropPoint());
        }

        Collapse();
    }

    /// <summary>The gesture's point in device-independent pixels, kept clear of the screen edges.</summary>
    private Point DropPoint()
    {
        var scale = VisualTreeHelper.GetDpi(this);
        var area = System.Windows.Forms.Screen.FromPoint(_dropAt).WorkingArea;

        double Clamp(double value, double low, double high) => Math.Clamp(value, low, Math.Max(low, high));

        return new Point(
            Clamp(_dropAt.X / scale.DpiScaleX, area.Left / scale.DpiScaleX, area.Right / scale.DpiScaleX - ChipRoomWidth),
            Clamp(_dropAt.Y / scale.DpiScaleY, area.Top / scale.DpiScaleY, area.Bottom / scale.DpiScaleY - ChipRoomHeight));
    }
}
