using Nickelony.IDEKit.Core.Bookmarks;

namespace Nickelony.IDEKit.Testing;

/// <summary>
/// An <see cref="IBookmarkStore"/> test double that records the calls made through it and returns configured results, so the coordinator-level
/// persistence extensions can be pinned without the file system.
/// </summary>
internal sealed class RecordingBookmarkStore : IBookmarkStore
{
	/// <summary>Returns the save calls in call order, each with the path and the line numbers the coordinator supplied.</summary>
	public List<(string FilePath, IReadOnlyList<int> LineNumbers)> SavedEntries { get; } = [];

	/// <summary>The load calls in call order.</summary>
	public List<string> LoadedPaths { get; } = [];

	/// <summary>Indicates whether <see cref="TrySave"/> reports success (defaults to <see langword="true"/>).</summary>
	public bool TrySaveResult { get; set; } = true;

	/// <summary>Indicates whether <see cref="TryLoad"/> reports a result (defaults to <see langword="true"/>).</summary>
	public bool TryLoadResult { get; set; } = true;

	/// <summary>Holds the line numbers <see cref="TryLoad"/> reports (defaults to an empty list).</summary>
	public IReadOnlyList<int> LoadResult { get; set; } = [];

	/// <inheritdoc/>
	public bool TrySave(string filePath, IReadOnlyList<int> bookmarkLineNumbers)
	{
		SavedEntries.Add((filePath, [.. bookmarkLineNumbers]));
		return TrySaveResult;
	}

	/// <inheritdoc/>
	public bool TryLoad(string filePath, out IReadOnlyList<int> bookmarkLineNumbers)
	{
		LoadedPaths.Add(filePath);

		bookmarkLineNumbers = LoadResult;
		return TryLoadResult;
	}
}
