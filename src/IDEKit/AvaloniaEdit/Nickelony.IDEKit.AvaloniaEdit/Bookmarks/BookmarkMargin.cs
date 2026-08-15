using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using Nickelony.IDEKit.Core.Notifications;
using Nickelony.IDEKit.Infrastructure;

namespace Nickelony.IDEKit.AvaloniaEdit.Bookmarks;

/// <include file="../../../../../shared/docs/BookmarkMargin.xml" path="doc/members/member[@name='BookmarkMargin.Class']/*"/>
public class BookmarkMargin : LineStatusIconMarginBase
{
	private const double IconWidth = 10.0;
	private const double IconHeight = 9.0;

	private static readonly Geometry s_sampleIconGeometry = CreateIconGeometry();
	private static readonly ImmutableSolidColorBrush s_sampleIconBrush = BrushHelpers.CreateFrozenBrush(Color.FromRgb(0xE6, 0xA2, 0x3C));

	static BookmarkMargin()
	{
		IconBrushProperty.OverrideDefaultValue<BookmarkMargin>(s_sampleIconBrush);
		IconGeometryProperty.OverrideDefaultValue<BookmarkMargin>(s_sampleIconGeometry);
	}

	/// <include file="../../../../../shared/docs/BookmarkMargin.xml" path="doc/members/member[@name='SampleIconBrush']/*"/>
	public static IBrush SampleIconBrush => s_sampleIconBrush;

	/// <include file="../../../../../shared/docs/BookmarkMargin.xml" path="doc/members/member[@name='SampleIconGeometry']/*"/>
	public static Geometry SampleIconGeometry => s_sampleIconGeometry;

	private readonly IBookmarkSource _bookmarkSource;

	/// <include file="../../../../../shared/docs/BookmarkMargin.xml" path="doc/members/member[@name='BookmarkMargin']/*"/>
	public BookmarkMargin(IBookmarkSource bookmarkSource)
	{
		ArgumentNullException.ThrowIfNull(bookmarkSource);
		_bookmarkSource = bookmarkSource;

		FollowSourceNotifications(bookmarkSource);
	}

	/// <inheritdoc/>
	protected override IReadOnlyList<int> GetMarkedLineNumbers()
		=> _bookmarkSource.GetMarkedLineNumbers();

	/// <inheritdoc/>
	protected override bool OnIconClicked(VisualLine visualLine, Point position)
	{
		if (!OnBookmarkToggleRequested(visualLine.FirstDocumentLine.Offset))
			return false;

		// The toggle changed the marked lines, so the margin repaints itself.
		InvalidateVisual();
		return true;
	}

	/// <include file="../../../../../shared/docs/BookmarkMargin.xml" path="doc/members/member[@name='TestHooks']/*"/>
	internal BookmarkMarginTestHooks TestHooks { get; } = new();

	/// <inheritdoc/>
	protected override Point ResolveClickPosition(PointerPressedEventArgs e)
		=> TestHooks.ClickPositionResolver is Func<PointerPressedEventArgs, Point> resolver
			? resolver(e)
			: base.ResolveClickPosition(e);

	/// <include file="../../../../../shared/docs/BookmarkMargin.xml" path="doc/members/member[@name='TryToggleBookmarkAt']/*"/>
	internal bool TryToggleBookmarkAt(Point position)
		=> TryGetVisualLineAt(position, out VisualLine? visualLine)
			&& OnIconClicked(visualLine, position);

	/// <include file="../../../../../shared/docs/BookmarkMargin.xml" path="doc/members/member[@name='OnBookmarkToggleRequested']/*"/>
	protected virtual bool OnBookmarkToggleRequested(int lineOffset)
	{
		_bookmarkSource.ToggleBookmark(lineOffset);
		return true;
	}

	private static StreamGeometry CreateIconGeometry()
	{
		var geometry = new StreamGeometry();

		using (StreamGeometryContext context = geometry.Open())
		{
			context.BeginFigure(new Point(0.0, 0.0), true);
			context.LineTo(new Point(IconWidth, 0.0), true);
			context.LineTo(new Point(IconWidth, IconHeight), true);

			// The notch vertex sits 2.5 DIP above the icon's bottom edge.
			context.LineTo(new Point(IconWidth / 2.0, 6.5), true);
			context.LineTo(new Point(0.0, IconHeight), true);
			context.EndFigure(true);
		}

		return geometry;
	}
}
