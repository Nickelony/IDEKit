using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Media;
using Nickelony.IDEKit.Core.LineStatus;

namespace Nickelony.IDEKit.AvalonEdit.LineStatus;

/// <include file="../../../../../shared/docs/ChangeMarkerMargin.xml" path="doc/members/member[@name='ChangeMarkerMargin.Class']/*"/>
public sealed partial class ChangeMarkerMargin : LineStatusMarginBase
{
	private static readonly SolidColorBrush s_sampleMarkerBrush = BrushAndPenFactory.CreateBrush(
		Color.FromRgb(SampleMarkerRed, SampleMarkerGreen, SampleMarkerBlue)
	);

	static ChangeMarkerMargin()
	{
		// The AffectsMeasure flag is not repeated because WPF merges the base metadata's flags into an override,
		// so the base registration already applies.
		MarginWidthProperty.OverrideMetadata(typeof(ChangeMarkerMargin), new FrameworkPropertyMetadata(BarWidth));
	}

	/// <include file="../../../../../shared/docs/ChangeMarkerMargin.xml" path="doc/members/member[@name='SampleMarkerBrush']/*"/>
	public static Brush SampleMarkerBrush => s_sampleMarkerBrush;

	/// <include file="../../../../../shared/docs/ChangeMarkerMargin.xml" path="doc/members/member[@name='MarkerBrushProperty']/*"/>
	public static readonly DependencyProperty MarkerBrushProperty = DependencyProperty.Register(
		name: nameof(MarkerBrush),
		propertyType: typeof(Brush),
		ownerType: typeof(ChangeMarkerMargin),
		typeMetadata: new FrameworkPropertyMetadata(s_sampleMarkerBrush, FrameworkPropertyMetadataOptions.AffectsRender),
		validateValueCallback: ValidateMarkerBrush
	);

	/// <include file="../../../../../shared/docs/ChangeMarkerMargin.xml" path="doc/members/member[@name='MarkerBrush']/*"/>
	/// <exception cref="ArgumentException">
	/// The value assigned through <c>SetValue</c> or XAML is <see langword="null"/>; the property's validation
	/// callback rejects it. A direct assignment of <see langword="null"/> throws <see cref="ArgumentNullException"/> from the setter
	/// before the callback runs.
	/// </exception>
	public Brush MarkerBrush
	{
		get => (Brush)GetValue(MarkerBrushProperty);
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			SetValue(MarkerBrushProperty, value);
		}
	}

	/// <inheritdoc/>
	protected internal override void DrawMarker(DrawingContext drawingContext, VisualLine visualLine, double visualTop)
	{
		double markerWidth = GetEffectiveMarginWidth();
		drawingContext.DrawRectangle(MarkerBrush, null, new Rect(x: 0.0, y: visualTop, width: markerWidth, height: visualLine.Height));
	}

	/// <summary>Rejects <see langword="null"/> so XAML and <c>SetValue</c> assignments cannot crash the render pass.</summary>
	private static bool ValidateMarkerBrush(object value) => RejectNullValue(value);
}
