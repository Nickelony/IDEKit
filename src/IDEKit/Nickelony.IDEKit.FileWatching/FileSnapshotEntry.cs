namespace Nickelony.IDEKit.FileWatching;

/// <summary>Represents the tracked file-system state for a single watched workspace path.</summary>
/// <param name="IsDirectory">Whether the path represents a directory.</param>
/// <param name="LastWriteUtcTicks">The last-write timestamp used for change detection.</param>
/// <param name="Length">The file length used for change detection.</param>
/// <param name="ContentFingerprint">A stable file-content fingerprint used when length and timestamps alone are ambiguous.</param>
public readonly record struct FileSnapshotEntry(bool IsDirectory, long LastWriteUtcTicks, long Length, ulong ContentFingerprint);
