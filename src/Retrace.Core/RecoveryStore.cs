namespace Retrace.Core;

public sealed class RecoveryStore
{
    private readonly string _baselineRoot;
    private readonly string _versionsRoot;
    private readonly long _maxTrackedBytes;

    public RecoveryStore(string baselineRoot, string versionsRoot, int maxTrackedFileSizeMb)
    {
        _baselineRoot = baselineRoot;
        _versionsRoot = versionsRoot;
        _maxTrackedBytes = maxTrackedFileSizeMb * 1024L * 1024L;
        Directory.CreateDirectory(_baselineRoot);
        Directory.CreateDirectory(_versionsRoot);
    }

    public string BaselinePath(string watchedRoot, string path) =>
        Path.Combine(_baselineRoot, PathHelpers.RootKey(watchedRoot), PathHelpers.SafeRelative(watchedRoot, path));

    public async Task BuildBaselineAsync(
        IEnumerable<string> roots,
        CancellationToken token = default,
        bool overwrite = false,
        IEnumerable<string>? excludedFolders = null)
    {
        var excluded = (excludedFolders ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(Path.GetFullPath)
            .ToList();

        foreach (var root in roots.Where(Directory.Exists))
        {
            // Keep a directory skeleton as well as file copies. This lets Retrace
            // distinguish deleted folders (including empty folders) from files.
            Directory.CreateDirectory(BaselinePath(root, root));

            foreach (var dir in EnumerateDirectoriesSafe(root, excluded))
            {
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(BaselinePath(root, dir));
                await Task.Yield();
            }

            foreach (var file in EnumerateFilesSafe(root, excluded))
            {
                token.ThrowIfCancellationRequested();
                var target = BaselinePath(root, file);
                if (overwrite || !File.Exists(target))
                    PathHelpers.TryCopyFile(file, target, _maxTrackedBytes);
                await Task.Yield();
            }
        }
    }

    public string? CapturePreviousVersion(string watchedRoot, string currentPath)
    {
        var baseline = BaselinePath(watchedRoot, currentPath);
        if (!File.Exists(baseline)) return null;
        var ext = Path.GetExtension(currentPath);
        var version = NewVersionFile(ext);
        return PathHelpers.TryCopyFile(baseline, version, long.MaxValue) ? version : null;
    }

    public bool RefreshBaseline(string watchedRoot, string currentPath)
    {
        if (!File.Exists(currentPath)) return false;
        return PathHelpers.TryCopyFile(currentPath, BaselinePath(watchedRoot, currentPath), _maxTrackedBytes);
    }

    public void EnsureBaselineDirectory(string watchedRoot, string currentPath)
    {
        try { Directory.CreateDirectory(BaselinePath(watchedRoot, currentPath)); } catch { }
    }

    public bool BaselineIsDirectory(string watchedRoot, string path)
    {
        try { return Directory.Exists(BaselinePath(watchedRoot, path)); }
        catch { return false; }
    }

    /// <summary>
    /// Moves the private baseline copy into an immutable recovery version when an
    /// item is deleted. This makes recovery independent of the Windows Recycle Bin.
    /// Moving is preferred over copying because it is fast and avoids a second
    /// temporary duplicate. If moving is not possible, Retrace falls back to copy.
    /// </summary>
    public string? CaptureDeletedVersion(string watchedRoot, string deletedPath, bool isDirectory)
    {
        var baseline = BaselinePath(watchedRoot, deletedPath);
        try
        {
            if (isDirectory)
            {
                if (!Directory.Exists(baseline)) return null;
                var version = NewVersionDirectory();
                Directory.CreateDirectory(Path.GetDirectoryName(version)!);
                try
                {
                    Directory.Move(baseline, version);
                    return version;
                }
                catch
                {
                    CopyDirectory(baseline, version, overwrite: true);
                    if (Directory.Exists(version))
                    {
                        try { Directory.Delete(baseline, true); } catch { }
                        return version;
                    }
                    return null;
                }
            }

            if (!File.Exists(baseline)) return null;
            var ext = Path.GetExtension(deletedPath);
            var fileVersion = NewVersionFile(ext);
            Directory.CreateDirectory(Path.GetDirectoryName(fileVersion)!);
            try
            {
                File.Move(baseline, fileVersion, true);
                return fileVersion;
            }
            catch
            {
                if (PathHelpers.TryCopyFile(baseline, fileVersion, long.MaxValue))
                {
                    try { File.Delete(baseline); } catch { }
                    return fileVersion;
                }
                return null;
            }
        }
        catch { return null; }
    }

    public void RenameBaseline(string watchedRoot, string oldPath, string newPath, bool isDirectory)
    {
        var oldBaseline = BaselinePath(watchedRoot, oldPath);
        var newBaseline = BaselinePath(watchedRoot, newPath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(newBaseline)!);
            if (isDirectory && Directory.Exists(oldBaseline))
            {
                if (Directory.Exists(newBaseline)) Directory.Delete(newBaseline, true);
                Directory.Move(oldBaseline, newBaseline);
            }
            else if (File.Exists(oldBaseline))
            {
                File.Move(oldBaseline, newBaseline, true);
            }
        }
        catch { }
    }

    public void RemoveBaseline(string watchedRoot, string path, bool isDirectory)
    {
        try
        {
            var baseline = BaselinePath(watchedRoot, path);
            if (isDirectory && Directory.Exists(baseline)) Directory.Delete(baseline, true);
            else if (File.Exists(baseline)) File.Delete(baseline);
        }
        catch { }
    }

    public bool RefreshBaselineTree(string watchedRoot, string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath)) return false;
            var target = BaselinePath(watchedRoot, directoryPath);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            CopyDirectory(directoryPath, target, overwrite: true, maxFileBytes: _maxTrackedBytes);
            return Directory.Exists(target);
        }
        catch { return false; }
    }

    public static bool RestoreDirectory(string recoveryDirectory, string targetDirectory)
    {
        try
        {
            if (!Directory.Exists(recoveryDirectory) || Directory.Exists(targetDirectory) || File.Exists(targetDirectory))
                return false;
            CopyDirectory(recoveryDirectory, targetDirectory, overwrite: false);
            return true;
        }
        catch { return false; }
    }

    private string NewVersionFile(string extension)
    {
        var version = Path.Combine(_versionsRoot, DateTime.UtcNow.ToString("yyyyMMdd"), $"{Guid.NewGuid():N}{extension}");
        Directory.CreateDirectory(Path.GetDirectoryName(version)!);
        return version;
    }

    private string NewVersionDirectory()
    {
        var version = Path.Combine(_versionsRoot, DateTime.UtcNow.ToString("yyyyMMdd"), $"{Guid.NewGuid():N}.dir");
        Directory.CreateDirectory(Path.GetDirectoryName(version)!);
        return version;
    }

    private static void CopyDirectory(string source, string destination, bool overwrite, long maxFileBytes = long.MaxValue)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var dest = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            var info = new FileInfo(file);
            if (info.Length <= maxFileBytes) File.Copy(file, dest, overwrite);
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string root, IReadOnlyList<string> excluded)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            string[] dirs = Array.Empty<string>();
            try { dirs = Directory.GetDirectories(dir); } catch { }
            foreach (var child in dirs)
            {
                if (excluded.Any(x => PathHelpers.IsUnder(child, x) || child.Equals(x, StringComparison.OrdinalIgnoreCase))) continue;
                yield return child;
                pending.Push(child);
            }
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, IReadOnlyList<string> excluded)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            if (excluded.Any(x => PathHelpers.IsUnder(dir, x) || dir.Equals(x, StringComparison.OrdinalIgnoreCase))) continue;
            string[] files = Array.Empty<string>(); string[] dirs = Array.Empty<string>();
            try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); } catch { }
            foreach (var f in files) yield return f;
            foreach (var d in dirs) pending.Push(d);
        }
    }
}
