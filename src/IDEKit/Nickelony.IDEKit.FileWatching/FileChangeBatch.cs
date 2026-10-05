namespace Nickelony.IDEKit.FileWatching;

/// <summary>Represents a batch of file changes ready to forward to the owner.</summary>
public sealed class FileChangeBatch
{
	/// <param name="entries">The entries captured for the batch.</param>
	/// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
	public FileChangeBatch(IEnumerable<FileChange> entries)
	{
		ArgumentNullException.ThrowIfNull(entries);
		Entries = Array.AsReadOnly([.. entries]);
	}

	/// <summary>The changes of the batch, in dispatch order.</summary>
	public IReadOnlyList<FileChange> Entries { get; }

	/// <summary>The number of changes in the batch.</summary>
	public int Count => Entries.Count;
}
