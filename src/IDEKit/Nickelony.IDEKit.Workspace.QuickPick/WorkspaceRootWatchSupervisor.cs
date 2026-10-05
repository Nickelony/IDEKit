using Nickelony.IDEKit.FileWatching;

namespace Nickelony.IDEKit.Workspace.QuickPick;

/// <summary>
/// Owns the file watcher of each workspace root: it starts them, replaces one that fails, and starts again one that could not start.
/// </summary>
/// <remarks>
/// <para>
/// A watcher that fails while it runs is replaced at once, and a walk of the roots follows the replacement, because the changes made
/// while the root had no watcher are unknown. A watcher that cannot start is tried again by a later query, no sooner than <see cref="RetryInterval"/>
/// after the last attempt of that root, and a failure with the same outcome and exception as the one the root reported
/// last is not reported again. A root that does not exist when its watcher fails or cannot start is reported as missing, so its owner drops the files
/// it listed.
/// </para>
/// <para>
/// The supervisor holds one lock for its own state and calls its owner only outside it. The owner's callbacks may run on any thread.
/// </para>
/// </remarks>
internal sealed class WorkspaceRootWatchSupervisor : IDisposable
{
	/// <summary>The shortest time between two attempts to start the watcher of one root from a query.</summary>
	internal static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5.0);

	private readonly string[] _roots;
	private readonly FileWatcherFactory _createWatcher;
	private readonly TimeProvider _timeProvider;
	private readonly Func<FileChangeBatch, Task> _handleBatch;
	private readonly Action _rescan;
	private readonly Action<int> _rootMissing;
	private readonly Action<Exception> _reportFault;
	private readonly object _syncRoot = new();
	private readonly IFileWatcher?[] _watchers;
	private readonly bool[] _isRecovering;
	private readonly long[] _lastAttempt;
	private readonly string?[] _lastReportedFailure;

	private bool _isDisposed;

	/// <param name="roots">The normalized workspace roots.</param>
	/// <param name="createWatcher">Creates the watcher of a root.</param>
	/// <param name="timeProvider">The clock that measures the retry interval.</param>
	/// <param name="handleBatch">Receives the batches of changes that the watchers report.</param>
	/// <param name="rescan">Walks the roots again after a replacement watcher started.</param>
	/// <param name="rootMissing">Receives the index of a root that does not exist and has no watcher.</param>
	/// <param name="reportFault">Receives each failure the supervisor contained.</param>
	internal WorkspaceRootWatchSupervisor(
		IReadOnlyList<string> roots,
		FileWatcherFactory createWatcher,
		TimeProvider timeProvider,
		Func<FileChangeBatch, Task> handleBatch,
		Action rescan,
		Action<int> rootMissing,
		Action<Exception> reportFault
	)
	{
		_roots = [.. roots];
		_createWatcher = createWatcher;
		_timeProvider = timeProvider;
		_handleBatch = handleBatch;
		_rescan = rescan;
		_rootMissing = rootMissing;
		_reportFault = reportFault;
		_watchers = new IFileWatcher?[_roots.Length];
		_isRecovering = new bool[_roots.Length];
		_lastAttempt = new long[_roots.Length];
		_lastReportedFailure = new string?[_roots.Length];
	}

	/// <summary>Starts the watcher of every root, so a change made while the first walk runs is reported.</summary>
	internal void StartAll()
	{
		for (int index = 0; index < _roots.Length; index++)
			TryStart(index);
	}

	/// <summary>Starts again the watchers that are not running, for the roots whose retry interval has passed.</summary>
	internal void RetryUnwatched()
	{
		List<int> due = [];

		lock (_syncRoot)
		{
			if (_isDisposed)
				return;

			for (int index = 0; index < _roots.Length; index++)
			{
				if (_watchers[index] is null && !_isRecovering[index] && _timeProvider.GetElapsedTime(_lastAttempt[index]) >= RetryInterval)
				{
					_isRecovering[index] = true;
					due.Add(index);
				}
			}
		}

		foreach (int index in due)
			StartRecovery(index);
	}

	/// <summary>Stops every watcher.</summary>
	public void Dispose()
	{
		IFileWatcher?[] watchers;

		lock (_syncRoot)
		{
			if (_isDisposed)
				return;

			_isDisposed = true;
			watchers = [.. _watchers];

			Array.Clear(_watchers);
		}

		foreach (IFileWatcher? watcher in watchers)
			DisposeWatcher(watcher);
	}

	private bool TryStart(int index)
	{
		IFileWatcher? watcher = null;

		// The attempt is timed before the factory runs, so a factory that throws is held to the retry interval as well.
		lock (_syncRoot)
			_lastAttempt[index] = _timeProvider.GetTimestamp();

		try
		{
			watcher = _createWatcher(
				_roots[index],
				(batch, _) => _handleBatch(batch),
				(failed, exception) => HandleFailure(index, failed, exception)
			);

			// The watcher is registered before it starts, so a failure it raises while starting finds its own registration.
			lock (_syncRoot)
			{
				if (_isDisposed)
				{
					DisposeWatcher(watcher);
					return false;
				}

				_watchers[index] = watcher;
			}

			FileWatcherStartResult started = watcher.Start();

			if (started.Succeeded)
			{
				lock (_syncRoot)
					_lastReportedFailure[index] = null;

				return true;
			}

			Unregister(index, watcher);
			DisposeWatcher(watcher);
			ReportStartFailure(index, started.Outcome.ToString(), started.StartupException);

			return false;
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			if (watcher is not null)
			{
				Unregister(index, watcher);
				DisposeWatcher(watcher);
			}

			ReportStartFailure(index, "Created", exception);

			return false;
		}
	}

	// A root that fails to start in the same way again is not worth a second report; a different failure is.
	private void ReportStartFailure(int index, string outcome, Exception? exception)
	{
		string signature = $"{outcome}|{exception?.GetType().FullName}|{exception?.Message}";
		bool isNew;

		lock (_syncRoot)
		{
			isNew = _lastReportedFailure[index] != signature;
			_lastReportedFailure[index] = signature;
		}

		if (isNew && exception is not null)
			_reportFault(exception);
	}

	private void Unregister(int index, IFileWatcher watcher)
	{
		lock (_syncRoot)
		{
			if (ReferenceEquals(_watchers[index], watcher))
				_watchers[index] = null;
		}
	}

	private void DisposeWatcher(IFileWatcher? watcher)
	{
		if (watcher is null)
			return;

		try
		{
			watcher.Dispose();
		}
		catch (Exception exception)
		{
			_reportFault(exception);
		}
	}

	private void HandleFailure(int index, IFileWatcher watcher, Exception? exception)
	{
		bool startsRecovery;

		lock (_syncRoot)
		{
			// A failure of a watcher that was already replaced, or that a disposal stopped, is stale.
			if (_isDisposed || !ReferenceEquals(_watchers[index], watcher))
				return;

			_watchers[index] = null;
			startsRecovery = !_isRecovering[index];
			_isRecovering[index] = true;
		}

		DisposeWatcher(watcher);

		if (exception is not null)
			_reportFault(exception);

		if (startsRecovery)
			StartRecovery(index);
	}

	private void StartRecovery(int index) => _ = Task.Run(() => Recover(index));

	// The changes made while a root had no watcher are unknown, so a replacement that starts is followed by a walk, and a root that is gone loses
	// its files.
	private void Recover(int index)
	{
		try
		{
			if (TryStart(index))
				_rescan();
			else if (!Directory.Exists(_roots[index]))
				_rootMissing(index);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_reportFault(exception);
		}
		finally
		{
			lock (_syncRoot)
				_isRecovering[index] = false;
		}
	}
}
