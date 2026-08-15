namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Reports the outcome of attempting to start a workspace file watcher together with the exception that caused a
/// failed start.
/// </summary>
/// <param name="Outcome">The watcher start outcome.</param>
/// <param name="StartupException">
/// The exception reported by a failed start, or <see langword="null"/> when the start failed without one (a disposed
/// watcher or a missing workspace root).
/// </param>
/// <remarks>
/// Only <see cref="WorkspaceWatcherStartOutcome.StartupFailed"/> carries a
/// <see cref="StartupException"/>; <see cref="WorkspaceWatcherStartOutcome.Started"/> and
/// <see cref="WorkspaceWatcherStartOutcome.AlreadyRunning"/> both count as a started watcher (see
/// <see cref="Started"/>).
/// </remarks>
public readonly record struct WorkspaceWatcherStartResult(
	WorkspaceWatcherStartOutcome Outcome,
	Exception? StartupException)
{
	/// <summary>
	/// Gets a value indicating whether the watcher is running after the start attempt, either because it started now
	/// or because it was already running.
	/// </summary>
	public bool Started => Outcome is WorkspaceWatcherStartOutcome.Started or WorkspaceWatcherStartOutcome.AlreadyRunning;
}
