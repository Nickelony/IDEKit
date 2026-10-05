using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.Core.LineStatus;
using Nickelony.IDEKit.Core.Validation;

namespace Nickelony.IDEKit.AvaloniaEdit.LineStatus;

/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='LineStatusIconMarginBase.Class']/*"/>
/// <remarks>
/// <para>
/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='LineStatusIconMarginBase.Remarks']/*"/>
/// </para>
/// <para>
/// Avalonia cannot freeze a geometry, so the cached marker layout is keyed on the property
/// stamp and the geometry reference instead; a geometry mutated in place without a property change is not observed, which is acceptable because hosts
/// replace the icon geometry rather than edit it.
/// </para>
/// </remarks>
public abstract partial class LineStatusIconMarginBase : LineStatusMarginBase
{
	private static readonly Geometry s_emptyIconGeometry = new StreamGeometry();

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='IconBrushProperty']/*"/>
	public static readonly StyledProperty<IBrush> IconBrushProperty = AvaloniaProperty.Register<LineStatusIconMarginBase, IBrush>(
		nameof(IconBrush),
		defaultValue: Brushes.Gray,
		validate: static value => RejectNullValue(value)
	);

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='IconGeometryProperty']/*"/>
	public static readonly StyledProperty<Geometry> IconGeometryProperty = AvaloniaProperty.Register<LineStatusIconMarginBase, Geometry>(
		nameof(IconGeometry),
		defaultValue: s_emptyIconGeometry,
		validate: static value => RejectNullValue(value)
	);

	static LineStatusIconMarginBase() => AffectsRender<LineStatusIconMarginBase>(IconBrushProperty, IconGeometryProperty);

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
	/// <remarks>
	/// Avalonia cannot freeze a geometry, so the getter returns the assigned instance itself: the value is the property
	/// store's instance and, for a derived margin's default, the same instance shared by every instance of that margin. Treat the returned geometry
	/// as read-only and assign a new instance instead of mutating it in place.
	/// </remarks>
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
	public override void Render(DrawingContext context)
	{
		// Avalonia hit-tests a control against the geometry it paints, so a margin that paints only its markers
		// is not hit-testable on an unmarked row. A transparent fill over the whole bounds makes the entire
		// strip reachable.
		if (Bounds.Width > 0.0 && Bounds.Height > 0.0)
			context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

		base.Render(context);
	}

	/// <inheritdoc/>
	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);

		// The pointer update kind is tested instead of IsLeftButtonPressed so a pen or touch press (which has no
		// mouse buttons) still activates the icon; a right-button press keeps its context gesture.
		if (e.Handled || e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
			return;

		if (TryHandleClickAt(e.GetPosition(this)))
			e.Handled = true;
	}

	/// <inheritdoc/>
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		// Every input of the cached marker layout is a property, so a change to any of them invalidates
		// the cache and the next marker draw recomputes it. A mutation of an icon geometry that raises no
		// property change is not observed, so the cache key includes the geometry reference.
		if (
			change.Property == IconGeometryProperty
			|| change.Property == MarginWidthProperty
			|| change.Property == TextBlock.FontSizeProperty
			|| change.Property == BoundsProperty
		)
		{
			InvalidateMarkerLayout();
		}
	}

	/// <summary>Reports whether the geometry's bounds describe a paintable icon.</summary>
	/// <remarks>
	/// Avalonia's <c>Rect.IsEmpty</c> is not public, so an empty icon (the base default) is detected by its non-positive or non-finite size instead.
	/// </remarks>
	private static partial bool HasUsableBounds(Rect bounds) =>
		NumericValidation.IsFinitePositive(bounds.Width) && NumericValidation.IsFinitePositive(bounds.Height);

	/// <summary>Reports whether a layout computed for the geometry can be cached.</summary>
	/// <remarks>
	/// Avalonia cannot freeze a geometry, so every geometry is cached under the layout stamp and its reference.
	/// </remarks>
	private static partial bool IsLayoutCacheable(Geometry iconGeometry) => true;

	private static partial Matrix CreateBaseMatrix(LineStatusIconPlacement placement) =>
		new(scaleX: placement.Scale, skewY: 0.0, skewX: 0.0, scaleY: placement.Scale, offsetX: placement.OffsetX, offsetY: placement.OffsetY);

	/// <summary>Draws the icon under a matrix built for this draw.</summary>
	/// <remarks>
	/// Avalonia's drawing context records the transform by value, so a fresh matrix is built per marker from the shared layout.
	/// </remarks>
	private partial void DrawIcon(DrawingContext drawingContext, Geometry iconGeometry, MarkerLayout layout, double iconTop)
	{
		Matrix markerMatrix = new(
			scaleX: layout.BaseMatrix.M11,
			skewY: layout.BaseMatrix.M12,
			skewX: layout.BaseMatrix.M21,
			scaleY: layout.BaseMatrix.M22,
			offsetX: layout.BaseMatrix.M31,
			offsetY: layout.BaseMatrix.M32 + iconTop
		);

		using (drawingContext.PushTransform(markerMatrix))
			drawingContext.DrawGeometry(IconBrush, null, iconGeometry);
	}
}
