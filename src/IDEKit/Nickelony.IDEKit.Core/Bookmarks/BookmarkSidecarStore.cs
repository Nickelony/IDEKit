namespace Nickelony.IDEKit.Core.Bookmarks;

/// <summary>Persists bookmarked line numbers in a sidecar file associated with each document.</summary>
/// <remarks>
/// The sidecar extension is supplied by the host, so naming stays a host decision. The document identifier is the path of the document's file,
/// because the sidecar sits beside it. The numbers are persisted sorted and de-duplicated, and a bookmark model matches them against the document's
/// lines when it restores them, so edits between a save and a restore can move a bookmark to a different line.
/// </remarks>
public sealed class BookmarkSidecarStore : IBookmarkStore
{
	private readonly string _sidecarExtension;

	/// <param name="sidecarExtension">
	/// The extension appended to document paths, for example <c>.bookmarks</c>; a blank or unusable extension is rejected here, so an invalid
	/// configuration fails at construction instead of on every save and restore.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="sidecarExtension"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="sidecarExtension"/> is empty, whitespace-only, or does not form a usable file-name extension
	/// (for example <c>.</c>, <c>a/b</c>, or <c>x\y</c>).
	/// </exception>
	public BookmarkSidecarStore(string sidecarExtension)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sidecarExtension);

		SidecarLineFile.ValidateExtension(sidecarExtension);

		_sidecarExtension = sidecarExtension;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// <see cref="SidecarLineFile.Save"/> ignores entries below one while at least one valid entry remains. A set whose entries
	/// are all below one is rejected as a caller bug, so the existing sidecar stays untouched instead of being deleted.
	/// </remarks>
	public bool TrySave(string documentId, IReadOnlyList<int> bookmarkLineNumbers)
	{
		ArgumentNullException.ThrowIfNull(documentId);
		ArgumentNullException.ThrowIfNull(bookmarkLineNumbers);

		return SidecarLineFile.Save(documentId, bookmarkLineNumbers, _sidecarExtension);
	}

	/// <inheritdoc/>
	public bool TryLoad(string documentId, out IReadOnlyList<int> bookmarkLineNumbers)
	{
		ArgumentNullException.ThrowIfNull(documentId);
		return SidecarLineFile.TryRestore(documentId, _sidecarExtension, out bookmarkLineNumbers);
	}
}
