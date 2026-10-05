namespace Nickelony.IDEKit.FileWatching;

/// <summary>The owner callbacks that a <see cref="FileWatchSupervisor"/> consults, reports to and dispatches through.</summary>
/// <remarks>
/// The owner supplies every callback, and the <see cref="FileWatchSupervisor"/> constructor rejects a missing one. <see cref="IsEnabled"/> and
/// <see cref="IsDisposed"/> run under the supervisor's lock, so they must return without blocking; <see cref="ReportFailure"/> and
/// <see cref="ObserveBackgroundTask"/> can run inside the watcher's failure callback, so they must return promptly. The
/// <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/FileWatchingGuide.md#the-supervisor">file-watching guide</see> lists the rules.
/// </remarks>
public sealed class FileWatchSupervisorHooks
{
	/// <summary>Reports whether the owner currently wants watching, which gates every watcher start.</summary>
	public required Func<bool> IsEnabled { get; init; }

	/// <summary>Reports whether the owner is disposed, which stops every start, recovery and reconciliation.</summary>
	public required Func<bool> IsDisposed { get; init; }

	/// <summary>Reports a failure that the supervisor could not recover from, once until a watcher starts again.</summary>
	public required Action<FileWatchFailure> ReportFailure { get; init; }

	/// <summary>
	/// Forwards coalesced file changes of the supervisor's root to the owner, naming the delivering supervisor so a nested root's own watcher stays
	/// the single forwarder for its subtree.
	/// </summary>
	public required Func<FileWatchSupervisor, FileChangeBatch, CancellationToken, Task> DispatchAsync { get; init; }

	/// <summary>Observes a background task of the supervisor, such as the reconciliation after a watcher recovery, so its fault is logged.</summary>
	public required Action<Task, string> ObserveBackgroundTask { get; init; }
}
