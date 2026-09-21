using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Wallup.Diagnostics;
using Wallup.Interop;
using Wallup.Storage;
using Wallup.ViewModels;
using Wallup.Views;

namespace Wallup;

public partial class App : Application
{
    private const string InstanceMutexName = "Wallup.SingleInstance";

    private bool _opaque;
    private Mutex? _instanceMutex;
    private DesktopRightClickHook? _hook;
    private System.Windows.Forms.NotifyIcon? _tray;
    private TaskListViewModel? _viewModel;
    private AmbientWindow? _ambient;
    private TaskBoxWindow? _taskBox;
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

        _opaque = e.Args.Contains("--opaque");

        Log.Info("---- Wallup starting ----");

        var settingsStore = new SettingsStore();
        _viewModel = new TaskListViewModel(new TaskStore(), settingsStore, settingsStore.Load());

        _ambient = new AmbientWindow(_viewModel, _opaque);
        _ambient.Show();

        _taskBox = new TaskBoxWindow(_viewModel);
        _taskBox.SettingsRequested += ShowSettings;

        InstallHook();
        InstallTray();

        Log.Info($"Ready. Wallpaper attach strategy: {_ambient.AttachStrategy}, attached={_ambient.IsAttached}.");

        if (e.Args.Contains("--selftest"))
        {
            RunSelfTest();
        }
    }

    /// <summary>
    /// Brings the app up for real, reports where the ambient window landed relative to the
    /// desktop icons, then exits. This is the only honest way to check the attach, because
    /// a window on the wrong side of the icon view looks identical from inside the process.
    /// </summary>
    private void RunSelfTest()
    {
        NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);

        // Give the shell a beat to settle the new child window into z-order.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            var report = $"""
                Wallup self-test
                Strategy  : {_ambient!.AttachStrategy}
                Attached  : {_ambient.IsAttached}
                Hook      : {(_hook?.IsInstalled == true ? "installed" : "NOT installed")}

                {DesktopLayer.DescribeAttachment(_ambient.Handle)}
                """;

            Console.WriteLine(report);
            Log.Raw(report);

            Shutdown(_ambient.IsAttached ? 0 : 1);
        };
        timer.Start();
    }

    private void InstallHook()
    {
        _hook = new DesktopRightClickHook();
        _hook.DesktopRightClicked += (x, y) =>
        {
            // The hook callback must return immediately, so hand the UI work to the
            // dispatcher rather than opening a window inline.
            Dispatcher.BeginInvoke(() => _taskBox?.ShowAt(x, y));
        };

        if (!_hook.Install())
        {
            Log.Warn("Right-click gesture unavailable; the tray menu is the only way in.");
        }
    }

    private void InstallTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Add task", null, (_, _) => ShowTaskBoxAtCursor());
        menu.Items.Add("Settings", null, (_, _) => ShowSettings());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Reattach to wallpaper", null, (_, _) => Reattach());
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

        _tray.DoubleClick += (_, _) => ShowTaskBoxAtCursor();
    }

    private void ShowTaskBoxAtCursor()
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        _taskBox?.ShowAt(cursor.X, cursor.Y);
    }

    private void ShowSettings()
    {
        _settings ??= new SettingsWindow(_viewModel!);
        _settings.Show();
        _settings.Activate();
    }

    /// <summary>
    /// Explorer restarts tear down WorkerW and orphan our window. Rebuilding the ambient
    /// window is the cheap fix until we watch for the shell's restart message.
    /// </summary>
    private void Reattach()
    {
        if (_viewModel is null)
        {
            return;
        }

        _ambient?.Close();
        _ambient = new AmbientWindow(_viewModel, _opaque);
        _ambient.Show();
        Log.Info($"Reattached. Strategy: {_ambient.AttachStrategy}, attached={_ambient.IsAttached}.");
    }

    private static void OpenLog() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Log.FilePath}\""));

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.SaveSettings();

        _hook?.Dispose();

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
