using Nickelony.IDEKit.Core.Persistence;

namespace Nickelony.IDEKit.Core.Bookmarks;

/// <summary>
/// Persists bookmarked line numbers in a sidecar file associated with each document.
/// </summary>
/// <remarks>
/// <para>
/// The sidecar extension is supplied by the host, so sidecar naming stays a host decision.
/// </para>
/// <para>
/// The numbers are persisted sorted and de-duplicated; a bookmark model matches them against the
/// document's lines when it restores them, so edits between a save and a restore can silently move a
/// bookmark to a different line.
/// </para>
/// <para>
/// Saves and loads perform synchronous file I/O on the calling thread, so a host should not call them
/// from a per-change handler.
/// </para>
/// </remarks>
public sealed class BookmarkSidecarStore : IBookmarkStore
{
	private readonly string _sidecarExtension;

	/// <summary>
	/// Initializes a new instance of the <see cref="BookmarkSidecarStore"/> class.
	/// </summary>
	/// <param name="sidecarExtension">
	/// The non-blank, usable extension appended to document paths, for example <c>.bookmarks</c>.
	/// A blank or unusable extension is rejected here, so an invalid configuration fails at construction
	/// instead of making every save and restore throw.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="sidecarExtension"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="sidecarExtension"/> is empty, whitespace-only, or does not form a usable file-name
	/// extension (for example <c>.</c>, <c>a/b</c>, or <c>x\y</c>).
	/// </exception>
	public BookmarkSidecarStore(string sidecarExtension)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sidecarExtension);

		SidecarLineFile.ValidateExtension(sidecarExtension);

		_sidecarExtension = sidecarExtension;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Entries below one are ignored by <see cref="SidecarLineFile.Save"/> while at least one valid
	/// entry remains. A set whose entries are all below one is rejected as a caller bug
	/// (<see cref="SidecarLineFile.Save"/> returns <see langword="false"/>), so the existing sidecar
	/// stays untouched instead of being deleted.
	/// </remarks>
	public bool TrySave(string filePath, IReadOnlyList<int> bookmarkLineNumbers)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(bookmarkLineNumbers);

		return SidecarLineFile.Save(filePath, bookmarkLineNumbers, _sidecarExtension);
	}

	/// <inheritdoc/>
	public bool TryLoad(string filePath, out IReadOnlyList<int> bookmarkLineNumbers)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		return SidecarLineFile.TryRestore(filePath, _sidecarExtension, out bookmarkLineNumbers);
	}
}
