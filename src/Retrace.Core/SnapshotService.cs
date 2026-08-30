using System.Text.Json;

namespace Retrace.Core;

public sealed class SnapshotService
{
    private readonly string _root;
    private readonly FileActivityMonitor _monitor;
    private readonly RecoveryStore _store;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public SnapshotService(string snapshotRoot, FileActivityMonitor monitor, RecoveryStore store) { _root = snapshotRoot; _monitor = monitor; _store = store; Directory.CreateDirectory(_root); }

    public async Task<SnapshotInfo> CreateAsync(string name, IEnumerable<string> watchedRoots, CancellationToken token = default)
    {
        var info = new SnapshotInfo { Name = string.IsNullOrWhiteSpace(name) ? $"Snapshot {DateTime.Now:g}" : name.Trim() };
        var dir = Path.Combine(_root, info.Id); Directory.CreateDirectory(dir);
        var index = 0;
        foreach (var original in watchedRoots.Where(Directory.Exists))
        {
            token.ThrowIfCancellationRequested();
            var storage = Path.Combine(dir, "roots", index++.ToString());
            CopyDirectory(original, storage, token);
            info.Roots.Add(new SnapshotRoot { OriginalPath = original, StorageFolder = storage });
            await Task.Yield();
        }
        await File.WriteAllTextAsync(Path.Combine(dir, "snapshot.json"), JsonSerializer.Serialize(info, _json), token);
        return info;
    }

    public async Task<IReadOnlyList<SnapshotInfo>> ListAsync()
    {
        var list = new List<SnapshotInfo>();
        foreach (var file in Directory.EnumerateFiles(_root, "snapshot.json", SearchOption.AllDirectories))
        {
            try { var item = JsonSerializer.Deserialize<SnapshotInfo>(await File.ReadAllTextAsync(file)); if (item is not null) list.Add(item); } catch { }
        }
        return list.OrderByDescending(x => x.CreatedUtc).ToList();
    }

    public async Task RestoreAsync(SnapshotInfo snapshot, CancellationToken token = default)
    {
        _monitor.Suspend();
        try
        {
            foreach (var root in snapshot.Roots)
            {
                token.ThrowIfCancellationRequested();
                if (!Directory.Exists(root.StorageFolder)) continue;
                Directory.CreateDirectory(root.OriginalPath);
                MirrorDirectory(root.StorageFolder, root.OriginalPath, token);
                await Task.Yield();
            }
        }
        finally { await _store.BuildBaselineAsync(snapshot.Roots.Select(x => x.OriginalPath), token, overwrite: true); await _monitor.ResumeAsync(); }
    }

    public void Delete(SnapshotInfo snapshot)
    {
        var dir = Path.Combine(_root, snapshot.Id);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private static void CopyDirectory(string source, string dest, CancellationToken token)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested(); Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(source, dir)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested(); var target = Path.Combine(dest, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            try { File.Copy(file, target, true); } catch { }
        }
    }

    private static void MirrorDirectory(string source, string dest, CancellationToken token)
    {
        var sourceFiles = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Select(x => Path.GetRelativePath(source, x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories).ToList())
        {
            token.ThrowIfCancellationRequested(); var rel = Path.GetRelativePath(dest, existing); if (!sourceFiles.Contains(rel)) { try { File.Delete(existing); } catch { } }
        }
        CopyDirectory(source, dest, token);
        foreach (var dir in Directory.EnumerateDirectories(dest, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length).ToList())
        {
            token.ThrowIfCancellationRequested(); try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
        }
    }
}
