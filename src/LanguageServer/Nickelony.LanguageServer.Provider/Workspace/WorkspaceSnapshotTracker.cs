using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Tracks the subset of workspace paths mirrored to a language server so watcher recovery can reconcile missed file changes.
/// </summary>
/// <remarks>
/// <para>
/// Capture follows the watch specifications and includes hidden and system files because the file-system watcher
/// observes them as well; reparse points (symbolic links and junctions) are excluded because neither capture nor the
/// watcher follows them consistently. Enumeration problems such as a directory that disappears mid-capture are
/// contained and reported at debug level, so capture does not throw for file-system races.
/// </para>
/// </remarks>
internal sealed class WorkspaceSnapshotTracker
{
	/// <summary>
	/// The number of leading and trailing bytes sampled into the content fingerprint of a file that is too large to
	/// hash in full, so the fingerprint cost stays constant instead of reading a multi-megabyte file on the dispatch
	/// path.
	/// </summary>
	private const int FingerprintSampleLength = 8 * 1024;

	private readonly ILogger _logger;

	private readonly string _workspaceRootDirectoryPath;
	private readonly ReadOnlyCollection<WorkspaceWatchSpecification> _watchSpecifications;

	// Enumeration options for the wildcard specifications, precomputed in the constructor. EnumerationOptions is
	// immutable once configured and Directory.EnumerateFiles does not mutate it, so a capture must not rebuild a
	// fresh instance per specification per walk; a non-wildcard specification has no options because its filter
	// names a single path instead of a search pattern.
	private readonly EnumerationOptions?[] _enumerationOptions;

	private readonly object _snapshotSyncRoot = new();
	private Dictionary<string, WorkspaceSnapshotEntry> _trackedSnapshot = new(LanguageServerPaths.LocalPathComparer);

	// Bumped whenever a capture replaces the tracked snapshot; ApplyChanges re-validates against it so an update
	// computed against an older snapshot cannot clobber a newer capture.
	private long _snapshotVersion;

	// Apply-order tickets: a batch that started computing earlier must not overwrite entries committed by a batch
	// that started later, so commits are ordered per path by the ticket assigned when the batch read the snapshot
	// version. A capture also claims a ticket before its walk, so a capture can tell which paths an interleaved
	// apply committed after the walk began. Markers are dropped as soon as no overlapping operation can still
	// compare against them (see EndTicketOperation), so the map stays bounded by the operations in flight.
	private long _nextApplyTicket;
	private readonly Dictionary<string, long> _lastCommittedApplyTickets = new(LanguageServerPaths.LocalPathComparer);

	// Tickets of the captures and applies that are currently in flight, so the per-path markers can be pruned to
	// the ones an in-flight operation may still need. Guarded by _snapshotSyncRoot.
	private readonly SortedSet<long> _inFlightTickets = [];

	/// <summary>
	/// Gets the test hooks the package tests install to interleave capture and apply operations deterministically, or
	/// <see cref="WorkspaceSnapshotTrackerTestHooks.None"/>.
	/// </summary>
	/// <remarks>
	/// Supplied through the object initializer by the package tests (the type is internal and reachable only through
	/// <c>InternalsVisibleTo</c>); production callers leave the default, so every hook branch is skipped.
	/// </remarks>
	internal WorkspaceSnapshotTrackerTestHooks TestHooks { get; init; } = WorkspaceSnapshotTrackerTestHooks.None;

	/// <summary>
	/// Gets the number of per-path commit markers currently retained for the overlapping capture or apply operations.
	/// </summary>
	/// <remarks>Internal diagnostic view used by the package tests; not part of the supported surface.</remarks>
	internal int LastCommittedApplyTicketCount
	{
		get
		{
			lock (_snapshotSyncRoot)
				return _lastCommittedApplyTickets.Count;
		}
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceSnapshotTracker"/> class.
	/// </summary>
	/// <param name="workspaceRootDirectoryPath">The workspace root directory whose files are captured into the tracked snapshot.</param>
	/// <param name="watchSpecifications">The file patterns that should participate in the tracked snapshot. The list is copied on assignment.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="workspaceRootDirectoryPath"/> is empty or whitespace-only,
	/// <paramref name="watchSpecifications"/> is empty, or one of the specification filters is <see langword="null"/>,
	/// empty, or whitespace-only, is rooted, or contains a directory separator.
	/// </exception>
	/// <exception cref="ArgumentNullException"><paramref name="workspaceRootDirectoryPath"/> or <paramref name="watchSpecifications"/> is <see langword="null"/>.</exception>
	public WorkspaceSnapshotTracker(string workspaceRootDirectoryPath, IReadOnlyList<WorkspaceWatchSpecification> watchSpecifications, ILogger? logger = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRootDirectoryPath);
		ArgumentNullException.ThrowIfNull(watchSpecifications);

		if (watchSpecifications.Count == 0)
			throw new ArgumentException("At least one watch specification is required.", nameof(watchSpecifications));

		for (int i = 0; i < watchSpecifications.Count; i++)
			WorkspaceWatchSpecification.Validate(watchSpecifications[i], nameof(watchSpecifications));

		_logger = logger ?? NullLogger.Instance;

		_workspaceRootDirectoryPath = workspaceRootDirectoryPath;
		_watchSpecifications = Array.AsReadOnly([.. watchSpecifications]);
		_enumerationOptions = new EnumerationOptions?[_watchSpecifications.Count];

		for (int i = 0; i < _watchSpecifications.Count; i++)
		{
			WorkspaceWatchSpecification specification = _watchSpecifications[i];

			if (specification.Filter.IndexOfAny(['*', '?']) < 0)
				continue;

			_enumerationOptions[i] = new EnumerationOptions
			{
				IgnoreInaccessible = true,
				RecurseSubdirectories = specification.IncludeSubdirectories,
				ReturnSpecialDirectories = false,
				// Hidden and system files are captured because the file-system watcher observes them; reparse points
				// are skipped so capture cannot escape the workspace through symbolic links or junctions.
				AttributesToSkip = FileAttributes.ReparsePoint,
				// Capture must apply the same simple wildcard grammar the watch specification matcher uses, so the
				// captured scope and the forwarded-event scope cannot disagree on exotic patterns.
				MatchType = MatchType.Simple
			};
		}
	}

	/// <summary>
	/// Replaces the tracked snapshot with a fresh capture of the current workspace state.
	/// </summary>
	public void CaptureTrackedSnapshot() => PublishCapturedSnapshot(clone: false);

	/// <summary>
	/// Creates a stable clone of the currently tracked workspace snapshot.
	/// </summary>
	/// <returns>The cloned snapshot.</returns>
	public Dictionary<string, WorkspaceSnapshotEntry> CloneTrackedSnapshot()
	{
		lock (_snapshotSyncRoot)
			return CloneSnapshot(_trackedSnapshot);
	}

	/// <summary>
	/// Captures the current workspace state, replaces the tracked snapshot, and returns a stable clone of the fresh snapshot.
	/// </summary>
	/// <returns>The newly captured snapshot as a caller-owned clone that later tracked changes cannot mutate.</returns>
	public Dictionary<string, WorkspaceSnapshotEntry> ReplaceTrackedSnapshotWithCurrent() => PublishCapturedSnapshot(clone: true);

	// Publishes a fresh capture as the tracked snapshot. The capture claims an ordering ticket before the walk so
	// that observations an apply committed while the walk was running survive the replacement: by the same ticket
	// rule two applies use, an apply that claimed a later slot is newer than the walk result the capture read
	// earlier. Without that merge a capture could resurrect stale entries over a concurrent apply's commit.
	private Dictionary<string, WorkspaceSnapshotEntry> PublishCapturedSnapshot(bool clone)
	{
		long captureTicket;

		lock (_snapshotSyncRoot)
		{
			captureTicket = ++_nextApplyTicket;
			BeginTicketOperation(captureTicket);
		}

		// A capture against a briefly-missing root yields an empty set; installing it would make the next delta
		// report every file as Created, so the previous snapshot is kept instead.
		if (!Directory.Exists(_workspaceRootDirectoryPath))
		{
			lock (_snapshotSyncRoot)
			{
				EndTicketOperation(captureTicket);
				return clone ? CloneSnapshot(_trackedSnapshot) : _trackedSnapshot;
			}
		}

		Dictionary<string, WorkspaceSnapshotEntry> snapshot = CaptureSnapshot();

		TestHooks.BeforeCapturePublish?.Invoke();
		lock (_snapshotSyncRoot)
		{
			foreach ((string path, long committedTicket) in _lastCommittedApplyTickets)
			{
				if (committedTicket <= captureTicket)
					continue;

				if (_trackedSnapshot.TryGetValue(path, out WorkspaceSnapshotEntry committedEntry))
					snapshot[path] = committedEntry;
				else
					snapshot.Remove(path);
			}

			_trackedSnapshot = snapshot;
			_snapshotVersion++;

			// Release this capture's ticket; the markers that no overlapping operation can still compare against
			// are dropped here, so the map does not retain the entries of the replaced snapshot.
			EndTicketOperation(captureTicket);

			// The tracked snapshot is mutated in place by later ApplyChanges calls; the clone must run under the
			// snapshot lock, otherwise a concurrent apply can mutate the dictionary while the clone enumerates it.
			return clone ? CloneSnapshot(snapshot) : snapshot;
		}
	}

	/// <summary>
	/// Applies a set of already forwarded file changes to the tracked snapshot.
	/// </summary>
	/// <param name="changes">The normalized forwarded file changes. Entries whose path is empty or whitespace-only are ignored.</param>
	/// <remarks>
	/// Concurrent calls are ordered per path by the ticket captured when each call read the snapshot version, so a
	/// batch that started computing earlier cannot overwrite entries that a later-started batch already committed for
	/// the same path; observations for unrelated paths are still applied. The file reads and content hashing for one
	/// call run outside the snapshot lock.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
	public void ApplyChanges(IReadOnlyList<WorkspaceFileChange> changes)
	{
		ArgumentNullException.ThrowIfNull(changes);

		long startingSnapshotVersion;
		long applyTicket;

		lock (_snapshotSyncRoot)
		{
			startingSnapshotVersion = _snapshotVersion;
			applyTicket = ++_nextApplyTicket;
			BeginTicketOperation(applyTicket);
		}

		// File reads and content hashing happen outside the lock; only the snapshot mutation is serialized.
		var updates = new List<(string Path, WorkspaceSnapshotEntry? Entry)>(changes.Count);

		try
		{
			TestHooks.SnapshotVersionRead?.Invoke();

			for (int i = 0; i < changes.Count; i++)
			{
				WorkspaceFileChange change = changes[i];

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

				if (TryCreateSnapshotEntry(change.Path, out WorkspaceSnapshotEntry entry, _logger))
				{
					updates.Add((change.Path, entry));
				}
				else if (TryDeterminePathMissing(change.Path, out bool isMissing, _logger) && isMissing)
				{
					updates.Add((change.Path, null));
				}
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
	private void CommitUpdates(
		List<(string Path, WorkspaceSnapshotEntry? Entry)> updates,
		long startingSnapshotVersion,
		long applyTicket)
	{
		try
		{
			// A capture that replaced the snapshot while these updates were computed already reflects the current
			// file-system state, so applying the older lock-free updates would clobber it.
			if (_snapshotVersion != startingSnapshotVersion)
			{
				_logger.LogDebug("Skipped {Count} tracked workspace change(s) because the tracked snapshot was replaced while their updates were being computed.", updates.Count);
				return;
			}

			// Commits are ordered per path: a concurrent apply that started later may already have committed newer
			// file-system observations for some paths, and only those paths are skipped here; observations for
			// unrelated paths from this (older) batch are still applied.
			int skippedUpdateCount = 0;

			foreach ((string path, WorkspaceSnapshotEntry? entry) in updates)
			{
				if (_lastCommittedApplyTickets.TryGetValue(path, out long committedApplyTicket) && committedApplyTicket > applyTicket)
				{
					skippedUpdateCount++;
					continue;
				}

				if (entry is WorkspaceSnapshotEntry capturedEntry)
					_trackedSnapshot[path] = capturedEntry;
				else
					_trackedSnapshot.Remove(path);

				_lastCommittedApplyTickets[path] = applyTicket;
			}

			if (skippedUpdateCount > 0)
			{
				_logger.LogDebug("Skipped {Count} tracked workspace change(s) because a newer apply already committed those paths.", skippedUpdateCount);
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
	// marker at or below the oldest in-flight ticket can no longer suppress anything. The caller must hold
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

	/// <summary>
	/// Builds a normalized delta batch between two captured workspace snapshots.
	/// </summary>
	/// <param name="previousSnapshot">The baseline snapshot captured before watcher disruption.</param>
	/// <param name="currentSnapshot">The replacement snapshot captured after watcher recovery.</param>
	/// <returns>The sorted workspace change batch needed to reconcile the snapshots.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="previousSnapshot"/> or <paramref name="currentSnapshot"/> is <see langword="null"/>.</exception>
	public static FileChangeBatch BuildDeltaBatch(
		IReadOnlyDictionary<string, WorkspaceSnapshotEntry> previousSnapshot,
		IReadOnlyDictionary<string, WorkspaceSnapshotEntry> currentSnapshot)
	{
		ArgumentNullException.ThrowIfNull(previousSnapshot);
		ArgumentNullException.ThrowIfNull(currentSnapshot);

		var changes = new List<WorkspaceFileChange>();

		foreach ((string path, WorkspaceSnapshotEntry previousEntry) in previousSnapshot)
		{
			if (!currentSnapshot.TryGetValue(path, out WorkspaceSnapshotEntry currentEntry))
			{
				if (TryDeterminePathMissing(path, out bool isMissing) && isMissing)
					changes.Add(new WorkspaceFileChange(path, FileChangeKind.Deleted));

				continue;
			}

			if (!currentEntry.Equals(previousEntry))
				changes.Add(new WorkspaceFileChange(path, FileChangeKind.Changed));
		}

		foreach ((string path, WorkspaceSnapshotEntry _) in currentSnapshot)
		{
			if (!previousSnapshot.ContainsKey(path))
				changes.Add(new WorkspaceFileChange(path, FileChangeKind.Created));
		}

		changes.Sort(static (left, right) => LanguageServerPaths.LocalPathComparer.Compare(left.Path, right.Path));
		return new(changes);
	}

	private Dictionary<string, WorkspaceSnapshotEntry> CaptureSnapshot()
	{
		var snapshot = new Dictionary<string, WorkspaceSnapshotEntry>(LanguageServerPaths.LocalPathComparer);

		if (!Directory.Exists(_workspaceRootDirectoryPath))
			return snapshot;

		for (int i = 0; i < _watchSpecifications.Count; i++)
			CaptureSnapshotForSpecification(snapshot, _watchSpecifications[i], _enumerationOptions[i]);

		return snapshot;
	}

	private void CaptureSnapshotForSpecification(
		Dictionary<string, WorkspaceSnapshotEntry> snapshot,
		WorkspaceWatchSpecification watchSpecification,
		EnumerationOptions? enumerationOptions)
	{
		try
		{
			if (enumerationOptions is null)
			{
				TryAddSnapshotPath(snapshot, Path.Combine(_workspaceRootDirectoryPath, watchSpecification.Filter));
				return;
			}

			foreach (string filePath in Directory.EnumerateFiles(_workspaceRootDirectoryPath, watchSpecification.Filter, enumerationOptions))
				TryAddSnapshotPath(snapshot, filePath);
		}
		catch (Exception exception)
		{
			// Enumeration races (a directory removed mid-capture) and access failures are contained here so provider
			// start and recovery never fail because of file-system churn; the next capture picks the state up.
			_logger.LogDebug(exception, "Failed to enumerate workspace files for filter '{Filter}' under '{Workspace}'.", watchSpecification.Filter, _workspaceRootDirectoryPath);
		}
	}

	private void TryAddSnapshotPath(Dictionary<string, WorkspaceSnapshotEntry> snapshot, string path)
	{
		if (!LanguageServerPaths.TryNormalizeLocalPath(path, out string normalizedPath))
			return;

		if (TryCreateSnapshotEntry(normalizedPath, out WorkspaceSnapshotEntry entry, _logger))
			snapshot[normalizedPath] = entry;
	}

	private static Dictionary<string, WorkspaceSnapshotEntry> CloneSnapshot(Dictionary<string, WorkspaceSnapshotEntry> snapshot)
		=> new(snapshot, LanguageServerPaths.LocalPathComparer);

	/// <summary>
	/// Creates one snapshot entry for a normalized path.
	/// </summary>
	/// <param name="normalizedPath">The normalized file path.</param>
	/// <param name="entry">Receives the captured snapshot entry when the path exists.</param>
	/// <param name="logger">The logger used for unexpected capture failures.</param>
	/// <returns><see langword="true"/> when an entry was captured; otherwise, <see langword="false"/>.</returns>
	private static bool TryCreateSnapshotEntry(string normalizedPath, out WorkspaceSnapshotEntry entry, ILogger? logger = null)
	{
		entry = default;

		try
		{
			FileAttributes attributes = File.GetAttributes(normalizedPath);

			// Reparse points (symbolic links and junctions) are excluded from the tracked snapshot on every
			// capture path, matching the capture walk's AttributesToSkip filter: the watcher does not follow them
			// consistently, so a tracked entry could never agree with a later full capture.
			if ((attributes & FileAttributes.ReparsePoint) != 0)
				return false;

			if ((attributes & FileAttributes.Directory) != 0)
			{
				var directoryInfo = new DirectoryInfo(normalizedPath);
				entry = new WorkspaceSnapshotEntry(IsDirectory: true, directoryInfo.LastWriteTimeUtc.Ticks, 0, 0);
				return true;
			}

			var fileInfo = new FileInfo(normalizedPath);

			entry = new WorkspaceSnapshotEntry(
				IsDirectory: false,
				fileInfo.LastWriteTimeUtc.Ticks,
				fileInfo.Length,
				ComputeFileContentFingerprint(normalizedPath));

			return true;
		}
		catch (Exception exception)
		{
			logger?.LogDebug(exception, "Failed to capture a workspace snapshot entry for '{Path}'.", normalizedPath);
		}

		return false;
	}

	/// <summary>
	/// Computes a compact content fingerprint for one file. The fingerprint disambiguates entries whose timestamp
	/// and length are equal, for example when a file is rewritten twice within one file-system timestamp tick.
	/// </summary>
	/// <remarks>
	/// The fingerprint is computed for every created or changed path, because a tracked entry must stay comparable
	/// with a fresh full capture of the same content; skipping the read would make the two disagree and report a
	/// spurious change. A file up to twice <see cref="FingerprintSampleLength"/> is hashed in full; a larger file is
	/// hashed from a bounded head and tail sample, so the cost stays constant instead of reading a multi-megabyte file
	/// on the dispatch path. The bounded sample can miss a same-length rewrite that changes only the middle of a large
	/// file within one file-system timestamp tick, which is an accepted trade-off for the constant cost.
	/// </remarks>
	/// <param name="normalizedPath">The normalized file path.</param>
	/// <returns>The first eight bytes of the SHA-256 hash over the hashed sample.</returns>
	private static ulong ComputeFileContentFingerprint(string normalizedPath)
	{
		using FileStream stream = File.OpenRead(normalizedPath);

		byte[] hash = stream.Length <= FingerprintSampleLength * 2
			? SHA256.HashData(stream)
			: HashHeadAndTailFingerprint(stream);

		return BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(0, sizeof(ulong)));
	}

	/// <summary>
	/// Hashes a bounded head and tail sample of a file that is too large to hash in full.
	/// </summary>
	/// <param name="stream">The readable file stream, positioned anywhere.</param>
	/// <returns>The SHA-256 hash of the concatenated head and tail samples.</returns>
	private static byte[] HashHeadAndTailFingerprint(FileStream stream)
	{
		byte[] sample = new byte[FingerprintSampleLength * 2];

		stream.ReadExactly(sample, 0, FingerprintSampleLength);
		stream.Seek(-FingerprintSampleLength, SeekOrigin.End);
		stream.ReadExactly(sample, FingerprintSampleLength, FingerprintSampleLength);

		return SHA256.HashData(sample);
	}

	/// <summary>
	/// Determines whether a normalized path is missing from the file system.
	/// </summary>
	/// <param name="normalizedPath">The normalized file path.</param>
	/// <param name="isMissing">Receives whether the path does not exist.</param>
	/// <param name="logger">The logger used for unexpected access failures.</param>
	/// <returns><see langword="true"/> when the missing state was resolved; otherwise, <see langword="false"/>.</returns>
	private static bool TryDeterminePathMissing(string normalizedPath, out bool isMissing, ILogger? logger = null)
	{
		isMissing = false;

		try
		{
			_ = File.GetAttributes(normalizedPath);
			return true;
		}
		catch (DirectoryNotFoundException)
		{
			isMissing = true;
			return true;
		}
		catch (FileNotFoundException)
		{
			isMissing = true;
			return true;
		}
		catch (Exception exception)
		{
			logger?.LogDebug(exception, "Failed to determine whether the workspace path '{Path}' is missing.", normalizedPath);
			return false;
		}
	}
}

/// <summary>
/// Represents the tracked file-system state for a single watched workspace path.
/// </summary>
/// <param name="IsDirectory">Whether the path represents a directory.</param>
/// <param name="LastWriteUtcTicks">The last-write timestamp used for change detection.</param>
/// <param name="Length">The file length used for change detection.</param>
/// <param name="ContentFingerprint">A stable file-content fingerprint used when length and timestamps alone are ambiguous.</param>
internal readonly record struct WorkspaceSnapshotEntry(bool IsDirectory, long LastWriteUtcTicks, long Length, ulong ContentFingerprint);
