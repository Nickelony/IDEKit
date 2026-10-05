using Nickelony.IDEKit.Core.LineStatus;
using Nickelony.IDEKit.Core.Notifications;

namespace Nickelony.IDEKit.Core.Bookmarks;

/// <summary>Provides the bookmarked document lines for a bookmark margin and performs the bookmark toggles requested by the margin.</summary>
/// <remarks>
/// A render-only implementation can implement <see cref="ToggleBookmark"/> as a no-op. A source that also raises
/// <see cref="IChangeNotificationSource.Changed"/> is watched by a connected margin, which invalidates itself when the notification arrives.
/// </remarks>
public interface IBookmarkSource : ILineStatusSource
{
	/// <summary>Adds or removes the bookmark on the line containing the specified offset.</summary>
	/// <param name="documentOffset">The zero-based document offset that locates the line. Values outside the document are clamped.</param>
	void ToggleBookmark(int documentOffset);
}
