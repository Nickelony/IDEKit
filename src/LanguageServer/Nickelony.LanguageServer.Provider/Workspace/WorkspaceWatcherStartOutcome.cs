namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Describes the outcome of attempting to start a workspace file watcher.
/// </summary>
public enum WorkspaceWatcherStartOutcome
{
	/// <summary>
	/// The watcher started successfully.
	/// </summary>
	Started = 1,

	/// <summary>
	/// The watcher was already running.
	/// </summary>
	AlreadyRunning = 2,

	/// <summary>
	/// The watcher could not start because it was already disposed.
	/// </summary>
	Disposed = 3,

	/// <summary>
	/// The watcher could not start because the workspace root could not be found or is unavailable.
	/// </summary>
	WorkspaceRootMissing = 4,

	/// <summary>
	/// The watcher failed to start because workspace validation, watcher creation, or activation threw.
	/// </summary>
	StartupFailed = 5
}
