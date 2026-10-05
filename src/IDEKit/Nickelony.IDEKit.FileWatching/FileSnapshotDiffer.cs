namespace Nickelony.IDEKit.FileWatching;

/// <summary>Computes the file changes that reconcile two workspace snapshots.</summary>
public static class FileSnapshotDiffer
{
	/// <summary>Builds a normalized delta batch between two workspace snapshots.</summary>
	/// <param name="previousSnapshot">The baseline snapshot captured before watcher disruption.</param>
	/// <param name="currentSnapshot">The replacement snapshot captured after watcher recovery.</param>
	/// <param name="pathComparison">The comparison that orders the changes of the batch by path.</param>
	/// <param name="isPathMissing">
	/// Reports whether a path that the current snapshot lacks is provably gone, which decides between a deletion and a path the walk skipped.
	/// </param>
	/// <returns>The sorted file change batch needed to reconcile the snapshots.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="previousSnapshot"/>, <paramref name="currentSnapshot"/>, <paramref name="pathComparison"/> or <paramref name="isPathMissing"/>
	/// is <see langword="null"/>.
	/// </exception>
	public static FileChangeBatch Build(
		IReadOnlyDictionary<string, FileSnapshotEntry> previousSnapshot,
		IReadOnlyDictionary<string, FileSnapshotEntry> currentSnapshot,
		LocalPathComparisonPolicy pathComparison,
		Func<string, bool> isPathMissing
	)
	{
		ArgumentNullException.ThrowIfNull(previousSnapshot);
		ArgumentNullException.ThrowIfNull(currentSnapshot);
		ArgumentNullException.ThrowIfNull(pathComparison);
		ArgumentNullException.ThrowIfNull(isPathMissing);

		var changes = new List<FileChange>();

		foreach ((string path, FileSnapshotEntry previousEntry) in previousSnapshot)
		{
			if (!currentSnapshot.TryGetValue(path, out FileSnapshotEntry currentEntry))
			{
				if (isPathMissing(path))
					changes.Add(new FileChange(path, FileChangeKind.Deleted));

				continue;
			}

			if (!currentEntry.Equals(previousEntry))
				changes.Add(new FileChange(path, FileChangeKind.Changed));
		}

		foreach ((string path, FileSnapshotEntry _) in currentSnapshot)
		{
			if (!previousSnapshot.ContainsKey(path))
				changes.Add(new FileChange(path, FileChangeKind.Created));
		}

		changes.Sort((left, right) => pathComparison.Comparer.Compare(left.Path, right.Path));
		return new(changes);
	}
}
