namespace Nickelony.IDEKit.FileWatching;

/// <summary>Describes the outcome of attempting to start a file watcher.</summary>
public enum FileWatcherStartOutcome
{
	/// <summary>The watcher started watching on this call.</summary>
	Started = 1,

	/// <summary>The watcher was already watching, so this call made no change.</summary>
	AlreadyRunning = 2,

	/// <summary>The watcher could not start because it was already disposed.</summary>
	Disposed = 3,

	/// <summary>The watcher could not start because the workspace root could not be found or is unavailable.</summary>
	WorkspaceRootMissing = 4,

	/// <summary>
	/// The watcher failed to start because workspace validation, watcher creation, or activation threw, or because a watcher error during activation
	/// stopped it.
	/// </summary>
	StartupFailed = 5,
}
