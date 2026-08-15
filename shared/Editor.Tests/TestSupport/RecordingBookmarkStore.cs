#if AVALONIAEDIT
using Nickelony.IDEKit.Core.Bookmarks;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using Nickelony.IDEKit.Core.Bookmarks;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

/// <summary>
/// An <see cref="IBookmarkStore"/> test double that records the calls made through it and returns
/// configured results, so the coordinator-level persistence extensions can be pinned without the
/// file system.
/// </summary>
internal sealed class RecordingBookmarkStore : IBookmarkStore
{
	/// <summary>
	/// Gets the save calls in call order, each with the path and the line numbers the coordinator supplied.
	/// </summary>
	public List<(string FilePath, IReadOnlyList<int> LineNumbers)> Saves { get; } = [];

	/// <summary>
	/// Gets the load calls in call order.
	/// </summary>
	public List<string> LoadedPaths { get; } = [];

	/// <summary>
	/// Gets or sets a value indicating whether <see cref="TrySave"/> reports success. Defaults to <see langword="true"/>.
	/// </summary>
	public bool SaveResult { get; set; } = true;

	/// <summary>
	/// Gets or sets a value indicating whether <see cref="TryLoad"/> reports a result.
	/// Defaults to <see langword="true"/>.
	/// </summary>
	public bool TryLoadResult { get; set; } = true;

	/// <summary>
	/// Gets or sets the line numbers <see cref="TryLoad"/> reports. Defaults to an empty list.
	/// </summary>
	public IReadOnlyList<int> LoadResult { get; set; } = [];

	/// <inheritdoc/>
	public bool TrySave(string filePath, IReadOnlyList<int> bookmarkLineNumbers)
	{
		Saves.Add((filePath, [.. bookmarkLineNumbers]));

		return SaveResult;
	}

	/// <inheritdoc/>
	public bool TryLoad(string filePath, out IReadOnlyList<int> bookmarkLineNumbers)
	{
		LoadedPaths.Add(filePath);

		bookmarkLineNumbers = LoadResult;
		return TryLoadResult;
	}
}
