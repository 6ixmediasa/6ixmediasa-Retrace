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
    private void SnapshotList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && list.SelectedItem is SnapshotRow row)
        {
            DetailTitleText.Text = row.Name;
            DetailSelectedText.Text = "Snapshot selected";
            DetailTypeText.Text = "Snapshot";
            DetailTimeText.Text = row.DisplayDate;
            DetailCurrentPathText.Text = string.Join(Environment.NewLine, row.Item.Roots.Select(x => x.OriginalPath));
            DetailOriginalPathText.Text = "Saved state";
            DetailRecoveryStatusText.Text = "Restorable";
            DetailNotesText.Text = "Use Restore to return watched folders to this saved point in time.";
        }
    }

    private void UpdateEventDetails(EventRow? row)
    {
        if (row is null)
        {
            DetailTitleText.Text = "No event selected";
            DetailSelectedText.Text = "Select an event from Timeline, Recover, Find, or Recent Activity.";
            DetailTypeText.Text = "—";
            DetailTimeText.Text = "—";
            DetailCurrentPathText.Text = "—";
            DetailOriginalPathText.Text = "—";
            DetailRecoveryStatusText.Text = "—";
            DetailNotesText.Text = "—";
            return;
        }

        DetailTitleText.Text = row.Name;
        DetailSelectedText.Text = row.Type + " event selected";
        DetailTypeText.Text = row.Type;
        DetailTimeText.Text = row.Item.LocalTime.ToString("dddd, dd MMM yyyy • HH:mm:ss");
        DetailCurrentPathText.Text = string.IsNullOrWhiteSpace(row.Item.CurrentPath) ? row.Item.DisplayPath : row.Item.CurrentPath!;
        DetailOriginalPathText.Text = string.IsNullOrWhiteSpace(row.Item.OriginalPath) ? row.Item.DisplayPath : row.Item.OriginalPath!;
        DetailRecoveryStatusText.Text = row.RecoveryLabel;
        DetailNotesText.Text = string.IsNullOrWhiteSpace(row.Item.Notes) ? DefaultNoteFor(row.Item) : row.Item.Notes!;
    }

    private static string DefaultNoteFor(RetraceEvent ev) => ev.EventType switch
    {
        RetraceEventType.Created => "Retrace recorded a new file or folder appearing in a watched location.",
        RetraceEventType.Modified => "Retrace saved a protected version so earlier contents can be restored.",
        RetraceEventType.Renamed => "Retrace can reverse this rename and restore the previous filename.",
        RetraceEventType.Deleted => "Retrace preserved recovery data so the deleted item can be restored even after Recycle Bin removal.",
        _ => "Recorded by Retrace."
    };

    private void EventDetailsView_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEventRow is null) return;
        OpenPath(_selectedEventRow.Item.OriginalPath ?? _selectedEventRow.Item.CurrentPath ?? _selectedEventRow.Item.DisplayPath);
    }

    private async void EventDetailsReverse_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEventRow is null) return;
        if (!_selectedEventRow.Item.RecoveryAvailable)
        {
            MessageBox.Show("Retrace recorded this event, but no recovery data is available for reversal.", "Retrace", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(BuildRecoveryPreview(new[] { _selectedEventRow.Item }), "Retrace recovery preview", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await RecoverEventsAsync(new[] { _selectedEventRow.Item });
    }

    private void ToggleProtection_Click(object sender, RoutedEventArgs e) => ToggleProtection();

    private void SixMediaLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        catch { }
    }

    private void Home_Click(object sender, RoutedEventArgs e) => ShowPage(HomePage);
    private async void Timeline_Click(object sender, RoutedEventArgs e) { ShowPage(TimelinePage); await RefreshVisibleListsAsync(); }
    private async void Recover_Click(object sender, RoutedEventArgs e) { ShowPage(RecoverPage); await RefreshVisibleListsAsync(); }
    private void Find_Click(object sender, RoutedEventArgs e) => ShowPage(FindPage);
    private void Explain_Click(object sender, RoutedEventArgs e) => ShowPage(ExplainPage);
    private void Resume_Click(object sender, RoutedEventArgs e) { ComingTitle.Text = "Resume"; ComingText.Text = "Restore a previous work context — applications, documents, windows, folders and tabs."; ShowPage(ComingPage); }
    private void Finish_Click(object sender, RoutedEventArgs e) { ComingTitle.Text = "Finish"; ComingText.Text = "Surface incomplete work such as unfinished uploads, drafts and abandoned tasks without nagging you."; ShowPage(ComingPage); }
    private async void Snapshots_Click(object sender, RoutedEventArgs e) { ShowPage(SnapshotsPage); await RefreshSnapshotsAsync(); }
    private void Settings_Click(object sender, RoutedEventArgs e) { LoadSettingsUi(); ShowPage(SettingsPage); }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAllAsync();

    private async void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Content is string text)
        {
            if (text.StartsWith("Show", StringComparison.OrdinalIgnoreCase)) { ShowPage(TimelinePage); await RefreshAllAsync(); return; }
            CommandBox.Text = text; await RunCommandAsync();
        }
    }

    private async void RunCommand_Click(object sender, RoutedEventArgs e) => await RunCommandAsync();
    private async void CommandBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await RunCommandAsync(); } }

    private async Task RunCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(CommandBox.Text)) return;
        var events = await _parser.ResolveRecoveryCommandAsync(CommandBox.Text, _repository);
        if (events.Count == 0)
        {
            MessageBox.Show("Retrace didn't find any reversible changes matching that request.", "Retrace", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var preview = BuildRecoveryPreview(events);
        if (MessageBox.Show(preview, "Retrace recovery preview", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await RecoverEventsAsync(events);
    }

    private static string BuildRecoveryPreview(IEnumerable<RetraceEvent> events)
    {
        var list = events.ToList();
        var groups = list.GroupBy(x => x.EventType).ToDictionary(g => g.Key, g => g.Count());
        var sb = new StringBuilder();
        sb.AppendLine($"Retrace will reverse {list.Count} change{(list.Count == 1 ? "" : "s")}:\n");
        foreach (var type in Enum.GetValues<RetraceEventType>()) if (groups.TryGetValue(type, out var count)) sb.AppendLine($"• {count} {type.ToString().ToLowerInvariant()}");
        sb.AppendLine("\nOnly these selected file-system changes will be touched. Continue?");
        return sb.ToString();
    }

    private async Task RecoverEventsAsync(IEnumerable<RetraceEvent> events)
    {
        if (_recovery is null) return;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var result = await _recovery.RecoverAsync(events);
            await RefreshAllAsync();
            var firstSuccess = result.Actions.FirstOrDefault(x => x.Success);
            var msg = $"Recovered {result.Succeeded} of {result.Requested} selected changes.";
            if (result.Failed > 0) msg += $"\n\n{result.Failed} could not be reversed safely. No forced overwrite was performed.";
            if (MessageBox.Show(msg + "\n\nOpen the affected location?", "Retrace — recovery complete", MessageBoxButton.YesNo, result.Failed == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                var source = events.FirstOrDefault();
                if (source is not null) OpenPath(source.OriginalPath ?? source.CurrentPath ?? string.Empty);
            }
        }
        finally { Mouse.OverrideCursor = null; }
    }

    private async void RecoverSelected_Click(object sender, RoutedEventArgs e)
    {
        var rows = RecoverList.SelectedItems.Cast<EventRow>().ToList();
        if (rows.Count == 0) { RecoverStatus.Text = "Select one or more changes first."; return; }
        var unavailable = rows.Count(x => !x.Item.RecoveryAvailable);
        var events = rows.Where(x => x.Item.RecoveryAvailable).Select(x => x.Item).ToList();
        if (events.Count == 0)
        {
            RecoverStatus.Text = "The selected changes were recorded, but no protected recovery copy is available.";
            return;
        }
        var preview = BuildRecoveryPreview(events);
        if (unavailable > 0) preview += $"\n\n{unavailable} selected change{(unavailable == 1 ? "" : "s")} cannot be reversed and will be skipped.";
        if (MessageBox.Show(preview, "Retrace recovery preview", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await RecoverEventsAsync(events);
    }

    private void SelectFive_Click(object sender, RoutedEventArgs e) => SelectRecoverSince(DateTime.UtcNow.AddMinutes(-5));
    private void SelectHour_Click(object sender, RoutedEventArgs e) => SelectRecoverSince(DateTime.UtcNow.AddHours(-1));
    private void SelectAllRecover_Click(object sender, RoutedEventArgs e)
    {
        RecoverList.SelectedItems.Clear();
        foreach (EventRow row in RecoverList.Items)
            if (row.Item.RecoveryAvailable) RecoverList.SelectedItems.Add(row);
        RecoverStatus.Text = $"{RecoverList.SelectedItems.Count} selected";
    }
    private void SelectRecoverSince(DateTime sinceUtc)
    {
        RecoverList.SelectedItems.Clear();
        foreach (EventRow row in RecoverList.Items)
            if (row.Item.TimestampUtc >= sinceUtc && row.Item.RecoveryAvailable) RecoverList.SelectedItems.Add(row);
        RecoverStatus.Text = $"{RecoverList.SelectedItems.Count} selected";
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        var q = FindBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(q)) { FindList.ItemsSource = Array.Empty<EventRow>(); return; }
        var results = await _repository.SearchAsync(q, 250);
        FindList.ItemsSource = results.Select(EventRow.From).ToList();
    }

    private async void ExplainAnalyse_Click(object sender, RoutedEventArgs e)
    {
        var recent = await _repository.GetSinceAsync(DateTime.UtcNow.AddHours(-1));
        var q = ExplainBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length > 2).ToArray();
            var matching = recent.Where(ev => terms.Any(t => ev.DisplayPath.Contains(t, StringComparison.OrdinalIgnoreCase))).Take(12).ToList();
            if (matching.Count > 0) recent = matching;
        }
        var items = recent.Take(12).ToList();
        if (items.Count == 0) { ExplainResult.Text = "Retrace hasn't recorded any recent changes to correlate yet."; return; }
        var sb = new StringBuilder("These are the most relevant recent changes Retrace can see:\n\n");
        foreach (var ev in items) sb.AppendLine($"• {ev.LocalTime:HH:mm:ss} — {ev.EventType}: {ev.FileName}");
        sb.AppendLine("\nV0.1 deliberately reports evidence instead of inventing a cause. Deeper app-aware diagnosis comes after the recovery engine is validated.");
        ExplainResult.Text = sb.ToString();
    }
}
