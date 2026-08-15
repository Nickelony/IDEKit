using Nickelony.IDEKit.Core.Bookmarks;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Bookmarks;
#else
namespace Nickelony.IDEKit.AvalonEdit.Bookmarks;
#endif

/// <summary>
/// Provides extension methods for persisting bookmarks managed by a <see cref="BookmarkCoordinator"/>.
/// </summary>
public static class BookmarkStoreExtensions
{
	/// <summary>
	/// Saves the coordinator's current one-based bookmark line numbers through the supplied store.
	/// </summary>
	/// <remarks>
	/// The save fails while <see cref="BookmarkCoordinator.IsBoundToCurrentDocument"/> is
	/// <see langword="false"/>; a host that saves from the coordinator's
	/// <see cref="BookmarkCoordinator.Changed"/> event can query that property to skip or defer the
	/// save in the unbound window.
	/// </remarks>
	/// <param name="coordinator">The coordinator whose bookmarks are persisted.</param>
	/// <param name="store">The store used to save the bookmarks.</param>
	/// <param name="filePath">The path of the document the bookmarks belong to.</param>
	/// <returns><see langword="true"/> when the store reports success; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="coordinator"/>, <paramref name="store"/>, or <paramref name="filePath"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The coordinator's document provider returns <see langword="null"/>, or the bookmarks are not
	/// bound to the document its provider currently returns, for example after a document swap before
	/// bookmarks are restored for the new document. Saving would delete the persisted bookmarks, so
	/// the save fails instead.
	/// </exception>
	public static bool SaveBookmarks(this BookmarkCoordinator coordinator, IBookmarkStore store, string filePath)
	{
		ArgumentNullException.ThrowIfNull(coordinator);
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(filePath);

		// The persistence target is resolved fail-fast: an unbound or mismatched coordinator would
		// otherwise persist an empty set, which deletes the sidecar instead of saving the bookmarks.
		_ = coordinator.GetDocumentForPersistence();

		IReadOnlyList<int> bookmarkLineNumbers = coordinator.GetMarkedLineNumbers();

		return store.TrySave(filePath, bookmarkLineNumbers);
	}

	/// <summary>
	/// Restores the stored one-based bookmark line numbers into the coordinator.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Replaces existing bookmarks. Line numbers outside the document are ignored, and the
	/// coordinator's <see cref="BookmarkCoordinator.Changed"/> event is raised.
	/// </para>
	/// <para>
	/// A load that reports failure (see <see cref="IBookmarkStore.TryLoad"/>) leaves the coordinator
	/// unchanged: a coordinator that has not bound yet stays unbound, so a later
	/// <see cref="SaveBookmarks(BookmarkCoordinator, IBookmarkStore, string)"/> fails instead of
	/// persisting an empty set and deleting the stored bookmarks.
	/// </para>
	/// </remarks>
	/// <param name="coordinator">The coordinator whose bookmarks are restored.</param>
	/// <param name="store">The store used to restore the bookmarks.</param>
	/// <param name="filePath">The path of the document the bookmarks belong to.</param>
	/// <returns>
	/// <see langword="true"/> when the store's load produced a result and the coordinator's bookmarks
	/// were replaced; <see langword="false"/> when the load failed and nothing was changed.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="coordinator"/>, <paramref name="store"/>, or <paramref name="filePath"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The coordinator's document provider returns <see langword="null"/> and the load produced a
	/// result to restore.
	/// </exception>
	public static bool RestoreBookmarks(this BookmarkCoordinator coordinator, IBookmarkStore store, string filePath)
	{
		ArgumentNullException.ThrowIfNull(coordinator);
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(filePath);

		if (!store.TryLoad(filePath, out IReadOnlyList<int> bookmarkLineNumbers))
			return false;

		coordinator.RestoreBookmarks(bookmarkLineNumbers);
		return true;
	}
}
