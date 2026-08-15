using System.Collections.Concurrent;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Accumulates workspace file changes and coalesces repeated updates per path.
/// </summary>
/// <remarks>
/// <para>
/// Coalescing preserves delete/create replacement semantics together with the path casing of each preserved
/// occurrence and keeps the first pending occurrence order. Collaborators such as the workspace watcher debounce buffer
/// and provider-side change forwarders use the accumulator to hold changes that could not be delivered yet.
/// </para>
/// <para>
/// Draining returns the coalesced changes in first-pending order and clears the accumulator. The accumulator is safe
/// for concurrent callers. It is internal to the framework: the watcher's debounce buffer and the change forwarder
/// are its only consumers, and both live in this package, so no consumer needs it as public surface.
/// </para>
/// </remarks>
internal sealed class WorkspaceChangeAccumulator
{
	/// <summary>
	/// Stores the latest coalesced change for each normalized path.
	/// </summary>
	private readonly ConcurrentDictionary<string, BufferedWorkspaceChange> _changes = new(LanguageServerPaths.LocalPathComparer);

	/// <summary>
	/// Produces deterministic insertion order for buffered paths.
	/// </summary>
	private long _nextSequence;

	/// <summary>
	/// Stores the coalesced change kind, the order in which the path first became pending, and the path casing of each
	/// occurrence that still contributes to the coalesced change.
	/// </summary>
	/// <param name="Sequence">The first pending occurrence order for the path.</param>
	/// <param name="PrimaryPath">The path casing of the most recent occurrence that still contributes to the primary change kind (a later occurrence can refresh the casing without replacing the kind).</param>
	/// <param name="PrimaryKind">The first effective coalesced change kind.</param>
	/// <param name="SecondaryPath">The path casing of the occurrence that owns the secondary change kind.</param>
	/// <param name="SecondaryKind">The second effective coalesced change kind when a delete/create pair must be preserved.</param>
	private readonly record struct BufferedWorkspaceChange(long Sequence, string PrimaryPath, FileChangeKind PrimaryKind, string? SecondaryPath = null, FileChangeKind? SecondaryKind = null);

	/// <summary>
	/// Gets a value indicating whether the accumulator currently holds no pending changes.
	/// </summary>
	public bool IsEmpty => _changes.IsEmpty;

	/// <summary>
	/// Adds or merges a single workspace file change into the accumulator.
	/// </summary>
	/// <param name="path">The normalized file path. Changes with an empty or whitespace-only path are ignored.</param>
	/// <param name="kind">The incoming change kind. Undefined kind values are forwarded unchanged.</param>
	public void Add(string path, FileChangeKind kind)
	{
		if (string.IsNullOrWhiteSpace(path))
			return;

		long newSequence = Interlocked.Increment(ref _nextSequence);

		while (true)
		{
			if (!_changes.TryGetValue(path, out BufferedWorkspaceChange existingChange))
			{
				if (_changes.TryAdd(path, new BufferedWorkspaceChange(newSequence, path, kind)))
					return;

				continue;
			}

			if (!TryCombine(existingChange, path, kind, out BufferedWorkspaceChange combinedChange))
			{
				if (_changes.TryRemove(new KeyValuePair<string, BufferedWorkspaceChange>(path, existingChange)))
					return;

				continue;
			}

			if (_changes.TryUpdate(path, combinedChange, existingChange))
				return;
		}
	}

	/// <summary>
	/// Adds or merges a sequence of workspace file changes into the accumulator.
	/// </summary>
	/// <param name="changes">The changes to merge.</param>
	/// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
	public void AddRange(IReadOnlyList<WorkspaceFileChange> changes)
	{
		ArgumentNullException.ThrowIfNull(changes);

		for (int i = 0; i < changes.Count; i++)
			Add(changes[i].Path, changes[i].Kind);
	}

	/// <summary>
	/// Drains all pending changes into a batch ready for forwarding.
	/// </summary>
	/// <returns>The drained file-change batch in first-pending order; the accumulator is empty afterwards.</returns>
	public FileChangeBatch DrainBatch()
	{
		var drainedChanges = new List<BufferedWorkspaceChangeEntry>();

		foreach (KeyValuePair<string, BufferedWorkspaceChange> entry in _changes)
		{
			if (_changes.TryRemove(entry.Key, out BufferedWorkspaceChange change))
				drainedChanges.Add(new BufferedWorkspaceChangeEntry(change.Sequence, change.PrimaryPath, change.PrimaryKind, change.SecondaryPath, change.SecondaryKind));
		}

		drainedChanges.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));

		var changes = new List<WorkspaceFileChange>(drainedChanges.Count);

		for (int i = 0; i < drainedChanges.Count; i++)
		{
			changes.Add(new WorkspaceFileChange(drainedChanges[i].PrimaryPath, drainedChanges[i].PrimaryKind));

			if (drainedChanges[i].SecondaryPath is string secondaryPath && drainedChanges[i].SecondaryKind is FileChangeKind secondaryKind)
				changes.Add(new WorkspaceFileChange(secondaryPath, secondaryKind));
		}

		return new FileChangeBatch(changes);
	}

	/// <summary>
	/// Stores one drained buffered change together with its preserved ordering metadata and path casings.
	/// </summary>
	/// <param name="Sequence">The first pending occurrence order for the path.</param>
	/// <param name="PrimaryPath">The path casing of the most recent occurrence that still contributes to the primary change kind.</param>
	/// <param name="PrimaryKind">The first effective coalesced change kind.</param>
	/// <param name="SecondaryPath">The path casing of the occurrence that owns the secondary change kind.</param>
	/// <param name="SecondaryKind">The second effective coalesced change kind when a delete/create pair must be preserved.</param>
	private readonly record struct BufferedWorkspaceChangeEntry(long Sequence, string PrimaryPath, FileChangeKind PrimaryKind, string? SecondaryPath, FileChangeKind? SecondaryKind);

	/// <summary>
	/// Combines an existing and incoming change into the effective change that should be forwarded.
	/// </summary>
	/// <param name="existing">The buffered change already stored for the path.</param>
	/// <param name="incomingPath">The path casing of the incoming change.</param>
	/// <param name="incoming">The new incoming change kind.</param>
	/// <param name="combined">Receives the coalesced effective change state when one remains.</param>
	/// <returns><see langword="true"/> when a coalesced change should remain buffered; otherwise, <see langword="false"/>.</returns>
	private static bool TryCombine(BufferedWorkspaceChange existing, string incomingPath, FileChangeKind incoming, out BufferedWorkspaceChange combined)
	{
		if (existing.SecondaryKind is FileChangeKind secondaryKind)
		{
			combined = TryCombineDeleteCreatePair(existing, incomingPath, secondaryKind, incoming);
			return true;
		}

		switch (existing.PrimaryKind)
		{
			case FileChangeKind.Created:
				if (incoming == FileChangeKind.Deleted)
				{
					// A same-path create/delete pair within one debounce window cancels, but a case-only replacement
					// must stay observable: the delete belongs to the previous casing and the server has to learn both
					// the removed and the new casing instead of never seeing the change at all.
					if (string.Equals(existing.PrimaryPath, incomingPath, StringComparison.Ordinal))
					{
						combined = default;
						return false;
					}

					combined = existing with { SecondaryPath = incomingPath, SecondaryKind = FileChangeKind.Deleted };
					return true;
				}

				combined = existing with { PrimaryPath = incomingPath };
				return true;

			case FileChangeKind.Changed:
				combined = incoming switch
				{
					FileChangeKind.Created => existing with { PrimaryPath = incomingPath, PrimaryKind = FileChangeKind.Created },
					FileChangeKind.Deleted => existing with { PrimaryPath = incomingPath, PrimaryKind = FileChangeKind.Deleted },
					_ => existing with { PrimaryPath = incomingPath }
				};

				return true;

			case FileChangeKind.Deleted:
				combined = incoming == FileChangeKind.Created
					? existing with { SecondaryPath = incomingPath, SecondaryKind = FileChangeKind.Created }
					: existing with { PrimaryPath = incomingPath };

				return true;

			default:
				combined = existing with { PrimaryPath = incomingPath };
				return true;
		}
	}

	private static BufferedWorkspaceChange TryCombineDeleteCreatePair(
		BufferedWorkspaceChange existing,
		string incomingPath,
		FileChangeKind secondaryKind,
		FileChangeKind incoming)
	{
		if (existing.PrimaryKind != FileChangeKind.Deleted || secondaryKind != FileChangeKind.Created)
			return existing;

		if (incoming == FileChangeKind.Deleted)
			return existing with { SecondaryPath = null, SecondaryKind = null };

		// A later create keeps the delete/create replacement observable and refreshes the preserved create casing.
		return incoming == FileChangeKind.Created ? existing with { SecondaryPath = incomingPath } : existing;
	}
}
