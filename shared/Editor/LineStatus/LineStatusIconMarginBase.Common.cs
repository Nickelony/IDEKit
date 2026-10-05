using System.Diagnostics.CodeAnalysis;
using Nickelony.IDEKit.Core.LineStatus;
#if AVALONIAEDIT
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
#else
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LineStatus;

#else

namespace Nickelony.IDEKit.AvalonEdit.LineStatus;

#endif

public abstract partial class LineStatusIconMarginBase
{
	// The marker layout computed for the current marker properties, together with the stamp it was computed
	// for. The stamp is bumped whenever a property the layout depends on changes, so a render pass that
	// draws several markers reuses one computation instead of rereading the icon geometry, the margin width,
	// and the font size per marked line. These fields are touched on the margin's dispatcher thread only.
	private MarkerLayout _markerLayout;

	private Geometry? _cachedLayoutGeometry;

	private int _layoutStamp;
	private int _computedLayoutStamp = -1;

	/// <summary>Determines whether the icon geometry's bounds describe a paintable icon.</summary>
	/// <param name="bounds">The bounds of the icon geometry.</param>
	/// <returns><see langword="true"/> when the icon paints; otherwise, <see langword="false"/> and the margin draws nothing.</returns>
	private static partial bool HasUsableBounds(Rect bounds);

	/// <summary>Determines whether a marker layout computed for the geometry stays valid until a property of the margin changes.</summary>
	/// <param name="iconGeometry">The icon geometry the layout derives from.</param>
	/// <returns><see langword="true"/> when the layout can be cached; otherwise, <see langword="false"/> and every draw recomputes it.</returns>
	private static partial bool IsLayoutCacheable(Geometry iconGeometry);

	/// <summary>Creates the matrix that carries the scale and the placement every marker shares.</summary>
	/// <param name="placement">The placement computed for the icon's bounds.</param>
	/// <returns>The engine's matrix for the placement, with no vertical offset of a marker applied.</returns>
	private static partial Matrix CreateBaseMatrix(LineStatusIconPlacement placement);

	/// <summary>Draws the icon geometry with the shared layout moved down by the icon's top.</summary>
	/// <param name="drawingContext">The context the margin draws into.</param>
	/// <param name="iconGeometry">The icon geometry to draw.</param>
	/// <param name="layout">The marker layout of the icon.</param>
	/// <param name="iconTop">The top of the icon in the margin's coordinate space.</param>
	private partial void DrawIcon(DrawingContext drawingContext, Geometry iconGeometry, MarkerLayout layout, double iconTop);

	/// <inheritdoc/>
	protected internal override void DrawMarker(DrawingContext drawingContext, VisualLine visualLine, double visualTop)
	{
		Geometry iconGeometry = IconGeometry;
		MarkerLayout layout = GetMarkerLayout(iconGeometry);

		if (!layout.HasIcon)
			return;

		DrawIcon(drawingContext, iconGeometry, layout, iconTop: visualTop + GetIconTopWithinLine(visualLine, layout.IconHeight));
	}

	/// <include file="../../docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='TryHandleClickAt']/*"/>
	public bool TryHandleClickAt(Point position) => TryGetVisualLineAt(position, out VisualLine? visualLine) && OnIconClicked(visualLine, position);

	/// <include file="../../docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='OnIconClicked']/*"/>
	protected virtual bool OnIconClicked(VisualLine visualLine, Point position) => false;

	/// <include file="../../docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='TryGetVisualLineAt']/*"/>
	protected bool TryGetVisualLineAt(Point position, [NotNullWhen(true)] out VisualLine? visualLine)
	{
		TextView? textView = TextView;

		if (textView is null || !textView.VisualLinesValid)
		{
			visualLine = null;
			return false;
		}

		visualLine = textView.GetVisualLineFromVisualTop(position.Y + textView.VerticalOffset);
		return visualLine is not null;
	}

	/// <summary>Discards the cached marker layout so the next marker draw recomputes it.</summary>
	private void InvalidateMarkerLayout() => _layoutStamp++;

	/// <include file="../../docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='GetMarkerLayout']/*"/>
	private MarkerLayout GetMarkerLayout(Geometry iconGeometry)
	{
		// A geometry whose bounds can change without a property change is not cached, because the stamp
		// cannot observe that change.
		if (!IsLayoutCacheable(iconGeometry))
			return CreateMarkerLayout(iconGeometry);

		// The cache is keyed on the property stamp and the geometry reference: a different geometry instance,
		// the margin width, the font size, or the arranged width all invalidate it.
		if (_computedLayoutStamp != _layoutStamp || !ReferenceEquals(_cachedLayoutGeometry, iconGeometry))
		{
			_markerLayout = CreateMarkerLayout(iconGeometry);
			_computedLayoutStamp = _layoutStamp;
			_cachedLayoutGeometry = iconGeometry;
		}

		return _markerLayout;
	}

	/// <include file="../../docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='CreateMarkerLayout']/*"/>
	private MarkerLayout CreateMarkerLayout(Geometry iconGeometry)
	{
		Rect bounds = iconGeometry.Bounds;

		// An icon without usable bounds (the base default) draws nothing.
		if (!HasUsableBounds(bounds))
			return default;

		LineStatusIconPlacement placement = LineStatusIconOperations.Place(
			boundsLeft: bounds.Left,
			boundsTop: bounds.Top,
			boundsWidth: bounds.Width,
			boundsHeight: bounds.Height,
			fontScale: GetFontScale(),
			marginWidth: GetEffectiveMarginWidth()
		);

		return new MarkerLayout(HasIcon: true, placement.IconHeight, BaseMatrix: CreateBaseMatrix(placement));
	}

	/// <summary>Returns the icon's top offset within its visual line, clamped into the box of the line's first text row.</summary>
	private static double GetIconTopWithinLine(VisualLine visualLine, double iconHeight)
	{
		double firstRowHeight = visualLine.TextLines.Count > 0 ? visualLine.TextLines[0].Height : visualLine.Height;
		return LineStatusIconOperations.GetTopWithinLine(visualLine.Height, firstRowHeight, iconHeight);
	}

	/// <include file="../../docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='MarkerLayout']/*"/>
	private readonly record struct MarkerLayout(bool HasIcon, double IconHeight, Matrix BaseMatrix);
}
