using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Wallup.Interop;
using Wallup.Models;

namespace Wallup.Views;

/// <summary>
/// One task, one window. Being a real window is what makes dragging, editing, the alarm
/// and the delete button possible at all - none of it can work on the wallpaper layer,
/// which receives no input.
/// </summary>
internal partial class ChipWindow : Window
{
    /// <summary>
    /// Transparent room around the capsule for its cast shadow, matching the root margin
    /// in the XAML. A task's saved position is the corner of the glass you can see, not of
    /// the window, so it survives a change to this number.
    /// </summary>
    private const double Halo = 14;

    private readonly TaskItem _task;

    internal ChipWindow(TaskItem task, AppSettings settings)
    {
        _task = task;
        DataContext = task;
        InitializeComponent();

        Left = task.X - Halo;
        Top = task.Y - Halo;
        ApplySettings(settings);

        MouseEnter += (_, _) => FadeActions(1);
        MouseLeave += (_, _) => FadeActions(0);
    }

    /// <summary>Raised when the user deletes this task from the chip.</summary>
    internal event Action<TaskItem>? Deleted;

    /// <summary>Raised after a drag or an edit, so the change can be persisted.</summary>
    internal event Action? Changed;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        DesktopWindow.Pin(this);
        AdaptiveGlass.Apply(this);
    }

    /// <summary>
    /// Takes on the appearance settings. Called again whenever a slider moves, so the chips
    /// on the desktop change while the settings panel is still open.
    /// </summary>
    internal void ApplySettings(AppSettings settings)
    {
        // The window's own opacity is safe here, unlike on anything inside the glass: a
        // layered window fades as a whole, with no separate layer for the glass to show.
        Opacity = settings.Opacity;

        Label.FontSize = settings.FontSize;
        Editor.FontSize = settings.FontSize;

        var width = settings.ChipWidth + 2 * Halo;
        if (width != Width)
        {
            Width = width;

            // A wider chip covers more wallpaper, which may tip it from dark to light.
            if (IsLoaded)
            {
                AdaptiveGlass.Apply(this);
            }
        }
    }

    private void FadeActions(double to)
    {
        var duration = TimeSpan.FromMilliseconds(120);
        Actions.BeginAnimation(OpacityProperty, new DoubleAnimation(to, duration));
        Created.BeginAnimation(OpacityProperty, new DoubleAnimation(1 - to, duration));
    }

    /// <summary>
    /// Fades the chip out, then runs <paramref name="then"/>. Gives a ticked task a beat
    /// on screen so the tick is seen before the chip drops away.
    /// </summary>
    internal void FadeOut(Action then)
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(350))
        {
            BeginTime = TimeSpan.FromMilliseconds(450),
        };
        fade.Completed += (_, _) => then();
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Puts the chip back to its set opacity, for a tick taken back mid-fade.</summary>
    internal void CancelFade() => BeginAnimation(OpacityProperty, null);

    /// <summary>
    /// Pulls the chip back inside <paramref name="bounds"/> if it has strayed outside, and
    /// records where it landed, which is what saves it.
    /// </summary>
    internal void KeepWithin(Rect bounds)
    {
        var left = Math.Clamp(Left, bounds.Left, Math.Max(bounds.Left, bounds.Right - Width));
        var top = Math.Clamp(Top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - ActualHeight));
        if (left == Left && top == Top)
        {
            return;
        }

        Left = left;
        Top = top;

        // Without this the move only lasted until the next start, when the chip went
        // straight back to its saved place off screen.
        _task.X = Left + Halo;
        _task.Y = Top + Halo;

        if (IsLoaded)
        {
            AdaptiveGlass.Apply(this);
        }
    }

    // ---- Dragging ---------------------------------------------------------

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        // While the editor is open a click belongs to the caret, not to the drag.
        if (Editor.Visibility == Visibility.Visible || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        DragMove();

        _task.X = Left + Halo;
        _task.Y = Top + Halo;

        // Dropped somewhere new, so the wallpaper underneath may have flipped from dark
        // to light or back. The glass itself follows the move on its own.
        AdaptiveGlass.Apply(this);

        Changed?.Invoke();
    }

    /// <summary>A double-click on the label opens the editor; a single click still drags.</summary>
    private void OnLabelClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            BeginEdit();
            e.Handled = true;
            return;
        }

        OnDragStart(sender, e);
    }

    private void BeginEdit()
    {
        Label.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        Editor.Focus();
        Editor.SelectAll();
    }

    // ---- Inline editing ---------------------------------------------------

    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        // Do not lean on focus to finish the edit. Window.Focus() does not take keyboard
        // focus off a child that already has it, so LostFocus never fired: the editor
        // stayed open and the typing was never written back to the task.
        switch (e.Key)
        {
            case Key.Enter:
                Commit();
                EndEdit();
                e.Handled = true;
                break;

            case Key.Escape:
                // Escape means "forget it", so put the stored text back in the box.
                Binding?.UpdateTarget();
                EndEdit();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Clicking away is the third way out of an edit, and it keeps the typing.</summary>
    private void OnEndEdit(object sender, RoutedEventArgs e)
    {
        if (Editor.Visibility != Visibility.Visible)
        {
            return;
        }

        Commit();
        EndEdit();
    }

    private BindingExpression? Binding => Editor.GetBindingExpression(TextBox.TextProperty);

    private void Commit() => Binding?.UpdateSource();

    private void EndEdit()
    {
        Editor.Visibility = Visibility.Collapsed;
        Label.Visibility = Visibility.Visible;
        Changed?.Invoke();
    }

    // ---- Alarm ------------------------------------------------------------

    private void OnClockClick(object sender, RoutedEventArgs e)
    {
        AlarmInput.Text = _task.AlarmAt?.ToString("HH:mm") ?? DateTime.Now.AddHours(1).ToString("HH:mm");
        AlarmPopup.IsOpen = true;
        AlarmInput.Focus();
        AlarmInput.SelectAll();
    }

    private void OnAlarmKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnAlarmSet(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            AlarmPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void OnAlarmSet(object sender, RoutedEventArgs e)
    {
        if (!TimeSpan.TryParse(AlarmInput.Text.Trim(), out var time))
        {
            AlarmInput.SelectAll();
            return;
        }

        // A time that has already passed today is meant for tomorrow.
        var when = DateTime.Today.Add(time);
        if (when <= DateTime.Now)
        {
            when = when.AddDays(1);
        }

        _task.AlarmAt = new DateTimeOffset(when);
        AlarmPopup.IsOpen = false;
        Changed?.Invoke();
    }

    private void OnAlarmClear(object sender, RoutedEventArgs e)
    {
        _task.AlarmAt = null;
        AlarmPopup.IsOpen = false;
        Changed?.Invoke();
    }

    // ---- Delete -----------------------------------------------------------

    private void OnDeleteClick(object sender, RoutedEventArgs e) => Deleted?.Invoke(_task);

    /// <summary>Pulses the chip when its alarm comes due.</summary>
    internal void Announce()
    {
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = 1,
            To = 0.25,
            Duration = TimeSpan.FromMilliseconds(400),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(4),
        });
    }
}
