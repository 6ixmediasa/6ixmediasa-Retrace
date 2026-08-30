using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Retrace.Core;
using Retrace.Data;

namespace Retrace.App;

public partial class MainWindow
{
    public async Task CreateQuickSnapshotAsync()
    {
        if (_snapshots is null) return;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            await _snapshots.CreateAsync($"Quick snapshot — {DateTime.Now:g}", _settings.WatchedFolders);
            await RefreshSnapshotsAsync();
        }
        finally { Mouse.OverrideCursor = null; }
    }

    private async void QuickSnapshot_Click(object sender, RoutedEventArgs e) { await CreateQuickSnapshotAsync(); MessageBox.Show("Snapshot created.", "Retrace", MessageBoxButton.OK, MessageBoxImage.Information); }
    private async void CreateSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshots is null) return;
        var name = string.IsNullOrWhiteSpace(SnapshotNameBox.Text) ? $"Snapshot — {DateTime.Now:g}" : SnapshotNameBox.Text.Trim();
        Mouse.OverrideCursor = Cursors.Wait;
        try { await _snapshots.CreateAsync(name, _settings.WatchedFolders); SnapshotNameBox.Clear(); await RefreshSnapshotsAsync(); }
        finally { Mouse.OverrideCursor = null; }
    }

    private async void RestoreSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshots is null || sender is not Button { Tag: SnapshotInfo snapshot }) return;
        if (MessageBox.Show($"Restore snapshot '{snapshot.Name}'?\n\nFiles added after this snapshot inside watched folders may be removed. Retrace will only touch the folders contained in this snapshot.", "Restore snapshot", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        Mouse.OverrideCursor = Cursors.Wait;
        try { await _snapshots.RestoreAsync(snapshot); MessageBox.Show("Snapshot restored successfully.", "Retrace", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally { Mouse.OverrideCursor = null; }
    }

    private async void DeleteSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshots is null || sender is not Button { Tag: SnapshotInfo snapshot }) return;
        if (MessageBox.Show($"Delete snapshot '{snapshot.Name}'?", "Retrace", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _snapshots.Delete(snapshot); await RefreshSnapshotsAsync();
    }

    private void ViewPath_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string path }) OpenPath(path); }
    private void OpenRecoveryFolder_Click(object sender, RoutedEventArgs e) => OpenPath(RetracePaths.RecoveryDirectory);

    private static void OpenPath(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else
            {
                var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
            }
        }
        catch { }
    }

    private void LoadSettingsUi()
    {
        WatchedFoldersList.ItemsSource = _settings.WatchedFolders.ToList();
        StorageGbBox.Text = _settings.MaxRecoveryStorageGb.ToString();
        HistoryDaysBox.Text = _settings.KeepHistoryDays.ToString();
        StartWindowsCheck.IsChecked = _settings.StartWithWindows;
        TrayCheck.IsChecked = _settings.MinimizeToTray;
        UpdateProtectionUi();
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose a folder for Retrace to remember", UseDescriptionForTitle = true };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !_settings.WatchedFolders.Contains(dialog.SelectedPath, StringComparer.OrdinalIgnoreCase))
        {
            if (PathHelpers.IsUnder(RetracePaths.DataDirectory, dialog.SelectedPath) || RetracePaths.DataDirectory.Equals(dialog.SelectedPath, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("For safety, Retrace V0.1 cannot watch a folder that contains its own recovery database. Choose a more specific folder.", "Retrace", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _settings.WatchedFolders.Add(dialog.SelectedPath); WatchedFoldersList.ItemsSource = _settings.WatchedFolders.ToList();
        }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (WatchedFoldersList.SelectedItem is string selected) { _settings.WatchedFolders.RemoveAll(x => x.Equals(selected, StringComparison.OrdinalIgnoreCase)); WatchedFoldersList.ItemsSource = _settings.WatchedFolders.ToList(); }
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(StorageGbBox.Text, out var storage)) _settings.MaxRecoveryStorageGb = Math.Clamp(storage, 1, 100);
        if (int.TryParse(HistoryDaysBox.Text, out var days)) _settings.KeepHistoryDays = Math.Clamp(days, 1, 3650);
        _settings.StartWithWindows = StartWindowsCheck.IsChecked == true;
        _settings.MinimizeToTray = TrayCheck.IsChecked == true;
        await _settingsStore.SaveAsync(_settings);
        ConfigureStartup(_settings.StartWithWindows);
        await StartEngineAsync();
        await _repository.DeleteOlderThanAsync(DateTime.UtcNow.AddDays(-_settings.KeepHistoryDays));
        await RefreshAllAsync();
        MessageBox.Show("Settings saved. Retrace protection restarted with the new configuration.", "Retrace", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void ConfigureStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (enabled) key?.SetValue("Retrace", $"\"{Environment.ProcessPath}\" --background"); else key?.DeleteValue("Retrace", false);
        }
        catch { }
    }

    public void ToggleProtection()
    {
        if (_monitor is null) return;
        if (_protectionActive) { _monitor.Suspend(); _protectionActive = false; }
        else { _ = _monitor.ResumeAsync(); _protectionActive = true; }
        UpdateProtectionUi();
    }

    private void UpdateProtectionUi()
    {
        if (ProtectionText is null) return;
        ProtectionText.Text = _protectionActive ? "Monitoring active" : "Monitoring paused";
        var color = _protectionActive ? "#4FD08A" : "#FFB86B";
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        ProtectionText.Foreground = brush;
        WatchCountText.Text = $"{_settings.WatchedFolders.Count} folder{(_settings.WatchedFolders.Count == 1 ? "" : "s")} watched";
        if (MonitoringToggleButton is not null)
        {
            MonitoringToggleButton.Content = _protectionActive ? "Monitoring Active" : "Monitoring Paused";
            MonitoringToggleButton.Background = (Brush)new BrushConverter().ConvertFromString(_protectionActive ? "#0E2A1A" : "#3A2611")!;
            MonitoringToggleButton.BorderBrush = (Brush)new BrushConverter().ConvertFromString(_protectionActive ? "#276647" : "#8A5A1D")!;
        }
        if (FooterStatusText is not null) FooterStatusText.Text = _protectionActive ? "Monitoring • Protection active • Up to date" : "Monitoring • Protection paused";
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose) return;
        if (_settings.MinimizeToTray)
        {
            e.Cancel = true; Hide();
        }
    }

    public void Dispose()
    {
        _allowClose = true;
        _monitor?.Dispose();
        Close();
    }
}
