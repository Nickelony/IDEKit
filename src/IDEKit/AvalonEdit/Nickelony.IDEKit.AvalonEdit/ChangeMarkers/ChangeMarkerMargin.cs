using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.Core.LineStatus;
using Nickelony.IDEKit.Core.Notifications;
using Nickelony.IDEKit.Infrastructure;
using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.ChangeMarkers;

/// <include file="../../../../../shared/docs/ChangeMarkerMargin.xml" path="doc/members/member[@name='ChangeMarkerMargin.Class']/*"/>
public class ChangeMarkerMargin : LineStatusMarginBase
{
	private static readonly SolidColorBrush s_sampleMarkerBrush = BrushHelpers.CreateFrozenBrush(Color.FromRgb(0x1E, 0x90, 0xFF));

	static ChangeMarkerMargin()
	{
		// A 4-DIP bar matches the width familiar from diff views, instead of the
		// 16-DIP reserved width that the base class uses for icon margins.
		MarginWidthProperty.OverrideMetadata(
			typeof(ChangeMarkerMargin),
			new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
	}

	/// <include file="../../../../../shared/docs/ChangeMarkerMargin.xml" path="doc/members/member[@name='SampleMarkerBrush']/*"/>
	public static Brush SampleMarkerBrush => s_sampleMarkerBrush;

	private readonly ILineStatusSource _markerSource;

	/// <include file="../../../../../shared/docs/ChangeMarkerMargin.xml" path="doc/members/member[@name='MarkerBrushProperty']/*"/>
	public static readonly DependencyProperty MarkerBrushProperty = DependencyProperty.Register(
		nameof(MarkerBrush),
		typeof(Brush),
		typeof(ChangeMarkerMargin),
		new FrameworkPropertyMetadata(s_sampleMarkerBrush, FrameworkPropertyMetadataOptions.AffectsRender),
		ValidateMarkerBrush);

	/// <include file="../../../../../shared/docs/ChangeMarkerMargin.xml" path="doc/members/member[@name='MarkerBrush']/*"/>
	/// <exception cref="ArgumentException">
	/// The value assigned through <c>SetValue</c> or XAML is not a <see cref="Brush"/>; the property's
	/// validation callback rejects it. A direct assignment cannot pass such a value because the CLR
	/// property type is already <see cref="Brush"/>.
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

		drawingContext.DrawRectangle(MarkerBrush, null, new Rect(0.0, visualTop, markerWidth, visualLine.Height));
	}

	/// <summary>
	/// Rejects <see langword="null"/> so XAML and <c>SetValue</c> assignments cannot crash the render pass.
	/// </summary>
	private static bool ValidateMarkerBrush(object value)
		=> RejectNullValue(value);
}
