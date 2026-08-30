namespace Retrace.Core;

public enum RetraceEventType
{
    Created = 1,
    Modified = 2,
    Renamed = 3,
    Deleted = 4
}

public sealed class RetraceEvent
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public RetraceEventType EventType { get; set; }
    public string? OriginalPath { get; set; }
    public string? CurrentPath { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public long? FileSize { get; set; }
    public bool IsDirectory { get; set; }
    public bool RecoveryAvailable { get; set; }
    public string? RecoveryDataPath { get; set; }
    public string Status { get; set; } = "Active";
    public string? Notes { get; set; }

    public string DisplayPath => CurrentPath ?? OriginalPath ?? string.Empty;
    public DateTime LocalTime => TimestampUtc.ToLocalTime();
}

public sealed class RetraceSettings
{
    public List<string> WatchedFolders { get; set; } = new();
    public List<string> ExcludedFolders { get; set; } = new();
    public int MaxRecoveryStorageGb { get; set; } = 5;
    public int KeepHistoryDays { get; set; } = 30;
    public int MaxTrackedFileSizeMb { get; set; } = 100;
    public bool StartWithWindows { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool FirstRunComplete { get; set; }
}

public sealed class RecoveryActionResult
{
    public long EventId { get; init; }
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class RecoveryBatchResult
{
    public int Requested { get; init; }
    public int Succeeded { get; init; }
    public int Failed { get; init; }
    public List<RecoveryActionResult> Actions { get; init; } = new();
}

public sealed class SnapshotInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<SnapshotRoot> Roots { get; set; } = new();
}

public sealed class SnapshotRoot
{
    public string OriginalPath { get; set; } = string.Empty;
    public string StorageFolder { get; set; } = string.Empty;
}
