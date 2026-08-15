using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using Nickelony.IDEKit.Core.LineStatus;
using Nickelony.IDEKit.Core.Notifications;
using Nickelony.IDEKit.Infrastructure;

namespace Nickelony.IDEKit.AvaloniaEdit.ChangeMarkers;

/// <include file="../../../../../shared/docs/ChangeMarkerMargin.xml" path="doc/members/member[@name='ChangeMarkerMargin.Class']/*"/>
public class ChangeMarkerMargin : LineStatusMarginBase
{
	private static readonly ImmutableSolidColorBrush s_sampleMarkerBrush = BrushHelpers.CreateFrozenBrush(Color.FromRgb(0x1E, 0x90, 0xFF));

	/// <include file="../../../../../shared/docs/ChangeMarkerMargin.xml" path="doc/members/member[@name='MarkerBrushProperty']/*"/>
	public static readonly StyledProperty<IBrush> MarkerBrushProperty = AvaloniaProperty.Register<ChangeMarkerMargin, IBrush>(
		nameof(MarkerBrush),
		defaultValue: s_sampleMarkerBrush,
		validate: static value => RejectNullValue(value));

	static ChangeMarkerMargin()
	{
		// A 4-DIP bar matches the width familiar from diff views, instead of the
		// 16-DIP reserved width that the base class uses for icon margins.
		MarginWidthProperty.OverrideDefaultValue<ChangeMarkerMargin>(4.0);
		AffectsMeasure<ChangeMarkerMargin>(MarginWidthProperty);
		AffectsRender<ChangeMarkerMargin>(MarkerBrushProperty);
	}

	/// <include file="../../../../../shared/docs/ChangeMarkerMargin.xml" path="doc/members/member[@name='SampleMarkerBrush']/*"/>
	public static IBrush SampleMarkerBrush => s_sampleMarkerBrush;

	private readonly ILineStatusSource _markerSource;

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

	/// <include file="../../../../../shared/docs/ChangeMarkerMargin.xml" path="doc/members/member[@name='ChangeMarkerMargin']/*"/>
	public ChangeMarkerMargin(ILineStatusSource markerSource)
	{
		ArgumentNullException.ThrowIfNull(markerSource);
		_markerSource = markerSource;

		FollowSourceNotifications(markerSource);
	}

	/// <inheritdoc/>
	protected override IReadOnlyList<int> GetMarkedLineNumbers()
		=> _markerSource.GetMarkedLineNumbers();

	/// <inheritdoc/>
	protected internal override void DrawMarker(DrawingContext drawingContext, VisualLine visualLine, double visualTop)
	{
		double markerWidth = GetEffectiveMarginWidth();

		drawingContext.DrawRectangle(MarkerBrush, null, new Rect(0.0, visualTop, markerWidth, visualLine.Height), 0.0, 0.0, default);
	}
}
