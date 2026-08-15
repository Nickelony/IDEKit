namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Represents a batch of workspace file changes ready to forward to the language server.
/// </summary>
public sealed class FileChangeBatch
{
	/// <summary>
	/// Initializes a new instance of the <see cref="FileChangeBatch"/> class.
	/// </summary>
	/// <param name="entries">The entries captured for the batch.</param>
	/// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
	public FileChangeBatch(IEnumerable<WorkspaceFileChange> entries)
	{
		ArgumentNullException.ThrowIfNull(entries);
		Entries = Array.AsReadOnly([.. entries]);
	}

	/// <summary>
	/// Gets the file-change entries in the batch.
	/// </summary>
	public IReadOnlyList<WorkspaceFileChange> Entries { get; }

	/// <summary>
	/// Gets the number of entries in the batch.
	/// </summary>
	public int Count => Entries.Count;
}
