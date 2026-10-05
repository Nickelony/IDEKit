namespace Nickelony.IDEKit.FileWatching;

/// <summary>Represents one file change ready for forwarding.</summary>
/// <param name="Path">The affected file path.</param>
/// <param name="Kind">The effective file change kind.</param>
public readonly record struct FileChange(string Path, FileChangeKind Kind);
