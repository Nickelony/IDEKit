using Nickelony.IDEKit.Core.Callbacks;
using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.FileWatching;
using Nickelony.QuickPick;

namespace Nickelony.IDEKit.Workspace.QuickPick;

/// <summary>Keeps the list of files below the workspace roots, walking each root once and then applying what its file watcher reports.</summary>
/// <remarks>
/// <para>
/// The first query starts the work: a watcher per root, then one walk of every root on a pool thread. A change that a watcher
/// reports while the walk runs is held back and re-checked after the walk is installed. After that, the tracker never walks to answer a query,
/// because every created, deleted or moved file or folder is applied to the list as it is reported. The watchers, their replacement and their retry
/// belong to a <see cref="WorkspaceRootWatchSupervisor"/>, which asks this tracker to walk again after a replacement and to drop the files of a root
/// that is gone.
/// </para>
/// <para>
/// A change is applied by asking the file system what is at the path now, never by trusting the kind of the report, so reports that arrive
/// late or out of order cannot leave a stale file behind, and the spelling on disk wins over the spelling of the report. The tracker lists
/// at most as many files as its bound, the roots taking what the earlier ones left, and drops the files that the filter excludes.
/// </para>
/// <para>
/// Three locks nest in one order. The walk lock admits one walk, or the drop of a root, at a time. The reconcile lock serializes the application
/// of changes with the installation of a walk. The state lock guards the list and is never held while the file system is read.
/// </para>
/// </remarks>
internal sealed class WorkspaceFileTracker : IDisposable
{
	private readonly string[] _roots;
	private readonly WorkspaceFileFilter _filter;
	private readonly int _maxFileCount;
	private readonly WorkspaceFileEnumerator _enumerate;
	private readonly QuickPickGroup _fileGroup;
	private readonly Action<Exception> _reportFault;
	private readonly CancellationTokenSource _lifetime = new();
	private readonly CancellationToken _lifetimeToken;

	private readonly object _walkLock = new();
	private readonly object _reconcileLock = new();
	private readonly object _stateLock = new();

	private readonly WorkspaceRootWatchSupervisor _watchers;

	private Dictionary<string, QuickPickItem> _items;
	private HashSet<string> _folders;
	private HashSet<string>? _pending;
	private WorkspaceFileSnapshot? _snapshot;
	private Task? _ready;
	private bool _isDisposed;
	private int _walkCount;

	/// <param name="rootPaths">The workspace roots, as absolute paths.</param>
	/// <param name="filter">Decides which files and folders stay out of the list.</param>
	/// <param name="maxFileCount">The largest number of files the list holds.</param>
	/// <param name="createWatcher">Creates the watcher of a root.</param>
	/// <param name="enumerate">Enumerates the files below a folder.</param>
	/// <param name="pathComparison">The comparison that decides which paths are one file.</param>
	/// <param name="fileGroup">The group of the items of the files.</param>
	/// <param name="timeProvider">The clock that measures the retry interval of a watcher.</param>
	/// <param name="reportFault">Receives each failure the tracker contained.</param>
	/// <exception cref="ArgumentException">A root is <see langword="null"/>, empty, relative or listed twice.</exception>
	internal WorkspaceFileTracker(
		IReadOnlyList<string> rootPaths,
		WorkspaceFileFilter filter,
		int maxFileCount,
		FileWatcherFactory createWatcher,
		WorkspaceFileEnumerator enumerate,
		LocalPathComparisonPolicy pathComparison,
		QuickPickGroup fileGroup,
		TimeProvider timeProvider,
		Action<Exception> reportFault
	)
	{
		_roots = [.. LocalPath.NormalizeRoots(rootPaths, pathComparison)];
		_filter = filter;
		_maxFileCount = maxFileCount;
		_enumerate = enumerate;
		PathComparison = pathComparison;
		_fileGroup = fileGroup;
		_reportFault = reportFault;
		_lifetimeToken = _lifetime.Token;

		_watchers = new WorkspaceRootWatchSupervisor(
			roots: _roots,
			createWatcher,
			timeProvider,
			handleBatch: HandleBatchAsync,
			rescan: () => Walk(isRescan: true),
			rootMissing: DropRoot,
			reportFault
		);

		_items = new Dictionary<string, QuickPickItem>(pathComparison.Comparer);
		_folders = new HashSet<string>(pathComparison.Comparer);
	}

	/// <summary>Raised on a pool thread after the list changed.</summary>
	internal event EventHandler? Changed;

	/// <summary>The comparison that decides which paths are one file, which the provider keys its own tables by.</summary>
	internal LocalPathComparisonPolicy PathComparison { get; }

	/// <summary>The number of walks that were installed, which a test reads to tell a change applied by the watcher from a walk.</summary>
	internal int WalkCount => Volatile.Read(ref _walkCount);

	/// <summary>Starts the watchers and the first walk when no query did, and waits for the first walk.</summary>
	/// <param name="cancellationToken">A token that stops the wait, not the walk.</param>
	/// <returns>A task that completes when the list holds the first walk.</returns>
	/// <exception cref="ObjectDisposedException">The tracker was disposed before the call.</exception>
	/// <exception cref="OperationCanceledException">The tracker was disposed during the wait.</exception>
	internal async Task EnsureStartedAsync(CancellationToken cancellationToken)
	{
		Task ready;

		lock (_stateLock)
		{
			ObjectDisposedException.ThrowIf(_isDisposed, this);

			// The query token stops the wait of one query, not the shared walk, which only a disposal stops.
			_ready ??= Task.Run(StartAndWalk, CancellationToken.None);
			ready = _ready;
		}

		if (ready.IsCompleted)
			_watchers.RetryUnwatched();

		await ready.WaitAsync(cancellationToken).ConfigureAwait(false);

		ThrowIfStopped();
	}

	/// <summary>Ends a wait that outlived the tracker.</summary>
	/// <exception cref="OperationCanceledException">The tracker was disposed.</exception>
	internal void ThrowIfStopped() => _lifetimeToken.ThrowIfCancellationRequested();

	/// <summary>Gets the list as it is now.</summary>
	/// <returns>The files in list order; an empty list after disposal.</returns>
	internal WorkspaceFileSnapshot Capture()
	{
		lock (_stateLock)
		{
			if (_snapshot is not null)
				return _snapshot;

			QuickPickItem[] items = [.. _items.Values];

			Array.Sort(items, CompareItems);

			Dictionary<string, int> indexByPath = new(items.Length, PathComparison.Comparer);

			for (int index = 0; index < items.Length; index++)
				indexByPath[items[index].Key] = index;

			return _snapshot = new WorkspaceFileSnapshot(items, indexByPath);
		}
	}

	/// <summary>Stops the watchers and the walk, and empties the list.</summary>
	public void Dispose()
	{
		lock (_stateLock)
		{
			if (_isDisposed)
				return;

			_isDisposed = true;
			_items = new Dictionary<string, QuickPickItem>(PathComparison.Comparer);
			_folders = new HashSet<string>(PathComparison.Comparer);
			_pending = null;
			_snapshot = null;
		}

		_lifetime.Cancel();

		_watchers.Dispose();
		_lifetime.Dispose();
	}

	private static int CompareItems(QuickPickItem left, QuickPickItem right)
	{
		int byFolder = string.Compare(left.Description, right.Description, StringComparison.OrdinalIgnoreCase);

		if (byFolder != 0)
			return byFolder;

		int byName = string.Compare(left.Label, right.Label, StringComparison.OrdinalIgnoreCase);

		return byName != 0 ? byName : string.CompareOrdinal(left.Key, right.Key);
	}

	private bool IsBelow(string root, string path) => path.Length > root.Length && LocalPath.IsDirectoryOrDescendant(path, root, PathComparison);

	private void StartAndWalk()
	{
		// The watchers start first, so a change made while the walk runs reaches the tracker as a held-back report.
		_watchers.StartAll();

		try
		{
			Walk(isRescan: false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			// The first walk is awaited by the queries, so a fault is reported and the list stays empty instead of failing every query.
			_reportFault(exception);
		}
	}

	private void Walk(bool isRescan)
	{
		lock (_walkLock)
		{
			// Taking the reconcile lock first lets a change that is being applied finish, and the walk reads the file system after it.
			lock (_reconcileLock)
			{
				lock (_stateLock)
				{
					if (_isDisposed)
						return;

					_pending = [];
				}
			}

			Dictionary<string, QuickPickItem> items = new(PathComparison.Comparer);
			HashSet<string> folders = new(PathComparison.Comparer);

			try
			{
				// The roots take the bound in order, so an earlier root can leave none of it for a later one.
				for (int index = 0; index < _roots.Length && items.Count < _maxFileCount; index++)
					WalkRoot(index, items, folders);
			}
			catch (OperationCanceledException)
			{
				// The tracker was disposed while it walked, and a disposed tracker keeps no list.
				return;
			}

			bool changed;

			lock (_reconcileLock)
			{
				HashSet<string> held;

				lock (_stateLock)
				{
					if (_isDisposed)
						return;

					_items = items;
					_folders = folders;
					_snapshot = null;
					held = _pending ?? [];
					_pending = null;
					_walkCount++;
				}

				changed = Reconcile(held);
			}

			if (isRescan || changed)
				RaiseChanged();
		}
	}

	private void WalkRoot(int index, Dictionary<string, QuickPickItem> items, HashSet<string> folders)
	{
		string root = _roots[index];

		string? lastFolder = null;
		string? lastDescription = null;

		try
		{
			foreach (string path in _enumerate(root, root, _filter, _lifetimeToken))
			{
				if (items.Count >= _maxFileCount)
					return;

				string folder = Path.GetDirectoryName(path) ?? root;

				// The files of one folder come out together, so they share one description.
				if (!string.Equals(folder, lastFolder, StringComparison.Ordinal))
				{
					lastFolder = folder;
					lastDescription = LocalPath.DescribeFolder(_roots, folder, PathComparison);
				}

				if (items.TryAdd(path, CreateItem(path, lastDescription)))
					AddFolders(folders, root, folder);
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_reportFault(exception);
		}
	}

	// A root that is gone lists no file: its owner saw its watcher fail or fail to start, and the root does not exist.
	private void DropRoot(int index)
	{
		bool dropped;

		lock (_walkLock)
		{
			lock (_reconcileLock)
			{
				lock (_stateLock)
				{
					if (_isDisposed)
						return;

					string root = _roots[index];
					string[] keys = [.. _items.Keys.Where(key => LocalPath.IsDirectoryOrDescendant(key, root, PathComparison))];

					foreach (string key in keys)
						_items.Remove(key);

					_folders.RemoveWhere(folder => LocalPath.IsDirectoryOrDescendant(folder, root, PathComparison));

					dropped = keys.Length > 0;

					if (dropped)
						_snapshot = null;
				}
			}
		}

		if (dropped)
			RaiseChanged();
	}

	private Task HandleBatchAsync(FileChangeBatch batch)
	{
		bool changed;

		lock (_reconcileLock)
		{
			lock (_stateLock)
			{
				if (_isDisposed)
					return Task.CompletedTask;

				// While a walk runs, its result would replace what the batch changes, so the batch waits for the walk.
				if (_pending is not null)
				{
					foreach (FileChange change in batch.Entries.Where(static change => change.Kind != FileChangeKind.Changed))
						_pending.Add(change.Path);

					return Task.CompletedTask;
				}
			}

			changed = Reconcile(batch.Entries.Where(static change => change.Kind != FileChangeKind.Changed).Select(static change => change.Path));
		}

		if (changed)
			RaiseChanged();

		return Task.CompletedTask;
	}

	private bool Reconcile(IEnumerable<string> paths)
	{
		bool changed = false;

		foreach (string path in paths)
		{
			try
			{
				changed |= ReconcilePath(path);
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				// One path that cannot be applied must not keep the others of its batch out of the list.
				_reportFault(exception);
			}
		}

		return changed;
	}

	// Brings the list in line with what the file system holds at one path now.
	private bool ReconcilePath(string path)
	{
		int index = IndexOfRoot(path);

		if (index < 0 || _filter.ExcludesPath(Path.GetRelativePath(_roots[index], path).Replace('\\', '/')))
			return false;

		FileAttributes attributes;

		try
		{
			attributes = File.GetAttributes(path);
		}
		catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
		{
			return Remove(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
		{
			// A path that cannot be read now is neither listed nor dropped, and the report of its next change decides.
			return false;
		}

		// A link is not listed, because the walk does not enter one.
		if ((attributes & FileAttributes.ReparsePoint) != 0)
			return Remove(path);

		string actual = ToSpellingOnDisk(path);

		return (attributes & FileAttributes.Directory) != 0 ? AddTree(index, actual) : Add(index, actual);
	}

	// A report spells the path as the change named it, which after a rename that only changed the casing is the old spelling, so the name on disk wins.
	private string ToSpellingOnDisk(string path)
	{
		if (!PathComparison.IgnoreCase)
			return path;

		string name = Path.GetFileName(path);
		string? parent = Path.GetDirectoryName(path);

		if (parent is null || name.Length == 0)
			return path;

		try
		{
			foreach (string entry in Directory.EnumerateFileSystemEntries(parent, name))
			{
				string entryName = Path.GetFileName(entry);

				if (string.Equals(entryName, name, StringComparison.OrdinalIgnoreCase))
					return Path.Combine(parent, entryName);
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
		{
			// The folder cannot be listed now, so the spelling of the report stands until the next report.
		}

		return path;
	}

	private bool AddTree(int index, string folder)
	{
		bool added = false;

		try
		{
			foreach (string path in _enumerate(folder, _roots[index], _filter, _lifetimeToken))
				added |= Add(index, path);
		}
		catch (OperationCanceledException)
		{
			// A disposal stops the listing, and what it listed so far is dropped with the list.
		}
		catch (Exception exception)
		{
			_reportFault(exception);
		}

		return added;
	}

	private bool Add(int index, string path)
	{
		lock (_stateLock)
		{
			if (_isDisposed)
				return false;

			if (_items.TryGetValue(path, out QuickPickItem? existing))
			{
				// The file is listed, so only another spelling of its name, from a rename that changed the casing alone, changes the entry.
				if (string.Equals(existing.Key, path, StringComparison.Ordinal))
					return false;

				_items.Remove(path);
			}
			else if (_items.Count >= _maxFileCount)
			{
				return false;
			}

			string folder = Path.GetDirectoryName(path) ?? _roots[index];

			_items.Add(path, CreateItem(path, LocalPath.DescribeFolder(_roots, folder, PathComparison)));
			AddFolders(_folders, _roots[index], folder);
			_snapshot = null;

			return true;
		}
	}

	private bool Remove(string path)
	{
		lock (_stateLock)
		{
			bool removed = _items.Remove(path);

			// A folder is no key of the list, so the folders that hold files tell a deleted or moved folder from a deleted file.
			if (_folders.Remove(path))
			{
				foreach (string key in _items.Keys.Where(key => LocalPath.IsDirectoryOrDescendant(key, path, PathComparison)).ToArray())
					_items.Remove(key);

				_folders.RemoveWhere(folder => LocalPath.IsDirectoryOrDescendant(folder, path, PathComparison));
				removed = true;
			}

			if (removed)
				_snapshot = null;

			return removed;
		}
	}

	private int IndexOfRoot(string path)
	{
		for (int index = 0; index < _roots.Length; index++)
		{
			if (IsBelow(_roots[index], path))
				return index;
		}

		return -1;
	}

	// Records the folder and the ones above it, up to the root, so a deleted folder is known as one.
	private static void AddFolders(HashSet<string> folders, string root, string folder)
	{
		string? current = folder;

		while (current is { } && current.Length > root.Length && folders.Add(current))
			current = Path.GetDirectoryName(current);
	}

	private QuickPickItem CreateItem(string path, string? description) =>
		new(path, Path.GetFileName(path)) { Description = description, Group = _fileGroup };

	private void RaiseChanged() => CallbackContainment.TryRun(() => Changed?.Invoke(this, EventArgs.Empty), _reportFault);
}
