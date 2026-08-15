namespace Nickelony.IDEKit.Core.Bookmarks;

/// <summary>
/// Provides persistence operations for bookmarked line numbers.
/// </summary>
/// <remarks>
/// <para>
/// Bookmark models keep their bookmarks in memory; implementations decide how the one-based line
/// numbers are persisted. The interface is editor-neutral, so any host bridge can persist through
/// the same store.
/// </para>
/// <para>
/// Implementations perform synchronous I/O on the calling thread unless they document otherwise, so
/// a host should not call them from a per-change handler.
/// </para>
/// </remarks>
public interface IBookmarkStore
{
	/// <summary>
	/// Attempts to save one-based bookmark line numbers for the document at <paramref name="filePath"/>.
	/// </summary>
	/// <remarks>
	/// <see langword="false"/> reports a save that did not persist the numbers. The bundled
	/// <see cref="BookmarkSidecarStore"/> reports a blank or unusable path and a failed file
	/// operation; another implementation may add reasons, so callers must not treat
	/// <see langword="false"/> as one specific failure.
	/// </remarks>
	/// <param name="filePath">The document path.</param>
	/// <param name="bookmarkLineNumbers">The one-based line numbers to save.</param>
	/// <returns><see langword="true"/> when the save operation succeeds; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="bookmarkLineNumbers"/> is <see langword="null"/>.
	/// </exception>
	bool TrySave(string filePath, IReadOnlyList<int> bookmarkLineNumbers);

	/// <summary>
	/// Attempts to load the stored one-based bookmark line numbers for the document at <paramref name="filePath"/>.
	/// </summary>
	/// <remarks>
	/// <see langword="true"/> reports a load that produced a result: the stored numbers, or an empty
	/// list when nothing was stored. <see langword="false"/> reports a load that could not be
	/// performed, so a caller can tell a storage failure apart from "nothing was saved". The bundled
	/// <see cref="BookmarkSidecarStore"/> reports a blank or unusable path and a failed read; another
	/// implementation may add reasons, so callers must not treat <see langword="false"/> as one
	/// specific failure.
	/// </remarks>
	/// <param name="filePath">The document path.</param>
	/// <param name="bookmarkLineNumbers">
	/// Receives the stored one-based line numbers; empty when none were stored.
	/// </param>
	/// <returns><see langword="true"/> when the load produced a result; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> is <see langword="null"/>.
	/// </exception>
	bool TryLoad(string filePath, out IReadOnlyList<int> bookmarkLineNumbers);
}
