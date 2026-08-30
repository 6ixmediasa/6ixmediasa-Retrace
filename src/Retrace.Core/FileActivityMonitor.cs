using System.Collections.Concurrent;

namespace Retrace.Core;

public sealed class FileActivityMonitor : IDisposable
{
    private readonly IEventRepository _repository;
    private readonly RecoveryStore _store;
    private readonly RetraceSettings _settings;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, DateTime> _debounce = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _suspended;
    public event EventHandler<RetraceEvent>? EventRecorded;

    public FileActivityMonitor(IEventRepository repository, RecoveryStore store, RetraceSettings settings)
    {
        _repository = repository; _store = store; _settings = settings;
    }

    public async Task StartAsync(CancellationToken token = default)
    {
        // Refresh current files at startup so the private baseline represents the state
        // Retrace actually began protecting in this session.
        await _store.BuildBaselineAsync(_settings.WatchedFolders, token, overwrite: true, excludedFolders: _settings.ExcludedFolders);
        foreach (var root in _settings.WatchedFolders.Where(Directory.Exists))
        {
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                InternalBufferSize = 64 * 1024,
                EnableRaisingEvents = true
            };
            watcher.Created += (_, e) => _ = HandleCreatedAsync(root, e.FullPath);
            watcher.Changed += (_, e) => _ = HandleChangedAsync(root, e.FullPath);
            watcher.Deleted += (_, e) => _ = HandleDeletedAsync(root, e.FullPath);
            watcher.Renamed += (_, e) => _ = HandleRenamedAsync(root, e.OldFullPath, e.FullPath);
            _watchers.Add(watcher);
        }
    }

    public void Suspend() { _suspended = true; foreach (var w in _watchers) w.EnableRaisingEvents = false; }
    public async Task ResumeAsync() { foreach (var w in _watchers) w.EnableRaisingEvents = true; _suspended = false; await Task.Delay(150); }

    private bool Ignore(string path)
    {
        if (_suspended) return true;
        if (_settings.ExcludedFolders.Any(x => PathHelpers.IsUnder(path, x))) return true;
        return false;
    }

    private bool Debounced(string key, int milliseconds)
    {
        var now = DateTime.UtcNow;
        if (_debounce.TryGetValue(key, out var last) && (now - last).TotalMilliseconds < milliseconds) return true;
        _debounce[key] = now; return false;
    }

    private async Task HandleCreatedAsync(string root, string path)
    {
        if (Ignore(path) || Debounced("C:" + path, 250)) return;
        await Task.Delay(120);
        var isDir = Directory.Exists(path);
        if (isDir) _store.EnsureBaselineDirectory(root, path);
        else if (File.Exists(path)) _store.RefreshBaseline(root, path);
        var item = Build(RetraceEventType.Created, null, path, isDir, recovery: true, recoveryPath: null);
        await RecordAsync(item);
    }

    private async Task HandleChangedAsync(string root, string path)
    {
        if (Ignore(path) || Directory.Exists(path) || !File.Exists(path) || Debounced("M:" + path, 900)) return;
        var previous = _store.CapturePreviousVersion(root, path);
        await Task.Delay(100);
        _store.RefreshBaseline(root, path);
        if (previous is null) return;
        var item = Build(RetraceEventType.Modified, path, path, false, true, previous);
        await RecordAsync(item);
    }

    private async Task HandleDeletedAsync(string root, string path)
    {
        if (Ignore(path) || Debounced("D:" + path, 250)) return;
        // The original item is already gone by the time FileSystemWatcher raises
        // Deleted, so determine its type from Retrace's private baseline first.
        var isDir = _store.BaselineIsDirectory(root, path);
        var recovery = _store.CaptureDeletedVersion(root, path, isDir);
        var item = Build(RetraceEventType.Deleted, path, null, isDir, recovery is not null, recovery);
        if (recovery is null)
            item.Notes = "Retrace saw this deletion but had no protected recovery copy available.";
        await RecordAsync(item);
    }

    private async Task HandleRenamedAsync(string root, string oldPath, string newPath)
    {
        if (Ignore(newPath) || Debounced("R:" + oldPath + ">" + newPath, 250)) return;
        var isDir = Directory.Exists(newPath);
        _store.RenameBaseline(root, oldPath, newPath, isDir);
        var item = Build(RetraceEventType.Renamed, oldPath, newPath, isDir, true, null);
        await RecordAsync(item);
    }

    private RetraceEvent Build(RetraceEventType type, string? oldPath, string? newPath, bool isDir, bool recovery, string? recoveryPath)
    {
        var path = newPath ?? oldPath ?? string.Empty;
        long? size = null;
        try { if (!isDir && File.Exists(path)) size = new FileInfo(path).Length; } catch { }
        return new RetraceEvent
        {
            TimestampUtc = DateTime.UtcNow, EventType = type, OriginalPath = oldPath, CurrentPath = newPath,
            FileName = Path.GetFileName(path), FileExtension = isDir ? string.Empty : Path.GetExtension(path),
            FileSize = size, IsDirectory = isDir, RecoveryAvailable = recovery, RecoveryDataPath = recoveryPath,
            Status = "Active"
        };
    }

    private async Task RecordAsync(RetraceEvent item)
    {
        try { item.Id = await _repository.AddAsync(item); EventRecorded?.Invoke(this, item); } catch { }
    }

    public void Dispose() { foreach (var watcher in _watchers) watcher.Dispose(); _watchers.Clear(); }
}
