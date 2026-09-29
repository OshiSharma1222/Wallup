using System.Diagnostics;
using System.Windows;
using Wallup.Diagnostics;
using Wallup.Interop;
using Wallup.Models;
using Wallup.Storage;
using Wallup.ViewModels;
using Wallup.Views;

namespace Wallup;

public partial class App : Application
{
    private const string InstanceMutexName = "Wallup.SingleInstance";

    private Mutex? _instanceMutex;
    private DesktopClickHook? _hook;
    private CtrlAltHook? _keys;
    private System.Windows.Forms.NotifyIcon? _tray;
    private TaskListViewModel? _viewModel;
    private ChipHost? _chips;
    private ComposerWindow? _composer;
    private SettingsWindow? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--diagnose"))
        {
            Shutdown(DesktopProbe.Run());
            return;
        }

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            Log.Info("Another instance is already running; exiting.");
            Shutdown();
            return;
        }

        Log.Info("---- Wallup starting ----");

        var settingsStore = new SettingsStore();
        _viewModel = new TaskListViewModel(new TaskStore(), settingsStore, settingsStore.Load());

        _chips = new ChipHost(_viewModel);
        _chips.AlarmDue += OnAlarmDue;

        _composer = new ComposerWindow();
        _composer.Committed += OnTaskComposed;

        InstallHook();
        ApplyGesture();
        _viewModel.Settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppSettings.OpenWith))
            {
                ApplyGesture();
            }
        };

        InstallTray();

        Log.Info($"Ready. {_viewModel.ToDo.Count} task(s) on the desktop.");
    }

    /// <summary>Drops the new task where the composer was standing.</summary>
    private void OnTaskComposed(string text, Point at) => _viewModel?.Add(text, at.X, at.Y);

    private void OnAlarmDue(TaskItem task)
    {
        _tray?.ShowBalloonTip(8000, "Wallup", task.Text, System.Windows.Forms.ToolTipIcon.Info);
    }

    private void InstallHook()
    {
        _hook = new DesktopClickHook();

        // Clicking the desktop again is how you put the box away. The hook callback must
        // return immediately, so the UI work goes to the dispatcher.
        _hook.DesktopClickedWhileComposing += () => Dispatcher.BeginInvoke(() => _composer?.ClickedAway());
        _hook.IsComposing = () => _composer?.IsVisible == true;
        _hook.DesktopRightDoubleClicked += (x, y) => Dispatcher.BeginInvoke(() =>
        {
            // A toggle: the first opens the box, the next cancels it, exactly like Escape.
            if (_composer?.IsVisible == true)
            {
                _composer.Cancel();
                return;
            }

            _composer?.ShowAt(x, y);
        });

        if (!_hook.Install())
        {
            Log.Warn("Desktop click gesture unavailable; the tray menu is the only way in.");
        }
    }

    /// <summary>
    /// Switches the gestures to match the setting. The keyboard hook sees every key
    /// pressed anywhere, so it is only installed while Ctrl+Alt is actually wanted.
    /// </summary>
    private void ApplyGesture()
    {
        var gesture = _viewModel!.Settings.OpenWith;
        _hook!.RightClickEnabled = gesture != OpenGesture.CtrlAlt;

        if (gesture == OpenGesture.RightClick)
        {
            _keys?.Dispose();
            _keys = null;
            return;
        }

        if (_keys is not null)
        {
            return;
        }

        _keys = new CtrlAltHook();
        _keys.Pressed += () => Dispatcher.BeginInvoke(() =>
        {
            // A toggle, like the double right-click: pressed again, it cancels the box.
            if (_composer?.IsVisible == true)
            {
                _composer.Cancel();
                return;
            }

            ShowComposerAtCursor();
        });

        if (!_keys.Install())
        {
            Log.Warn("Ctrl+Alt gesture unavailable.");
        }
    }

    private void InstallTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Add task", null, (_, _) => ShowComposerAtCursor());
        menu.Items.Add("Today and settings", null, (_, _) => ShowSettings());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Clear finished", null, (_, _) => _viewModel?.ClearCompleted());
        menu.Items.Add("Bring tasks on screen", null, (_, _) => _chips?.ReflowOntoScreen());
        menu.Items.Add("Open log", null, (_, _) => OpenLog());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Shutdown());

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Wallup",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _tray.DoubleClick += (_, _) => ShowComposerAtCursor();
    }

    private void ShowComposerAtCursor()
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        _composer?.ShowAt(cursor.X, cursor.Y);
    }

    private void ShowSettings()
    {
        _settings ??= new SettingsWindow(_viewModel!);
        _settings.ShowToday();
    }

    private static void OpenLog() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Log.FilePath}\""));

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.SaveSettings();

        _hook?.Dispose();
        _keys?.Dispose();
        _chips?.Dispose();

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }

        _instanceMutex?.Dispose();

        Log.Info("---- Wallup stopped ----");
        base.OnExit(e);
    }
}
