namespace Nickelony.IDEKit.FileWatching;

/// <summary>Reports the outcome of attempting to start a file watcher together with the exception that caused a failed start.</summary>
/// <param name="Outcome">The watcher start outcome.</param>
/// <param name="StartupException">
/// The exception reported by a failed start, or <see langword="null"/> when a failed start has none, such as a watcher error the owner
/// already received during activation.
/// </param>
/// <remarks>
/// Only <see cref="FileWatcherStartOutcome.StartupFailed"/> carries a <see cref="StartupException"/>;
/// <see cref="FileWatcherStartOutcome.Started"/> and <see cref="FileWatcherStartOutcome.AlreadyRunning"/> count as a started watcher
/// (see <see cref="Succeeded"/>).
/// </remarks>
public readonly record struct FileWatcherStartResult(FileWatcherStartOutcome Outcome, Exception? StartupException)
{
	/// <summary>Whether the watcher is running after the start attempt, either because it started now or because it was already running.</summary>
	public bool Succeeded => Outcome is FileWatcherStartOutcome.Started or FileWatcherStartOutcome.AlreadyRunning;
}
