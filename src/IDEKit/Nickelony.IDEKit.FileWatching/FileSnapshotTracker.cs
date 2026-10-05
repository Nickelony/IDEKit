namespace Nickelony.IDEKit.FileWatching;

/// <summary>Tracks the subset of workspace paths that its owner follows, so watcher recovery can reconcile missed file changes.</summary>
/// <remarks>
/// <para>
/// A full walk (see <see cref="FileSnapshotWalker"/>) replaces the tracked snapshot, and the forwarded changes update it. Each claims an ordering
/// ticket before it reads the file system, so a walk or a batch that started earlier never overwrites what a later one committed.
/// </para>
/// </remarks>
public sealed class FileSnapshotTracker : IFileSnapshotTracker
{
	private readonly ILogger _logger;
	private readonly FileSnapshotWalker _walker;

	private readonly object _snapshotSyncRoot = new();
	private readonly LocalPathComparisonPolicy _pathComparison;

	private Dictionary<string, FileSnapshotEntry> _trackedSnapshot;

	// Bumped whenever a capture replaces the tracked snapshot; ApplyChanges re-validates against it so an update
	// computed against an older snapshot cannot clobber a newer capture.
	private long _snapshotVersion;

	// Apply-order tickets: a batch that started computing earlier must not overwrite entries committed by a batch
	// that started later, so commits are ordered per path by the ticket each batch claims before it reads the file system.
	// A capture also claims a ticket before its walk, so a capture can tell which paths an interleaved
	// apply committed after the walk began. Markers are dropped as soon as no overlapping operation can still
	// compare against them (see EndTicketOperation), so the map stays bounded by the operations in flight.
	private long _nextApplyTicket;
	private readonly Dictionary<string, long> _lastCommittedApplyTickets;

	// Tickets of the captures and applies that are currently in flight, so the per-path markers can be pruned to
	// the ones an in-flight operation may still need. Guarded by _snapshotSyncRoot.
	private readonly SortedSet<long> _inFlightTickets = [];

	/// <summary>
	/// The internal test hooks that interleave capture and apply operations deterministically, or <see cref="FileSnapshotTrackerTestHooks.None"/>.
	/// </summary>
	/// <remarks>
	/// Internal and reachable only through <c>InternalsVisibleTo</c>; production leaves the default, so every hook branch is skipped.
	/// </remarks>
	internal FileSnapshotTrackerTestHooks TestHooks { get; init; } = FileSnapshotTrackerTestHooks.None;

	/// <summary>The number of per-path commit markers currently retained for the overlapping capture or apply operations.</summary>
	/// <remarks>
	/// Internal diagnostic view; not part of the supported surface.
	/// </remarks>
	internal int LastCommittedApplyTicketCount
	{
		get
		{
			lock (_snapshotSyncRoot)
				return _lastCommittedApplyTickets.Count;
		}
	}

	/// <param name="workspaceRootDirectoryPath">The workspace root directory whose files are captured into the tracked snapshot.</param>
	/// <param name="watchSpecifications">The file patterns that should participate in the tracked snapshot. The list is copied on assignment.</param>
	/// <param name="pathComparison">
	/// The comparison that decides which paths are one file, or <see langword="null"/> for the current platform's.
	/// </param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="workspaceRootDirectoryPath"/> or <paramref name="watchSpecifications"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="watchSpecifications"/> is empty, or one of the specification filters is <see langword="null"/>, empty, or whitespace-only,
	/// is <c>.</c> or <c>..</c>, is rooted, or contains a directory separator.
	/// </exception>
	public FileSnapshotTracker(
		string workspaceRootDirectoryPath,
		IReadOnlyList<FileWatchSpecification> watchSpecifications,
		LocalPathComparisonPolicy? pathComparison = null,
		ILogger? logger = null
	)
	{
		ArgumentNullException.ThrowIfNull(workspaceRootDirectoryPath);
		ArgumentNullException.ThrowIfNull(watchSpecifications);

		if (watchSpecifications.Count == 0)
			throw new ArgumentException("At least one watch specification is required.", nameof(watchSpecifications));

		for (int i = 0; i < watchSpecifications.Count; i++)
			FileWatchSpecification.Validate(watchSpecifications[i], nameof(watchSpecifications));

		_logger = logger ?? NullLogger.Instance;

		_pathComparison = pathComparison ?? LocalPathComparisonPolicy.ForCurrentPlatform;
		_trackedSnapshot = new Dictionary<string, FileSnapshotEntry>(_pathComparison.Comparer);
		_lastCommittedApplyTickets = new Dictionary<string, long>(_pathComparison.Comparer);
		_walker = new FileSnapshotWalker(workspaceRootDirectoryPath, watchSpecifications, pathComparison: _pathComparison, logger: _logger);
	}

	/// <summary>Replaces the tracked snapshot with a fresh capture of the current workspace state.</summary>
	public void CaptureTrackedSnapshot() => PublishCapturedSnapshot(clone: false);

	/// <summary>Creates a stable clone of the currently tracked workspace snapshot.</summary>
	public Dictionary<string, FileSnapshotEntry> CloneTrackedSnapshot()
	{
		lock (_snapshotSyncRoot)
			return CloneSnapshot(_trackedSnapshot);
	}

	/// <summary>Captures the current workspace state, replaces the tracked snapshot, and returns a stable clone of the fresh snapshot.</summary>
	/// <returns>The newly captured snapshot as a caller-owned clone that later tracked changes cannot mutate.</returns>
	public Dictionary<string, FileSnapshotEntry> ReplaceTrackedSnapshotWithCurrent() => PublishCapturedSnapshot(clone: true);

	// Publishes a fresh capture as the tracked snapshot. The capture claims an ordering ticket before the walk so
	// that observations an apply committed while the walk was running survive the replacement: by the same ticket
	// rule two applies use, an apply that claimed a later slot is newer than the walk result the capture read
	// earlier. Without that merge a capture could resurrect stale entries over a concurrent apply's commit.
	private Dictionary<string, FileSnapshotEntry> PublishCapturedSnapshot(bool clone)
	{
		long captureTicket;

		lock (_snapshotSyncRoot)
		{
			captureTicket = ++_nextApplyTicket;
			BeginTicketOperation(captureTicket);
		}

		// A capture against a briefly-missing root yields an empty set; installing it would make the next delta
		// report every file as Created, so the previous snapshot is kept instead.
		if (!_walker.RootExists())
		{
			lock (_snapshotSyncRoot)
			{
				EndTicketOperation(captureTicket);
				return clone ? CloneSnapshot(_trackedSnapshot) : _trackedSnapshot;
			}
		}

		Dictionary<string, FileSnapshotEntry> snapshot = _walker.Walk();

		TestHooks.BeforeCapturePublish?.Invoke();

		lock (_snapshotSyncRoot)
		{
			foreach ((string path, long committedTicket) in _lastCommittedApplyTickets)
			{
				if (committedTicket <= captureTicket)
					continue;

				if (_trackedSnapshot.TryGetValue(path, out FileSnapshotEntry committedEntry))
					snapshot[path] = committedEntry;
				else
					snapshot.Remove(path);
			}

			_trackedSnapshot = snapshot;
			_snapshotVersion++;

			// Release this capture's ticket; the markers that no overlapping operation can still compare against
			// are dropped here, so the marker map stays bounded.
			EndTicketOperation(captureTicket);

			// The tracked snapshot is mutated in place by later ApplyChanges calls; the clone must run under the
			// snapshot lock, otherwise a concurrent apply can mutate the dictionary while the clone enumerates it.
			return clone ? CloneSnapshot(snapshot) : snapshot;
		}
	}

	/// <summary>Applies a set of already forwarded file changes to the tracked snapshot.</summary>
	/// <param name="changes">The normalized forwarded file changes. Entries whose path is empty or whitespace-only are ignored.</param>
	/// <remarks>
	/// Concurrent calls are ordered per path by the ticket each call claims before it reads the file system, so a batch that started
	/// computing earlier cannot overwrite entries that a later-started batch already committed for the same path; observations for unrelated paths
	/// are still applied. The file reads and content hashing for one call run outside the snapshot lock.
	/// </remarks>
	public void ApplyChanges(IReadOnlyList<FileChange> changes)
	{
		ArgumentNullException.ThrowIfNull(changes);

		long applyTicket;

		lock (_snapshotSyncRoot)
		{
			applyTicket = ++_nextApplyTicket;
			BeginTicketOperation(applyTicket);
		}

		// File reads and content hashing happen outside the lock; only the snapshot mutation is serialized.
		var updates = new List<(string Path, FileSnapshotEntry? Entry)>(changes.Count);
		long startingSnapshotVersion;

		try
		{
			TestHooks.BeforeSnapshotVersionRead?.Invoke();

			// Stamp the observation time into the version fence: reading the version as late as possible before the
			// file reads makes the fence describe when this apply observed the file system, not when it claimed its
			// ticket. A capture that replaced the snapshot while this thread was preempted between the claim and the read is then
			// already reflected in the stamped version, so this apply's own (newer) observations are committed
			// instead of being discarded as stale.
			lock (_snapshotSyncRoot)
				startingSnapshotVersion = _snapshotVersion;

			for (int i = 0; i < changes.Count; i++)
			{
				FileChange change = changes[i];

				if (string.IsNullOrWhiteSpace(change.Path))
				{
					_logger.LogDebug("Ignored a {Kind} workspace change without a usable path.", change.Kind);
					continue;
				}

				if (change.Kind == FileChangeKind.Deleted)
				{
					updates.Add((change.Path, null));
					continue;
				}

				if (FileSnapshotWalker.TryCreateEntry(change.Path, out FileSnapshotEntry entry, _logger))
					updates.Add((change.Path, entry));
				else if (FileSnapshotWalker.TryDeterminePathMissing(change.Path, out bool isMissing, _logger) && isMissing)
					updates.Add((change.Path, null));
			}

			TestHooks.BeforeApplyCommit?.Invoke();
		}
		catch
		{
			// A failure while the updates are computed must still release this apply's ticket, or the leaked
			// low ticket would pin the in-flight floor and stop the per-path commit markers from being pruned.
			lock (_snapshotSyncRoot)
				EndTicketOperation(applyTicket);

			throw;
		}

		lock (_snapshotSyncRoot)
			CommitUpdates(updates, startingSnapshotVersion, applyTicket);
	}

	// Re-validates the updates computed outside the lock against the snapshot version and commits them in
	// per-path ticket order. The caller must hold _snapshotSyncRoot.
	private void CommitUpdates(List<(string Path, FileSnapshotEntry? Entry)> updates, long startingSnapshotVersion, long applyTicket)
	{
		try
		{
			// A capture that replaced the snapshot while these updates were computed already reflects the current
			// file-system state, so applying the older lock-free updates would clobber it.
			if (_snapshotVersion != startingSnapshotVersion)
			{
				_logger.LogDebug(
					"Skipped {Count} tracked workspace change(s) because the tracked snapshot was replaced while their updates were being computed.",
					updates.Count
				);

				return;
			}

			// Commits are ordered per path: a concurrent apply that started later may already have committed newer
			// file-system observations for some paths, and only those paths are skipped here; observations for
			// unrelated paths from this (older) batch are still applied.
			int skippedUpdateCount = 0;

			foreach ((string path, FileSnapshotEntry? entry) in updates)
			{
				if (_lastCommittedApplyTickets.TryGetValue(path, out long committedApplyTicket) && committedApplyTicket > applyTicket)
				{
					skippedUpdateCount++;
					continue;
				}

				if (entry is FileSnapshotEntry capturedEntry)
					_trackedSnapshot[path] = capturedEntry;
				else
					_trackedSnapshot.Remove(path);

				_lastCommittedApplyTickets[path] = applyTicket;
			}

			if (skippedUpdateCount > 0)
			{
				_logger.LogDebug(
					"Skipped {Count} tracked workspace change(s) because a newer apply already committed those paths.",
					skippedUpdateCount
				);
			}
		}
		finally
		{
			// Release this apply's ticket and drop the markers no overlapping operation can still compare
			// against, so the map stays bounded by the operations in flight instead of growing per path.
			EndTicketOperation(applyTicket);
		}
	}

	// Registers a capture or apply ticket as in flight so its per-path commit markers are protected until it
	// finishes. The caller must hold _snapshotSyncRoot.
	private void BeginTicketOperation(long ticket) => _inFlightTickets.Add(ticket);

	// Releases an operation's ticket and drops the per-path commit markers that no in-flight operation can still
	// compare against, so the marker map stays bounded by the operations that currently overlap instead of growing
	// for the whole session. A marker only suppresses a commit from an operation that claimed a lower ticket, so a
	// marker at or below the oldest in-flight ticket cannot suppress anything. The caller must hold
	// _snapshotSyncRoot.
	private void EndTicketOperation(long ticket)
	{
		_inFlightTickets.Remove(ticket);

		if (_inFlightTickets.Count == 0)
		{
			// No capture or apply is in flight: every later operation claims a higher ticket, so no retained
			// marker can suppress a future commit and the whole map can be released.
			_lastCommittedApplyTickets.Clear();
			return;
		}

		long oldestInFlightTicket = _inFlightTickets.Min;
		List<string>? expiredPaths = null;

		foreach ((string path, long committedTicket) in _lastCommittedApplyTickets)
		{
			if (committedTicket > oldestInFlightTicket)
				continue;

			(expiredPaths ??= []).Add(path);
		}

		if (expiredPaths is null)
			return;

		for (int i = 0; i < expiredPaths.Count; i++)
			_lastCommittedApplyTickets.Remove(expiredPaths[i]);
	}

	/// <inheritdoc/>
	public FileChangeBatch BuildDeltaBatch(
		IReadOnlyDictionary<string, FileSnapshotEntry> previousSnapshot,
		IReadOnlyDictionary<string, FileSnapshotEntry> currentSnapshot
	) =>
		FileSnapshotDiffer.Build(previousSnapshot, currentSnapshot, pathComparison: _pathComparison, isPathMissing: FileSnapshotWalker.IsPathMissing);

	private Dictionary<string, FileSnapshotEntry> CloneSnapshot(Dictionary<string, FileSnapshotEntry> snapshot) =>
		new(snapshot, _pathComparison.Comparer);
}
