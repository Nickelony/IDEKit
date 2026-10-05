namespace Nickelony.IDEKit.FileWatching;

/// <summary>Reports a watcher failure of one workspace root that its supervisor could not recover from.</summary>
/// <param name="Code">One of the <see cref="FileWatchFailureCodes"/> values.</param>
/// <param name="WorkspaceRootDirectoryPath">The normalized workspace root whose watcher failed.</param>
/// <param name="Exception">The exception that caused the failure, or <see langword="null"/> when there was none.</param>
public sealed record FileWatchFailure(string Code, string WorkspaceRootDirectoryPath, Exception? Exception);
