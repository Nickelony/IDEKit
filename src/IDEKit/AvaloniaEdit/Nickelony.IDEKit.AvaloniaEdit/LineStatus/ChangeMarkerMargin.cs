using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Media;
using Nickelony.IDEKit.Core.LineStatus;

namespace Nickelony.IDEKit.AvaloniaEdit.LineStatus;

/// <include file="../../../../../shared/docs/ChangeMarkerMargin.xml" path="doc/members/member[@name='ChangeMarkerMargin.Class']/*"/>
public sealed partial class ChangeMarkerMargin : LineStatusMarginBase
{
	private static readonly ImmutableSolidColorBrush s_sampleMarkerBrush = BrushAndPenFactory.CreateBrush(
		Color.FromRgb(SampleMarkerRed, SampleMarkerGreen, SampleMarkerBlue)
	);

	/// <include file="../../../../../shared/docs/ChangeMarkerMargin.xml" path="doc/members/member[@name='MarkerBrushProperty']/*"/>
	public static readonly StyledProperty<IBrush> MarkerBrushProperty = AvaloniaProperty.Register<ChangeMarkerMargin, IBrush>(
		nameof(MarkerBrush),
		defaultValue: s_sampleMarkerBrush,
		validate: static value => RejectNullValue(value)
	);

	static ChangeMarkerMargin()
	{
		// The base class already registers AffectsMeasure for MarginWidthProperty, so it is not repeated here.
		MarginWidthProperty.OverrideDefaultValue<ChangeMarkerMargin>(BarWidth);
		AffectsRender<ChangeMarkerMargin>(MarkerBrushProperty);
	}

	/// <include file="../../../../../shared/docs/ChangeMarkerMargin.xml" path="doc/members/member[@name='SampleMarkerBrush']/*"/>
	public static IBrush SampleMarkerBrush => s_sampleMarkerBrush;

	/// <include file="../../../../../shared/docs/ChangeMarkerMargin.xml" path="doc/members/member[@name='MarkerBrush']/*"/>
	public IBrush MarkerBrush
	{
		get => GetValue(MarkerBrushProperty);
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

		drawingContext.DrawRectangle(
			brush: MarkerBrush,
			pen: null,
			rect: new Rect(x: 0.0, y: visualTop, width: markerWidth, height: visualLine.Height),
			radiusX: 0.0,
			radiusY: 0.0,
			boxShadows: default
		);
	}
}
