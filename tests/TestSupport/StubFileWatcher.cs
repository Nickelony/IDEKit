using Nickelony.IDEKit.FileWatching;

namespace Nickelony.Testing;

/// <summary>Answers <see cref="Start"/> with a configured result and never observes the file system.</summary>
/// <remarks>
/// A test of the recovery reconciliation needs a watcher that cannot deliver a real file-system change on its own. The default watcher would observe
/// the change while it is still running and track it before the replacement capture could see it, which empties the reconciliation delta and makes
/// the assertion vacuous. A test of the forwarding path hands a batch to the dispatch delegate itself, so the timing of a real event cannot add
/// a notification it did not expect.
/// </remarks>
internal sealed class StubFileWatcher : IFileWatcher
{
	private int _startCount;
	private int _disposeCount;

	/// <summary>The result <see cref="Start"/> returns; a started watcher by default.</summary>
	public FileWatcherStartResult StartResult { get; set; } = new(FileWatcherStartOutcome.Started, null);

	/// <summary>
	/// The directory that must exist for <see cref="Start"/>
	/// to return <see cref="StartResult"/>; while it is missing, the start reports <see cref="FileWatcherStartOutcome.WorkspaceRootMissing"/>,
	/// as the real watcher does. <see langword="null"/> skips the check.
	/// </summary>
	public string? RequiredDirectory { get; set; }

	/// <summary>
	/// An action that runs inside <see cref="Start"/> before it returns, so a test can raise the failure callback synchronously, as a watcher
	/// that fails while it activates does.
	/// </summary>
	public Action? OnStart { get; set; }

	/// <summary>The exception <see cref="Dispose"/> throws after it records the call, or <see langword="null"/> to dispose quietly.</summary>
	public Exception? DisposeException { get; set; }

	public int StartCount => Volatile.Read(ref _startCount);

	public int DisposeCount => Volatile.Read(ref _disposeCount);

	public bool IsDisposed => DisposeCount > 0;

	/// <summary>
	/// Creates a watcher for one workspace root, which reports a missing root as the real watcher does, and a scripted startup failure as the result
	/// the real watcher reports when its file-system watcher cannot be created.
	/// </summary>
	/// <param name="workspaceRootDirectoryPath">The workspace root the watcher stands for.</param>
	/// <param name="startupFailure">The failure its start reports, or <see langword="null"/> for a start that succeeds.</param>
	/// <returns>The watcher.</returns>
	public static StubFileWatcher ForWorkspaceRoot(string workspaceRootDirectoryPath, Exception? startupFailure = null)
	{
		return new StubFileWatcher
		{
			RequiredDirectory = workspaceRootDirectoryPath,

			StartResult = startupFailure is null
				? new FileWatcherStartResult(FileWatcherStartOutcome.Started, null)
				: new FileWatcherStartResult(FileWatcherStartOutcome.StartupFailed, startupFailure),
		};
	}

	/// <summary>
	/// Creates a watcher whose start reports a synchronous failure through the failure callback and then returns a startup failure.
	/// </summary>
	/// <param name="onWatcherFailed">The watcher failure callback the framework registered.</param>
	/// <returns>The watcher.</returns>
	public static StubFileWatcher FailingAtStart(Action<IFileWatcher, Exception?> onWatcherFailed)
	{
		Exception startupException = new IOException("Simulated synchronous startup failure.");

		var watcher = new StubFileWatcher { StartResult = new FileWatcherStartResult(FileWatcherStartOutcome.StartupFailed, startupException) };

		watcher.OnStart = () => onWatcherFailed(watcher, startupException);

		return watcher;
	}

	/// <inheritdoc/>
	public FileWatcherStartResult Start()
	{
		Interlocked.Increment(ref _startCount);
		OnStart?.Invoke();

		if (RequiredDirectory is not null && !Directory.Exists(RequiredDirectory))
			return new FileWatcherStartResult(FileWatcherStartOutcome.WorkspaceRootMissing, null);

		return StartResult;
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		Interlocked.Increment(ref _disposeCount);

		if (DisposeException is not null)
			throw DisposeException;
	}
}
