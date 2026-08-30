namespace Retrace.Core;

public sealed class RecoveryEngine
{
    private readonly IEventRepository _repository;
    private readonly FileActivityMonitor _monitor;
    private readonly RecoveryStore _store;
    private readonly RetraceSettings _settings;

    public RecoveryEngine(IEventRepository repository, FileActivityMonitor monitor, RecoveryStore store, RetraceSettings settings)
    {
        _repository = repository; _monitor = monitor; _store = store; _settings = settings;
    }

    public async Task<RecoveryBatchResult> RecoverAsync(IEnumerable<RetraceEvent> sourceEvents, CancellationToken token = default)
    {
        var events = sourceEvents.Where(e => e.Status == "Active" && e.RecoveryAvailable)
            .OrderByDescending(e => e.TimestampUtc).ThenByDescending(e => e.Id).ToList();
        var results = new List<RecoveryActionResult>();
        _monitor.Suspend();
        try
        {
            foreach (var item in events)
            {
                token.ThrowIfCancellationRequested();
                var result = RecoverOne(item);
                results.Add(new RecoveryActionResult { EventId = item.Id, Success = result.ok, Message = result.message });
                if (result.ok)
                {
                    SyncBaseline(item);
                    await _repository.UpdateStatusAsync(item.Id, "Reversed", token);
                }
            }
        }
        finally { await _monitor.ResumeAsync(); }
        return new RecoveryBatchResult { Requested = events.Count, Succeeded = results.Count(x => x.Success), Failed = results.Count(x => !x.Success), Actions = results };
    }

    private void SyncBaseline(RetraceEvent item)
    {
        var path = item.OriginalPath ?? item.CurrentPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        var root = _settings.WatchedFolders.FirstOrDefault(r => PathHelpers.IsUnder(path, r));
        if (root is null) return;
        switch (item.EventType)
        {
            case RetraceEventType.Created:
                _store.RemoveBaseline(root, item.CurrentPath ?? path, item.IsDirectory);
                break;
            case RetraceEventType.Renamed:
                if (!string.IsNullOrWhiteSpace(item.CurrentPath) && !string.IsNullOrWhiteSpace(item.OriginalPath))
                    _store.RenameBaseline(root, item.CurrentPath, item.OriginalPath, item.IsDirectory);
                break;
            case RetraceEventType.Deleted:
                if (item.IsDirectory && Directory.Exists(path)) _store.RefreshBaselineTree(root, path);
                else if (!item.IsDirectory && File.Exists(path)) _store.RefreshBaseline(root, path);
                break;
            case RetraceEventType.Modified:
                if (!item.IsDirectory && File.Exists(path)) _store.RefreshBaseline(root, path);
                break;
        }
    }

    private static (bool ok, string message) RecoverOne(RetraceEvent item)
    {
        try
        {
            switch (item.EventType)
            {
                case RetraceEventType.Created:
                    var created = item.CurrentPath;
                    if (string.IsNullOrWhiteSpace(created)) return (false, "Missing created path.");
                    if (File.Exists(created)) { File.Delete(created); return (true, $"Removed {created}"); }
                    if (Directory.Exists(created))
                    {
                        if (Directory.EnumerateFileSystemEntries(created).Any()) return (false, "Created folder is not empty; recover its child changes first.");
                        Directory.Delete(created); return (true, $"Removed folder {created}");
                    }
                    return (true, "Created item was already absent.");

                case RetraceEventType.Deleted:
                    if (string.IsNullOrWhiteSpace(item.OriginalPath)) return (false, "Missing original path.");
                    if (item.IsDirectory)
                    {
                        if (string.IsNullOrWhiteSpace(item.RecoveryDataPath) || !Directory.Exists(item.RecoveryDataPath))
                            return (false, "Recovery copy of the deleted folder is unavailable.");
                        if (File.Exists(item.OriginalPath) || Directory.Exists(item.OriginalPath))
                            return (false, "An item already exists at the original folder location.");
                        if (!RecoveryStore.RestoreDirectory(item.RecoveryDataPath, item.OriginalPath))
                            return (false, "The deleted folder could not be restored safely.");
                        return (true, $"Restored folder {item.OriginalPath}");
                    }
                    if (string.IsNullOrWhiteSpace(item.RecoveryDataPath) || !File.Exists(item.RecoveryDataPath)) return (false, "Recovery copy is unavailable.");
                    Directory.CreateDirectory(Path.GetDirectoryName(item.OriginalPath)!);
                    if (File.Exists(item.OriginalPath)) return (false, "A file already exists at the original location.");
                    File.Copy(item.RecoveryDataPath, item.OriginalPath); return (true, $"Restored {item.OriginalPath}");

                case RetraceEventType.Renamed:
                    if (string.IsNullOrWhiteSpace(item.OriginalPath) || string.IsNullOrWhiteSpace(item.CurrentPath)) return (false, "Missing rename paths.");
                    if (File.Exists(item.CurrentPath))
                    {
                        if (File.Exists(item.OriginalPath)) return (false, "Original filename is already occupied.");
                        Directory.CreateDirectory(Path.GetDirectoryName(item.OriginalPath)!);
                        File.Move(item.CurrentPath, item.OriginalPath); return (true, $"Renamed back to {item.OriginalPath}");
                    }
                    if (Directory.Exists(item.CurrentPath))
                    {
                        if (Directory.Exists(item.OriginalPath)) return (false, "Original folder path is already occupied.");
                        Directory.CreateDirectory(Path.GetDirectoryName(item.OriginalPath)!);
                        Directory.Move(item.CurrentPath, item.OriginalPath); return (true, $"Moved folder back to {item.OriginalPath}");
                    }
                    return (false, "Renamed item can no longer be found at its current path.");

                case RetraceEventType.Modified:
                    var target = item.CurrentPath ?? item.OriginalPath;
                    if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(item.RecoveryDataPath) || !File.Exists(item.RecoveryDataPath)) return (false, "Previous version is unavailable.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(item.RecoveryDataPath, target, true); return (true, $"Restored previous version of {target}");
            }
        }
        catch (Exception ex) { return (false, ex.Message); }
        return (false, "Unsupported event.");
    }
}
