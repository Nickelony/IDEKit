using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.AvaloniaEdit.Rendering;

/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='LineStatusIconMarginBase.Class']/*"/>
/// <remarks>
/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='LineStatusIconMarginBase.Remarks']/*"/>
/// <para>
/// Avalonia has no frozen freezables, so the cached marker layout is keyed on the property stamp and the
/// geometry reference instead of the WPF <c>IsFrozen</c> test; a geometry mutated in place without a
/// property change is not observed, matching the practical editor usage where the icon geometry is
/// replaced rather than edited.
/// </para>
/// </remarks>
public abstract class LineStatusIconMarginBase : LineStatusMarginBase
{
	private static readonly Geometry s_emptyIconGeometry = new StreamGeometry();

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='IconBrushProperty']/*"/>
	public static readonly StyledProperty<IBrush> IconBrushProperty = AvaloniaProperty.Register<LineStatusIconMarginBase, IBrush>(
		nameof(IconBrush),
		defaultValue: Brushes.Gray,
		validate: static value => RejectNullValue(value));

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='IconGeometryProperty']/*"/>
	public static readonly StyledProperty<Geometry> IconGeometryProperty = AvaloniaProperty.Register<LineStatusIconMarginBase, Geometry>(
		nameof(IconGeometry),
		defaultValue: s_emptyIconGeometry,
		validate: static value => RejectNullValue(value));

	// The marker layout computed for the current marker properties, together with the stamp it was computed
	// for. The stamp is bumped whenever a property the layout depends on changes, so a render pass that
	// draws several markers reuses one computation instead of rereading the icon geometry, the margin width,
	// and the font size per marked line. Both fields are touched on the margin's dispatcher thread only.
	private MarkerLayout _markerLayout;

	private Geometry? _cachedLayoutGeometry;
	private int _layoutStamp;
	private int _computedLayoutStamp = -1;

	static LineStatusIconMarginBase()
	{
		AffectsRender<LineStatusIconMarginBase>(IconBrushProperty, IconGeometryProperty);
	}

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='IconBrush']/*"/>
	public IBrush IconBrush
	{
		get => GetValue(IconBrushProperty);
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			SetValue(IconBrushProperty, value);
		}
	}

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='IconGeometry']/*"/>
	public Geometry IconGeometry
	{
		get => GetValue(IconGeometryProperty);
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

		// A marker draw is a render-hot path, and Avalonia's drawing context records the transform by
		// value, so a fresh matrix is built per marker from the shared layout with the icon's vertical
		// offset applied.
		Matrix markerMatrix = new(
			layout.BaseMatrix.M11,
			layout.BaseMatrix.M12,
			layout.BaseMatrix.M21,
			layout.BaseMatrix.M22,
			layout.BaseMatrix.M31,
			layout.BaseMatrix.M32 + iconTop);

		using (drawingContext.PushTransform(markerMatrix))
		{
			drawingContext.DrawGeometry(IconBrush, null, iconGeometry);
		}
	}

	/// <inheritdoc/>
	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);

		if (e.Handled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
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
	protected virtual Point ResolveClickPosition(PointerPressedEventArgs e) => e.GetPosition(this);

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
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		// Every input of the cached marker layout is a property, so a change to any of them invalidates
		// the cache and the next marker draw recomputes it. A mutation of an icon geometry that raises no
		// property change is not observed, which is why the reference key includes the geometry reference.
		if (change.Property == IconGeometryProperty ||
			change.Property == MarginWidthProperty ||
			change.Property == TextBlock.FontSizeProperty ||
			change.Property == BoundsProperty)
		{
			_layoutStamp++;
		}
	}

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='GetMarkerLayout']/*"/>
	private MarkerLayout GetMarkerLayout(Geometry iconGeometry)
	{
		// Avalonia has no frozen geometry, so the cache is keyed on the property stamp and the geometry
		// reference: a different geometry instance, the margin width, the font size, or the arranged width
		// all invalidate it.
		if (_computedLayoutStamp != _layoutStamp || !ReferenceEquals(_cachedLayoutGeometry, iconGeometry))
		{
			_markerLayout = CreateMarkerLayout(iconGeometry);
			_computedLayoutStamp = _layoutStamp;
			_cachedLayoutGeometry = iconGeometry;
		}

		return _markerLayout;
	}

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='CreateMarkerLayout']/*"/>
	private MarkerLayout CreateMarkerLayout(Geometry iconGeometry)
	{
		Rect bounds = iconGeometry.Bounds;

		// An icon without usable bounds (the base default) draws nothing.
		if (!HasUsableBounds(bounds))
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
	/// Reports whether the geometry's bounds describe a paintable icon.
	/// </summary>
	/// <remarks>
	/// Avalonia's <c>Rect.IsEmpty</c> is not public, so an empty icon (the base default) is
	/// detected by its non-positive or non-finite size instead.
	/// </remarks>
	private static bool HasUsableBounds(Rect bounds)
		=> bounds.Width > 0.0 && bounds.Height > 0.0 && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height);

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='MarkerLayout']/*"/>
	private readonly record struct MarkerLayout(bool HasIcon, double IconHeight, Matrix BaseMatrix);
}
