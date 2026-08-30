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

public partial class MainWindow : Window, IDisposable
{
    private readonly SettingsStore _settingsStore;
    private RetraceSettings _settings;
    private readonly SqliteEventRepository _repository = new();
    private RecoveryStore? _store;
    private FileActivityMonitor? _monitor;
    private RecoveryEngine? _recovery;
    private SnapshotService? _snapshots;
    private readonly CommandParser _parser = new();
    private bool _protectionActive = true;
    private bool _allowClose;
    private EventRow? _selectedEventRow;

    public MainWindow(SettingsStore settingsStore, RetraceSettings settings)
    {
        _settingsStore = settingsStore;
        _settings = settings;
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _repository.InitializeAsync();
            await StartEngineAsync();
            LoadSettingsUi();
            await RefreshAllAsync();
            if (!_settings.FirstRunComplete)
            {
                _settings.FirstRunComplete = true;
                await _settingsStore.SaveAsync(_settings);
                MessageBox.Show(
                    "Retrace is now protecting your selected folders locally on this PC.\n\nFor this first test build, Retrace starts with a safe Desktop\\RetraceTest folder. You can add real folders under Settings after you have tested recovery on copies of files.",
                    "Welcome to Retrace",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Retrace could not start its protection engine.\n\n{ex.Message}\n\nCheck %LOCALAPPDATA%\\Retrace\\retrace.log for details.", "Retrace", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task StartEngineAsync()
    {
        _monitor?.Dispose();
        if (!_settings.ExcludedFolders.Contains(RetracePaths.DataDirectory, StringComparer.OrdinalIgnoreCase))
            _settings.ExcludedFolders.Add(RetracePaths.DataDirectory);
        _store = new RecoveryStore(RetracePaths.BaselineDirectory, RetracePaths.VersionsDirectory, _settings.MaxTrackedFileSizeMb);
        _monitor = new FileActivityMonitor(_repository, _store, _settings);
        _monitor.EventRecorded += Monitor_EventRecorded;
        await _monitor.StartAsync();
        _recovery = new RecoveryEngine(_repository, _monitor, _store, _settings);
        _snapshots = new SnapshotService(RetracePaths.SnapshotsDirectory, _monitor, _store);
        _protectionActive = true;
        UpdateProtectionUi();
    }

    private void Monitor_EventRecorded(object? sender, RetraceEvent e)
    {
        Dispatcher.BeginInvoke(async () => await RefreshVisibleListsAsync());
    }

    private async Task RefreshAllAsync()
    {
        var events = await _repository.GetRecentAsync(500);
        var rows = events.Select(EventRow.From).ToList();
        TimelineList.ItemsSource = rows;
        RecentList.ItemsSource = rows.Take(8).ToList();
        FindList.ItemsSource = Array.Empty<EventRow>();
        RecoverList.ItemsSource = rows.Where(x => x.Item.Status == "Active").ToList();
        WatchCountText.Text = $"{_settings.WatchedFolders.Count} folder{(_settings.WatchedFolders.Count == 1 ? "" : "s")} watched";
        UpdateDashboard(rows);
        await RefreshSnapshotsAsync();
        SyncSelectedEvent();
    }

    private async Task RefreshVisibleListsAsync()
    {
        var events = await _repository.GetRecentAsync(500);
        var rows = events.Select(EventRow.From).ToList();
        RecentList.ItemsSource = rows.Take(8).ToList();
        TimelineList.ItemsSource = rows;
        RecoverList.ItemsSource = rows.Where(x => x.Item.Status == "Active").ToList();
        UpdateDashboard(rows);
        SyncSelectedEvent();
    }

    private async Task RefreshSnapshotsAsync()
    {
        if (_snapshots is null) return;
        var snapshots = await _snapshots.ListAsync();
        var rows = snapshots.Select(x => new SnapshotRow(x)).ToList();
        SnapshotsList.ItemsSource = rows;
        HomeSnapshotsList.ItemsSource = rows.Take(5).ToList();
    }

    private void ShowPage(FrameworkElement page)
    {
        foreach (var p in new FrameworkElement[] { HomePage, TimelinePage, RecoverPage, FindPage, ExplainPage, ComingPage, SnapshotsPage, SettingsPage })
            p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        UpdateNavState(page);
    }

    private void UpdateNavState(FrameworkElement page)
    {
        ResetNavButtons();
        if (page == HomePage) HighlightNav(NavHomeButton);
        else if (page == RecoverPage) HighlightNav(NavRecoverButton);
        else if (page == FindPage) HighlightNav(NavFindButton);
        else if (page == ExplainPage) HighlightNav(NavExplainButton);
        else if (page == TimelinePage) HighlightNav(NavTimelineButton);
        else if (page == SnapshotsPage) HighlightNav(NavSnapshotsButton);
        else if (page == SettingsPage) HighlightNav(NavSettingsButton);
        else if (page == ComingPage && ComingTitle.Text == "Resume") HighlightNav(NavResumeButton);
        else if (page == ComingPage && ComingTitle.Text == "Finish") HighlightNav(NavFinishButton);
    }

    private void ResetNavButtons()
    {
        foreach (var b in new[] { NavHomeButton, NavRecoverButton, NavFindButton, NavExplainButton, NavResumeButton, NavFinishButton, NavTimelineButton, NavSnapshotsButton, NavSettingsButton })
        {
            b.Background = Brushes.Transparent;
            b.BorderBrush = Brushes.Transparent;
        }
    }

    private static void HighlightNav(Button button)
    {
        button.Background = (Brush)new BrushConverter().ConvertFromString("#102742")!;
        button.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#183F66")!;
    }

    private void UpdateDashboard(IReadOnlyCollection<EventRow> rows)
    {
        SummaryChangesTrackedText.Text = rows.Count.ToString("N0");
        SummaryFilesChangedText.Text = rows.Select(x => x.Item.DisplayPath).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString("N0");
        SummaryIssuesText.Text = rows.Count(x => x.Item.EventType == RetraceEventType.Deleted || !x.Item.RecoveryAvailable).ToString("N0");

        var latest = rows.Take(1).FirstOrDefault();
        if (latest is not null)
        {
            ExplainSummaryText.Text = $"The latest activity Retrace recorded was a {latest.Type.ToLowerInvariant()} event on {latest.Name}. Use Timeline or Recover to inspect and reverse recent changes.";
            ExplainConfidenceText.Text = "92%";
            ExplainConfidenceBar.Value = 92;
        }
        else
        {
            ExplainSummaryText.Text = "Retrace is waiting for new file activity in your watched folders.";
            ExplainConfidenceText.Text = "0%";
            ExplainConfidenceBar.Value = 0;
        }

        FooterStorageText.Text = $"Watched folders: {_settings.WatchedFolders.Count}  •  Recovery storage: {_settings.MaxRecoveryStorageGb} GB";
    }

    private void SyncSelectedEvent()
    {
        if (_selectedEventRow is null) return;
        try
        {
            var events = new[] { RecentList.SelectedItem as EventRow, TimelineList.SelectedItem as EventRow, RecoverList.SelectedItem as EventRow, FindList.SelectedItem as EventRow };
            var match = events.FirstOrDefault(x => x?.Item.Id == _selectedEventRow.Item.Id);
            if (match is not null) _selectedEventRow = match;
            UpdateEventDetails(_selectedEventRow);
        }
        catch { }
    }

    private void EventList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && list.SelectedItem is EventRow row)
        {
            _selectedEventRow = row;
            UpdateEventDetails(row);
        }
    }
}
