using System.Windows;
using System.Windows.Controls;
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
    private readonly TaskItem _task;

    internal ChipWindow(TaskItem task)
    {
        _task = task;
        DataContext = task;
        InitializeComponent();

        Left = task.X;
        Top = task.Y;

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
        Glass.Apply(this, smallCorners: true);
    }

    private void FadeActions(double to) =>
        Actions.BeginAnimation(OpacityProperty,
            new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)));

    // ---- Dragging ---------------------------------------------------------

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        // While the editor is open a click belongs to the caret, not to the drag.
        if (Editor.Visibility == Visibility.Visible || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        DragMove();

        _task.X = Left;
        _task.Y = Top;
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
        if (e.Key is not (Key.Enter or Key.Escape))
        {
            return;
        }

        // Pushing focus to the window commits the binding and ends the edit.
        Focus();
        e.Handled = true;
    }

    private void OnEndEdit(object sender, RoutedEventArgs e)
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
