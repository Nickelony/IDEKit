using Nickelony.IDEKit.Core.Bookmarks;
#if AVALONIAEDIT
using Avalonia;
using AvaloniaEdit.Rendering;
#else
using System.Windows;
using ICSharpCode.AvalonEdit.Rendering;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Bookmarks;

#else

namespace Nickelony.IDEKit.AvalonEdit.Bookmarks;

#endif

public partial class BookmarkMargin
{
	private const double IconWidth = 10.0;
	private const double IconHeight = 9.0;

	// The sample icon color (amber).
	private const byte SampleIconRed = 0xE6;
	private const byte SampleIconGreen = 0xA2;
	private const byte SampleIconBlue = 0x3C;

	private readonly IBookmarkSource _bookmarkSource;

	/// <include file="../../docs/BookmarkMargin.xml" path="doc/members/member[@name='BookmarkMargin']/*"/>
	public BookmarkMargin(IBookmarkSource bookmarkSource)
	{
		ArgumentNullException.ThrowIfNull(bookmarkSource);
		_bookmarkSource = bookmarkSource;

		FollowSourceNotifications(bookmarkSource);
	}

	/// <summary>Returns the vertices of the sample icon: a flag with a notch 2.5 DIP above its bottom edge.</summary>
	/// <returns>The vertices in drawing order, starting at the top-left corner; the engine half closes the figure.</returns>
	private static Point[] GetIconOutline() =>
		[new(0.0, 0.0), new(IconWidth, 0.0), new(IconWidth, IconHeight), new(IconWidth / 2.0, IconHeight - 2.5), new(0.0, IconHeight)];

	/// <inheritdoc/>
	protected override IReadOnlyList<int> GetMarkedLineNumbers() => _bookmarkSource.GetMarkedLineNumbers();

	/// <inheritdoc/>
	protected override bool OnIconClicked(VisualLine visualLine, Point position)
	{
		if (!OnBookmarkToggleRequested(visualLine.FirstDocumentLine.Offset))
			return false;

		// The toggle changed the marked lines, so the margin repaints itself.
		InvalidateVisual();
		return true;
	}

	/// <include file="../../docs/BookmarkMargin.xml" path="doc/members/member[@name='OnBookmarkToggleRequested']/*"/>
	protected virtual bool OnBookmarkToggleRequested(int documentOffset)
	{
		_bookmarkSource.ToggleBookmark(documentOffset);
		return true;
	}
}
