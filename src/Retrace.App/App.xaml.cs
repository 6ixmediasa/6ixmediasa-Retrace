using System;
using System.IO;
using System.Linq;
using System.Windows;
using Retrace.Data;

namespace Retrace.App;

public partial class App : System.Windows.Application
{
    private MainWindow? _main;
    private System.Windows.Forms.NotifyIcon? _tray;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log("Unhandled: " + args.ExceptionObject);
        DispatcherUnhandledException += (_, args) =>
        {
            Log("UI: " + args.Exception);
            try
            {
                System.Windows.MessageBox.Show(
                    "Retrace hit an unexpected UI error. Details were saved to:\n\n" + RetracePaths.LogPath,
                    "Retrace startup error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { }
            args.Handled = true;
        };

        Log("Retrace startup begin. BaseDirectory=" + AppContext.BaseDirectory);

        try
        {
            var settingsStore = new SettingsStore();
            var settings = await settingsStore.LoadAsync();
            Log("Settings loaded.");

            _main = new MainWindow(settingsStore, settings);
            Log("MainWindow constructed.");

            // Show the actual application before optional tray integration. A tray
            // icon failure must never make Retrace look as if it did not launch.
            _main.Show();
            Log("MainWindow shown.");

            ConfigureTraySafe();

            if (e.Args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase)))
            {
                _main.Hide();
                Log("Started in background mode.");
            }
        }
        catch (Exception ex)
        {
            Log("Startup failure: " + ex);
            try
            {
                System.Windows.MessageBox.Show(
                    "Retrace could not open.\n\n" + ex.Message + "\n\nA detailed log was saved to:\n" + RetracePaths.LogPath,
                    "Retrace startup error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { }
            Shutdown(-1);
        }
    }

    private void ConfigureTraySafe()
    {
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Retrace.ico");
            System.Drawing.Icon icon;
            try
            {
                icon = File.Exists(iconPath)
                    ? new System.Drawing.Icon(iconPath)
                    : System.Drawing.SystemIcons.Application;
            }
            catch
            {
                icon = System.Drawing.SystemIcons.Application;
            }

            _tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = icon,
                Text = "Retrace - Your computer remembers",
                Visible = true
            };

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open Retrace", null, (_, _) => ShowMain());
            menu.Items.Add("Create Snapshot", null, async (_, _) =>
            {
                if (_main is not null) await _main.CreateQuickSnapshotAsync();
            });
            menu.Items.Add("Pause / Resume", null, (_, _) => _main?.ToggleProtection());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => ExitApp());
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (_, _) => ShowMain();
            Log("Tray configured.");
        }
        catch (Exception ex)
        {
            Log("Tray disabled because setup failed: " + ex);
            try
            {
                if (_tray is not null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                    _tray = null;
                }
            }
            catch { }
        }
    }

    private void ShowMain()
    {
        if (_main is null) return;
        _main.Show();
        _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    public void ExitApp()
    {
        try { _main?.Dispose(); } catch { }
        if (_tray is not null)
        {
            try { _tray.Visible = false; } catch { }
            try { _tray.Dispose(); } catch { }
        }
        Shutdown();
    }

    private static void Log(object value)
    {
        try
        {
            File.AppendAllText(
                RetracePaths.LogPath,
                $"[{DateTime.Now:O}] {value}{Environment.NewLine}");
        }
        catch { }
    }
}
