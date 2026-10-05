using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.Core.LineStatus;

namespace Nickelony.IDEKit.AvalonEdit.LineStatus;

/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='LineStatusIconMarginBase.Class']/*"/>
/// <remarks>
/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='LineStatusIconMarginBase.Remarks']/*"/>
/// </remarks>
public abstract partial class LineStatusIconMarginBase : LineStatusMarginBase
{
	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='IconBrushProperty']/*"/>
	public static readonly DependencyProperty IconBrushProperty = DependencyProperty.Register(
		name: nameof(IconBrush),
		propertyType: typeof(Brush),
		ownerType: typeof(LineStatusIconMarginBase),
		typeMetadata: new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender),
		validateValueCallback: ValidateIconBrush
	);

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='IconGeometryProperty']/*"/>
	public static readonly DependencyProperty IconGeometryProperty = DependencyProperty.Register(
		name: nameof(IconGeometry),
		propertyType: typeof(Geometry),
		ownerType: typeof(LineStatusIconMarginBase),
		typeMetadata: new FrameworkPropertyMetadata(Geometry.Empty, FrameworkPropertyMetadataOptions.AffectsRender),
		validateValueCallback: ValidateIconGeometry
	);

	/// <include file="../../../../../shared/docs/LineStatusIconMarginBase.xml" path="doc/members/member[@name='IconBrush']/*"/>
	/// <exception cref="ArgumentException">
	/// The value assigned through <c>SetValue</c> or XAML is <see langword="null"/>; the property's validation
	/// callback rejects it. A direct assignment of <see langword="null"/> throws <see cref="ArgumentNullException"/> from the setter
	/// before the callback runs.
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
	/// The value assigned through <c>SetValue</c> or XAML is <see langword="null"/>; the property's validation
	/// callback rejects it. A direct assignment of <see langword="null"/> throws <see cref="ArgumentNullException"/> from the setter
	/// before the callback runs.
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
	protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
	{
		base.OnMouseLeftButtonDown(e);

		if (e.Handled)
			return;

		if (TryHandleClickAt(e.GetPosition(this)))
			e.Handled = true;
	}

	/// <inheritdoc/>
	protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters) =>
		new PointHitTestResult(this, hitTestParameters.HitPoint);

	/// <inheritdoc/>
	protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
	{
		base.OnPropertyChanged(e);

		// Every input of the cached marker layout is a dependency property, so a change to any of them
		// invalidates the cache and the next marker draw recomputes it. A non-frozen icon geometry is not
		// cached at all, so a change to its bounds is seen whether or not a property change reaches the margin.
		if (
			e.Property == IconGeometryProperty
			|| e.Property == MarginWidthProperty
			|| e.Property == TextBlock.FontSizeProperty
			|| e.Property == ActualWidthProperty
		)
		{
			InvalidateMarkerLayout();
		}
	}

	/// <summary>Reports whether the geometry's bounds describe a paintable icon.</summary>
	private static partial bool HasUsableBounds(Rect bounds) => !bounds.IsEmpty;

	/// <summary>Reports whether a layout computed for the geometry can be cached.</summary>
	/// <remarks>
	/// Only a frozen geometry, whose bounds are fixed, is cached; a mutable geometry's layout is recomputed on every draw, so a change to its bounds
	/// is seen without relying on a property change.
	/// </remarks>
	private static partial bool IsLayoutCacheable(Geometry iconGeometry) => iconGeometry.IsFrozen;

	private static partial Matrix CreateBaseMatrix(LineStatusIconPlacement placement) =>
		new(m11: placement.Scale, m12: 0.0, m21: 0.0, m22: placement.Scale, offsetX: placement.OffsetX, offsetY: placement.OffsetY);

	/// <summary>Draws the icon under a transform built for this draw.</summary>
	/// <remarks>
	/// The drawing context records a pushed transform by reference, so each marker needs its own transform; it is frozen so the render thread
	/// can read it without a copy.
	/// </remarks>
	private partial void DrawIcon(DrawingContext drawingContext, Geometry iconGeometry, MarkerLayout layout, double iconTop)
	{
		Matrix markerMatrix = layout.BaseMatrix;
		markerMatrix.OffsetY += iconTop;

		var markerTransform = new MatrixTransform(markerMatrix);
		markerTransform.Freeze();

		drawingContext.PushTransform(markerTransform);
		drawingContext.DrawGeometry(IconBrush, null, iconGeometry);
		drawingContext.Pop();
	}

	/// <summary>Rejects <see langword="null"/> so XAML and <c>SetValue</c> assignments cannot crash the render pass.</summary>
	private static bool ValidateIconBrush(object value) => RejectNullValue(value);

	/// <summary>Rejects <see langword="null"/> so XAML and <c>SetValue</c> assignments cannot crash the render pass.</summary>
	private static bool ValidateIconGeometry(object value) => RejectNullValue(value);
}
