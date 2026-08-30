# Retrace V0.1 architecture

`WPF UI + system tray -> FileActivityMonitor -> RecoveryStore + SQLite -> RecoveryEngine / SnapshotService`

V0.1 intentionally hosts the monitor in the desktop process and keeps the process alive in the Windows notification area. This makes the first user-test installer dramatically simpler: no administrator-required Windows Service installation and no IPC boundary yet. After the core recovery semantics are validated on real machines, the monitor can be moved behind a dedicated Windows service without rewriting the core event/recovery model.

## Recovery model

Retrace maintains a local shadow baseline for trackable files. A modification captures the previous baseline as an immutable version before the baseline is refreshed. A deletion can therefore use the last baseline copy. Rename/move events update the shadow baseline path. Recovery pauses watchers while changes are applied so Retrace does not mistake its own reversal operations for user activity.

## Safety rules

- Newest changes are reversed first.
- Existing conflicting destinations are not overwritten for delete/rename recovery.
- A created folder is removed only when empty at its turn in the reverse sequence.
- Modified files are restored only when a valid previous version exists.
- Monitoring is scoped to explicitly watched folders.
