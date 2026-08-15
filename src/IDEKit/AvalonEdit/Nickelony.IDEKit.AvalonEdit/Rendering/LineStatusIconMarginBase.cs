using ICSharpCode.AvalonEdit.Rendering;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Rendering;

/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='LineStatusIconMarginBase.Class']/*"/>
/// <remarks>
/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='LineStatusIconMarginBase.Remarks']/*"/>
/// </remarks>
public abstract class LineStatusIconMarginBase : LineStatusMarginBase
{
	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='IconBrushProperty']/*"/>
	public static readonly DependencyProperty IconBrushProperty = DependencyProperty.Register(
		nameof(IconBrush),
		typeof(Brush),
		typeof(LineStatusIconMarginBase),
		new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender),
		ValidateIconBrush);

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='IconGeometryProperty']/*"/>
	public static readonly DependencyProperty IconGeometryProperty = DependencyProperty.Register(
		nameof(IconGeometry),
		typeof(Geometry),
		typeof(LineStatusIconMarginBase),
		new FrameworkPropertyMetadata(Geometry.Empty, FrameworkPropertyMetadataOptions.AffectsRender),
		ValidateIconGeometry);

	// The marker layout computed for the current marker properties, together with the stamp it was computed
	// for. The stamp is bumped whenever a property the layout depends on changes, so a render pass that
	// draws several markers reuses one computation instead of rereading the icon geometry, the margin width,
	// and the font size per marked line. Both fields are touched on the margin's dispatcher thread only.
	private MarkerLayout _markerLayout;

	private int _layoutStamp;
	private int _computedLayoutStamp = -1;

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='IconBrush']/*"/>
	/// <exception cref="ArgumentException">
	/// The value assigned through <c>SetValue</c> or XAML is not a <see cref="Brush"/>; the property's
	/// validation callback rejects it. A direct assignment cannot pass such a value because the CLR
	/// property type is already <see cref="Brush"/>.
	/// </exception>
	public Brush IconBrush
	{
		get => (Brush)GetValue(IconBrushProperty);
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			SetValue(IconBrushProperty, value);
		}
	}

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='IconGeometry']/*"/>
	/// <exception cref="ArgumentException">
	/// The value assigned through <c>SetValue</c> or XAML is not a <see cref="Geometry"/>; the property's
	/// validation callback rejects it. A direct assignment cannot pass such a value because the CLR
	/// property type is already <see cref="Geometry"/>.
	/// </exception>
	public Geometry IconGeometry
	{
		get => (Geometry)GetValue(IconGeometryProperty);
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			SetValue(IconGeometryProperty, value);
		}
	}

	/// <inheritdoc/>
	protected internal override void DrawMarker(DrawingContext drawingContext, VisualLine visualLine, double visualTop)
	{
		Geometry iconGeometry = IconGeometry;
		MarkerLayout layout = GetMarkerLayout(iconGeometry);

		// A margin without an icon (the base default) draws nothing instead of drawing from empty bounds.
		if (!layout.HasIcon)
			return;

		// A word-wrapped document line is one visual line with several text lines, and its box can be
		// taller than the viewport, so centering the icon in the whole box could keep it out of view
		// while the line's first text row is visible. The centered position is therefore clamped into
		// the first text line's box.
		double firstTextLineHeight = visualLine.TextLines.Count > 0 ? visualLine.TextLines[0].Height : visualLine.Height;
		double iconTop = Math.Clamp(
			visualTop + ((visualLine.Height - layout.IconHeight) / 2.0),
			visualTop,
			visualTop + Math.Max(0.0, firstTextLineHeight - layout.IconHeight));

		// A marker draw is a render-hot path, but the transform cannot be reused across markers: the
		// drawing context records it by reference, so one mutated instance would leave every recorded
		// marker rendered with the last matrix. Each marker pushes its own frozen copy of the cached
		// matrix, moved down to its icon's top, which also keeps the recorded transform safe for the
		// render thread.
		Matrix markerMatrix = layout.BaseMatrix;
		markerMatrix.OffsetY += iconTop;

		var markerTransform = new MatrixTransform(markerMatrix);
		markerTransform.Freeze();

		drawingContext.PushTransform(markerTransform);
		drawingContext.DrawGeometry(IconBrush, null, iconGeometry);
		drawingContext.Pop();
	}

	/// <inheritdoc/>
	protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
	{
		base.OnMouseLeftButtonDown(e);

		if (e.Handled)
			return;

		Point position = ResolveClickPosition(e);

		if (!TryGetVisualLineAt(position, out VisualLine? visualLine))
			return;

		if (OnIconClicked(visualLine, position))
			e.Handled = true;
	}

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='OnIconClicked']/*"/>
	protected virtual bool OnIconClicked(VisualLine visualLine, Point position) => false;

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='ResolveClickPosition']/*"/>
	protected virtual Point ResolveClickPosition(MouseButtonEventArgs e) => e.GetPosition(this);

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='TryGetVisualLineAt']/*"/>
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

	/// <inheritdoc/>
	protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
		=> new PointHitTestResult(this, hitTestParameters.HitPoint);

	/// <inheritdoc/>
	protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
	{
		base.OnPropertyChanged(e);

		// Every input of the cached marker layout is a dependency property, so a change to any of them
		// invalidates the cache and the next marker draw recomputes it. A mutation of a non-frozen icon
		// geometry raises no property change, which is why that case is not cached at all.
		if (e.Property == IconGeometryProperty ||
			e.Property == MarginWidthProperty ||
			e.Property == TextBlock.FontSizeProperty ||
			e.Property == ActualWidthProperty)
		{
			_layoutStamp++;
		}
	}

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='GetMarkerLayout']/*"/>
	private MarkerLayout GetMarkerLayout(Geometry iconGeometry)
	{
		// A mutable geometry's bounds can change without a property change, which the stamp cannot observe,
		// so only a frozen geometry - whose bounds are fixed - is cached. Anything else is recomputed.
		if (!iconGeometry.IsFrozen)
			return CreateMarkerLayout(iconGeometry);

		if (_computedLayoutStamp != _layoutStamp)
		{
			_markerLayout = CreateMarkerLayout(iconGeometry);
			_computedLayoutStamp = _layoutStamp;
		}

		return _markerLayout;
	}

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='CreateMarkerLayout']/*"/>
	private MarkerLayout CreateMarkerLayout(Geometry iconGeometry)
	{
		Rect bounds = iconGeometry.Bounds;

		// An icon without bounds (the base default) draws nothing.
		if (bounds.IsEmpty)
			return default;

		double scale = GetFontScale();
		double iconWidth = bounds.Width * scale;
		double iconHeight = bounds.Height * scale;
		double iconLeft = (GetEffectiveMarginWidth() - iconWidth) / 2.0;

		return new MarkerLayout(
			HasIcon: true,
			IconHeight: iconHeight,
			BaseMatrix: new Matrix(
				scale,
				0.0,
				0.0,
				scale,
				iconLeft - (bounds.Left * scale),
				-(bounds.Top * scale)));
	}

	/// <summary>
	/// Rejects <see langword="null"/> so XAML and <c>SetValue</c> assignments cannot crash the render pass.
	/// </summary>
	private static bool ValidateIconBrush(object value)
		=> RejectNullValue(value);

	/// <summary>
	/// Rejects <see langword="null"/> so XAML and <c>SetValue</c> assignments cannot crash the render pass.
	/// </summary>
	private static bool ValidateIconGeometry(object value)
		=> RejectNullValue(value);

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='MarkerLayout']/*"/>
	private readonly record struct MarkerLayout(bool HasIcon, double IconHeight, Matrix BaseMatrix);
}
