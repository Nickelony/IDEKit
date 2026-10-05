namespace Nickelony.IDEKit.Core.Bookmarks;

/// <summary>Provides persistence operations for bookmarked line numbers.</summary>
/// <remarks>
/// Bookmark models keep their bookmarks in memory; implementations decide how the one-based line numbers are persisted,
/// and the interface is editor-neutral, so any host bridge can persist through the same store. Implementations perform synchronous I/O on the calling
/// thread unless they document otherwise, so a host should not call them from a per-change handler.
/// </remarks>
public interface IBookmarkStore
{
	/// <summary>Attempts to save one-based bookmark line numbers for the document <paramref name="documentId"/>.</summary>
	/// <param name="documentId">The identifier of the document the line numbers belong to.</param>
	/// <param name="bookmarkLineNumbers">The one-based line numbers to save.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentId"/> or <paramref name="bookmarkLineNumbers"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// <see langword="false"/> reports a save that did not persist the numbers. The bundled <see cref="BookmarkSidecarStore"/> reports a blank
	/// or unusable path and a failed file operation, and another implementation may add reasons, so a caller must not treat <see langword="false"/>
	/// as one specific failure.
	/// </remarks>
	bool TrySave(string documentId, IReadOnlyList<int> bookmarkLineNumbers);

	/// <summary>Attempts to load the stored one-based bookmark line numbers for the document <paramref name="documentId"/>.</summary>
	/// <param name="documentId">The identifier of the document the line numbers belong to.</param>
	/// <param name="bookmarkLineNumbers">Receives the stored one-based line numbers; empty when none were stored.</param>
	/// <returns><see langword="true"/> when the load produced a result; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="documentId"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// <see langword="true"/> reports a load that produced a result: the stored numbers, or an empty list when nothing was stored.
	/// <see langword="false"/> reports a load that could not be performed, so a caller can tell a storage failure apart from "nothing was saved".
	/// The bundled <see cref="BookmarkSidecarStore"/> reports a blank or unusable path and a failed read, and another implementation may add reasons.
	/// </remarks>
	bool TryLoad(string documentId, out IReadOnlyList<int> bookmarkLineNumbers);
}
