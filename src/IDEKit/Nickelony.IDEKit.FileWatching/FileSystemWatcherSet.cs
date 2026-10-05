namespace Nickelony.IDEKit.FileWatching;

/// <summary>
/// Owns the native file-system watchers of one <see cref="FileWatcher"/>, wires their events to its callbacks, and stops them together.
/// </summary>
/// <remarks>
/// Every member takes the set's lock. The activation callbacks run under it, so a watcher error that a starting watcher raises is handled
/// only after the activation finished.
/// </remarks>
internal sealed class FileSystemWatcherSet
{
	private readonly string _workspaceRootDirectoryPath;
	private readonly LocalPathComparisonPolicy _pathComparison;
	private readonly Action<string, FileChangeKind> _queueChange;
	private readonly Action<Exception?, FileSystemWatcher> _handleError;
	private readonly Func<string, FileWatchSpecification, FileSystemWatcher> _createFileSystemWatcher;
	private readonly ILogger _logger;
	private readonly List<FileSystemWatcher> _watchers = [];
	private readonly object _syncRoot = new();

	/// <param name="workspaceRootDirectoryPath">The workspace root directory that each watcher observes.</param>
	/// <param name="pathComparison">The comparison that decides whether file-name casing matters when a rename endpoint is matched.</param>
	/// <param name="queueChange">Queues one changed file for the next dispatch.</param>
	/// <param name="handleError">Handles an error that a registered watcher raised, naming the watcher.</param>
	/// <param name="createFileSystemWatcher">
	/// Creates the file-system watcher of one specification, or <see langword="null"/> for the built-in one.
	/// </param>
	/// <param name="logger">The logger instance.</param>
	internal FileSystemWatcherSet(
		string workspaceRootDirectoryPath,
		LocalPathComparisonPolicy pathComparison,
		Action<string, FileChangeKind> queueChange,
		Action<Exception?, FileSystemWatcher> handleError,
		Func<string, FileWatchSpecification, FileSystemWatcher>? createFileSystemWatcher,
		ILogger logger
	)
	{
		_workspaceRootDirectoryPath = workspaceRootDirectoryPath;
		_pathComparison = pathComparison;
		_queueChange = queueChange;
		_handleError = handleError;
		_createFileSystemWatcher = createFileSystemWatcher ?? CreateBuiltInWatcher;
		_logger = logger;
	}

	/// <summary>The number of active file-system watchers.</summary>
	internal int Count
	{
		get
		{
			lock (_syncRoot)
				return _watchers.Count;
		}
	}

	/// <summary>Reports whether a watcher belongs to the current set.</summary>
	/// <param name="watcher">The watcher that raised an error.</param>
	/// <returns>
	/// <see langword="false"/> for a watcher that a later activation or a stop superseded, because its errors belong to a set that was replaced.
	/// </returns>
	internal bool Contains(FileSystemWatcher watcher)
	{
		lock (_syncRoot)
			return _watchers.Contains(watcher);
	}

	/// <summary>Creates, registers and activates one file-system watcher per specification.</summary>
	/// <param name="specifications">The specifications to watch.</param>
	/// <param name="isDisposed">Reports whether the owner is disposed, which ends the activation.</param>
	/// <param name="onRegistered">
	/// Invoked for each watcher after it is registered and before it starts raising events, or <see langword="null"/>.
	/// </param>
	/// <param name="onActivated">Invoked under the set's lock once every watcher is active.</param>
	/// <returns>How the activation ended.</returns>
	/// <remarks>
	/// Each watcher is registered before it starts raising events: the error handler ignores errors from watchers that are not registered,
	/// so activating a not-yet-registered watcher could drop its failure and leave a dead registration behind.
	/// </remarks>
	internal FileSystemWatcherActivationOutcome Activate(
		IReadOnlyList<FileWatchSpecification> specifications,
		Func<bool> isDisposed,
		Action<FileSystemWatcher>? onRegistered,
		Action onActivated
	)
	{
		lock (_syncRoot)
		{
			if (isDisposed())
				return FileSystemWatcherActivationOutcome.Disposed;

			if (_watchers.Count > 0)
				return FileSystemWatcherActivationOutcome.AlreadyRunning;

			for (int i = 0; i < specifications.Count; i++)
			{
				FileSystemWatcher watcher = CreateWatcher(specifications[i]);

				_watchers.Add(watcher);
				onRegistered?.Invoke(watcher);
				watcher.EnableRaisingEvents = true;

				// A watcher error raised while it was starting already stopped and cleared the set and notified
				// the owner; stop starting specifications instead of resurrecting a set the owner was told failed.
				if (_watchers.Count == 0)
					return FileSystemWatcherActivationOutcome.FailedDuringActivation;
			}

			// Disposal marks the owner disposed under its notification lock and then waits for this lock, so it can
			// win the race after the entry check; report the disposal instead of claiming a start that the disposal
			// tears down as soon as this lock is released.
			if (isDisposed())
				return FileSystemWatcherActivationOutcome.Disposed;

			onActivated();
			return FileSystemWatcherActivationOutcome.Started;
		}
	}

	/// <summary>Stops and disposes all active file-system watchers.</summary>
	/// <remarks>
	/// Disposing the watchers synchronously from an error callback is safe on .NET 8: <see cref="FileSystemWatcher"/> raises <c>Error</c>
	/// on a thread-pool callback that <c>Dispose</c> does not join. The stop stays synchronous so the owner's failure callback always observes
	/// an already-stopped set and can recreate it immediately.
	/// </remarks>
	internal void StopAll()
	{
		List<FileSystemWatcher> watchersToDispose;

		lock (_syncRoot)
		{
			if (_watchers.Count == 0)
				return;

			watchersToDispose = [.. _watchers];
			_watchers.Clear();
		}

		for (int i = watchersToDispose.Count - 1; i >= 0; i--)
			DisposeBestEffort(watchersToDispose[i]);
	}

	/// <summary>Creates and configures a file-system watcher for one specification without starting it.</summary>
	/// <param name="specification">The watch specification to apply.</param>
	/// <returns>The configured, not yet activated file-system watcher.</returns>
	/// <remarks>
	/// The notify filter and buffer size are applied on every platform but interpreted with full fidelity only on Windows; on other platforms
	/// they are best-effort hints, and a native overflow or watch failure still surfaces through <see cref="FileSystemWatcher.Error"/>.
	/// Correctness never depends on the buffer size because a reported failure resynchronizes the workspace, and the caller registers the watcher
	/// before activating it so a failure raised while it starts is never treated as an unknown (superseded) watcher.
	/// </remarks>
	private FileSystemWatcher CreateWatcher(FileWatchSpecification specification)
	{
		FileSystemWatcher watcher = _createFileSystemWatcher(_workspaceRootDirectoryPath, specification);

		try
		{
			watcher.IncludeSubdirectories = specification.IncludeSubdirectories;

			watcher.NotifyFilter =
				NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.DirectoryName | NotifyFilters.Size;

			// 64 KiB gives the internal change buffer headroom for bursty writes on large workspaces; the watcher reports
			// a buffer overflow through its Error event, which is handled as a watcher failure.
			watcher.InternalBufferSize = 64 * 1024;

			watcher.Created += (_, e) => _queueChange(e.FullPath, FileChangeKind.Created);
			watcher.Changed += (_, e) => _queueChange(e.FullPath, FileChangeKind.Changed);
			watcher.Deleted += (_, e) => _queueChange(e.FullPath, FileChangeKind.Deleted);

			// The raising instance is captured so an error callback that was already queued when this watcher was
			// superseded cannot stop a replacement watcher set created by a later Start call.
			watcher.Error += (_, e) => _handleError(e.GetException(), watcher);

			watcher.Renamed += (_, e) =>
			{
				// A rename is raised when either endpoint matches the filter; forward only the endpoints that still
				// match so a rename out of the watched set cannot forward an unmatched create. The endpoint check uses
				// simple wildcard matching in managed code, the same grammar the snapshot tracker's enumeration uses.
				if (specification.MatchesFileName(e.OldFullPath, _pathComparison))
					_queueChange(e.OldFullPath, FileChangeKind.Deleted);

				if (specification.MatchesFileName(e.FullPath, _pathComparison))
					_queueChange(e.FullPath, FileChangeKind.Created);
			};

			return watcher;
		}
		catch
		{
			// A watcher that failed configuration is not registered yet; dispose it here so a partially
			// constructed instance cannot leak its 64 KiB internal buffer.
			DisposeBestEffort(watcher);
			throw;
		}
	}

	private static FileSystemWatcher CreateBuiltInWatcher(string workspaceRootDirectoryPath, FileWatchSpecification specification) =>
		new(workspaceRootDirectoryPath, specification.Filter);

	private void DisposeBestEffort(FileSystemWatcher watcher)
	{
		try
		{
			watcher.Dispose();
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Failed to dispose workspace watcher resource '{ResourceName}'.", nameof(FileSystemWatcher));
		}
	}
}

/// <summary>Classifies how the activation of a <see cref="FileSystemWatcherSet"/> ended.</summary>
internal enum FileSystemWatcherActivationOutcome
{
	/// <summary>Every watcher is active.</summary>
	Started,

	/// <summary>The owner was disposed before or during the activation.</summary>
	Disposed,

	/// <summary>The set already holds active watchers.</summary>
	AlreadyRunning,

	/// <summary>A watcher error raised during the activation already stopped the set and notified the owner.</summary>
	FailedDuringActivation,
}
