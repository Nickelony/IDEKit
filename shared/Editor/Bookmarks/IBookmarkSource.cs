using Nickelony.IDEKit.Core.LineStatus;
using Nickelony.IDEKit.Core.Notifications;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Bookmarks;
#else
namespace Nickelony.IDEKit.AvalonEdit.Bookmarks;
#endif

/// <summary>
/// Provides the bookmarked document lines for a <see cref="BookmarkMargin"/> and performs the
/// bookmark toggles requested by the margin.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BookmarkCoordinator"/> is the default implementation.
/// A custom implementation can drive a bookmark margin from another bookmark model.
/// A render-only implementation can implement <see cref="ToggleBookmark"/> as a no-op, because the
/// default margin calls it only for a toggle requested by a left-button click.
/// </para>
/// <para>
/// An implementation that also raises <see cref="IChangeNotificationSource.Changed"/> after its
/// bookmarks change is followed by a connected <see cref="BookmarkMargin"/>, which invalidates itself
/// when the notification arrives, so no manual invalidation call is required. A source without
/// notifications requires the host to invalidate the margin after its bookmarks change.
/// </para>
/// </remarks>
public interface IBookmarkSource : ILineStatusSource
{
	/// <summary>
	/// Adds or removes the bookmark on the line containing the specified offset.
	/// </summary>
	/// <param name="offset">
	/// The zero-based document offset used to locate the line. Values outside the document are clamped.
	/// </param>
	void ToggleBookmark(int offset);
}
