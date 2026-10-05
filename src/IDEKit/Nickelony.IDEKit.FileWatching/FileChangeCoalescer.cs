using System.Collections.Concurrent;

namespace Nickelony.IDEKit.FileWatching;

/// <summary>Coalesces repeated file change updates per path into one pending change.</summary>
/// <param name="pathComparison">
/// The comparison that decides which paths coalesce into one change, or <see langword="null"/> for the current platform's.
/// </param>
/// <remarks>
/// <para>
/// Coalescing keeps the first pending occurrence order of each path, preserves delete/create replacement semantics,
/// and keeps the path casing of each preserved occurrence. <see cref="FileChangeBuffer"/> pairs two coalescers for the buffering owners,
/// such as <see cref="FileChangeDebouncer"/>.
/// </para>
/// <para>
/// Draining returns the coalesced changes in first-pending order and clears the coalescer. The coalescer is safe for concurrent callers.
/// It is internal to the framework: the buffered-change component and its owners live in this package, so no consumer needs it as public surface.
/// </para>
/// </remarks>
internal sealed class FileChangeCoalescer(LocalPathComparisonPolicy? pathComparison = null)
{
	private readonly ConcurrentDictionary<string, CoalescedFileChange> _changes = new(
		(pathComparison ?? LocalPathComparisonPolicy.ForCurrentPlatform).Comparer
	);

	private long _nextSequence;

	/// <summary>
	/// Stores the coalesced change kind, the order in which the path first became pending, and the path casing of each occurrence
	/// that still contributes to the coalesced change.
	/// </summary>
	/// <param name="Sequence">The first pending occurrence order for the path.</param>
	/// <param name="PrimaryPath">
	/// The path casing of the most recent occurrence that still contributes to the primary change kind (a later occurrence can refresh the casing
	/// without replacing the kind).
	/// </param>
	/// <param name="PrimaryKind">The first effective coalesced change kind.</param>
	/// <param name="SecondaryPath">The path casing of the occurrence that owns the secondary change kind.</param>
	/// <param name="SecondaryKind">The second effective coalesced change kind when a delete/create pair must be preserved.</param>
	private readonly record struct CoalescedFileChange(
		long Sequence,
		string PrimaryPath,
		FileChangeKind PrimaryKind,
		string? SecondaryPath = null,
		FileChangeKind? SecondaryKind = null
	);

	internal bool IsEmpty => _changes.IsEmpty;

	internal int Count => _changes.Count;

	/// <summary>Adds a change, or merges it into any buffered change for the same path, preserving delete/create replacement semantics.</summary>
	/// <param name="filePath">The normalized file path. Changes with an empty or whitespace-only path are ignored.</param>
	/// <param name="kind">The incoming change kind. An undefined kind is stored only when no change is buffered for the path.</param>
	internal void Add(string filePath, FileChangeKind kind)
	{
		if (string.IsNullOrWhiteSpace(filePath))
			return;

		long newSequence = Interlocked.Increment(ref _nextSequence);

		while (true)
		{
			if (!_changes.TryGetValue(filePath, out CoalescedFileChange existingChange))
			{
				if (_changes.TryAdd(filePath, new CoalescedFileChange(newSequence, filePath, kind)))
					return;

				continue;
			}

			if (!TryCombine(existing: existingChange, incomingPath: filePath, incoming: kind, combined: out CoalescedFileChange combinedChange))
			{
				if (_changes.TryRemove(new KeyValuePair<string, CoalescedFileChange>(filePath, existingChange)))
					return;

				continue;
			}

			if (_changes.TryUpdate(filePath, combinedChange, existingChange))
				return;
		}
	}

	/// <summary>Adds or merges a sequence of file changes into the coalescer.</summary>
	/// <param name="changes">The changes to merge.</param>
	internal void AddRange(IReadOnlyList<FileChange> changes)
	{
		for (int i = 0; i < changes.Count; i++)
			Add(changes[i].Path, changes[i].Kind);
	}

	/// <summary>Drains all pending changes into a batch ready for forwarding.</summary>
	/// <returns>The drained file change batch in first-pending order; the coalescer is empty afterwards.</returns>
	internal FileChangeBatch DrainBatch()
	{
		var drainedChanges = new List<CoalescedFileChangeEntry>();

		foreach (KeyValuePair<string, CoalescedFileChange> entry in _changes)
		{
			if (_changes.TryRemove(entry.Key, out CoalescedFileChange change))
			{
				drainedChanges.Add(
					new CoalescedFileChangeEntry(
						Sequence: change.Sequence,
						PrimaryPath: change.PrimaryPath,
						PrimaryKind: change.PrimaryKind,
						SecondaryPath: change.SecondaryPath,
						SecondaryKind: change.SecondaryKind
					)
				);
			}
		}

		drainedChanges.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));

		var changes = new List<FileChange>(drainedChanges.Count);

		for (int i = 0; i < drainedChanges.Count; i++)
		{
			changes.Add(new FileChange(drainedChanges[i].PrimaryPath, drainedChanges[i].PrimaryKind));

			if (drainedChanges[i].SecondaryPath is string secondaryPath && drainedChanges[i].SecondaryKind is FileChangeKind secondaryKind)
				changes.Add(new FileChange(secondaryPath, secondaryKind));
		}

		return new FileChangeBatch(changes);
	}

	/// <summary>Stores one drained buffered change together with its preserved ordering metadata and path casings.</summary>
	/// <param name="Sequence">The first pending occurrence order for the path.</param>
	/// <param name="PrimaryPath">The path casing of the most recent occurrence that still contributes to the primary change kind.</param>
	/// <param name="PrimaryKind">The first effective coalesced change kind.</param>
	/// <param name="SecondaryPath">The path casing of the occurrence that owns the secondary change kind.</param>
	/// <param name="SecondaryKind">The second effective coalesced change kind when a delete/create pair must be preserved.</param>
	private readonly record struct CoalescedFileChangeEntry(
		long Sequence,
		string PrimaryPath,
		FileChangeKind PrimaryKind,
		string? SecondaryPath,
		FileChangeKind? SecondaryKind
	);

	/// <summary>Combines an existing and incoming change into the effective change that should be forwarded.</summary>
	/// <param name="existing">The buffered change already stored for the path.</param>
	/// <param name="incomingPath">The path casing of the incoming change.</param>
	/// <param name="incoming">The new incoming change kind.</param>
	/// <param name="combined">Receives the coalesced effective change state when one remains.</param>
	/// <returns><see langword="true"/> when a coalesced change should remain buffered; otherwise, <see langword="false"/>.</returns>
	private static bool TryCombine(CoalescedFileChange existing, string incomingPath, FileChangeKind incoming, out CoalescedFileChange combined)
	{
		if (existing.SecondaryKind is FileChangeKind secondaryKind)
		{
			combined = CombineDeleteCreatePair(existing, incomingPath, secondaryKind, incoming);
			return true;
		}

		switch (existing.PrimaryKind)
		{
			case FileChangeKind.Created:
				if (incoming == FileChangeKind.Deleted)
				{
					// A same-path create/delete pair within one debounce window cancels. A case-only replacement
					// must stay observable: the delete belongs to the previous casing and the server has to learn
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
					_ => existing with { PrimaryPath = incomingPath },
				};

				return true;

			case FileChangeKind.Deleted:
				combined =
					incoming == FileChangeKind.Created
						? existing with
						{
							SecondaryPath = incomingPath,
							SecondaryKind = FileChangeKind.Created,
						}
						: existing with
						{
							PrimaryPath = incomingPath,
						};

				return true;

			default:
				combined = existing with { PrimaryPath = incomingPath };
				return true;
		}
	}

	private static CoalescedFileChange CombineDeleteCreatePair(
		CoalescedFileChange existing,
		string incomingPath,
		FileChangeKind secondaryKind,
		FileChangeKind incoming
	)
	{
		if (existing.PrimaryKind == FileChangeKind.Created && secondaryKind == FileChangeKind.Deleted)
		{
			// The pairing is a case-only create followed by a delete of the previous casing, and this third event
			// supersedes it: the pending delete does not describe the tree. A later delete makes the final state
			// deleted; anything else leaves the path created at the latest casing.
			return incoming == FileChangeKind.Deleted
				? existing with
				{
					PrimaryPath = incomingPath,
					PrimaryKind = FileChangeKind.Deleted,
					SecondaryPath = null,
					SecondaryKind = null,
				}
				: existing with
				{
					PrimaryPath = incomingPath,
					SecondaryPath = null,
					SecondaryKind = null,
				};
		}

		if (existing.PrimaryKind != FileChangeKind.Deleted || secondaryKind != FileChangeKind.Created)
			return existing;

		if (incoming == FileChangeKind.Deleted)
			return existing with { SecondaryPath = null, SecondaryKind = null };

		// A later create keeps the delete/create replacement observable and refreshes the preserved create casing.
		return incoming == FileChangeKind.Created ? existing with { SecondaryPath = incomingPath } : existing;
	}
}
