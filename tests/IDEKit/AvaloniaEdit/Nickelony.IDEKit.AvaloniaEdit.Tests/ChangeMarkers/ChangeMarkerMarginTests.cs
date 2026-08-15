using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.ChangeMarkers;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

/// <summary>
/// The AvaloniaEdit binding of the shared <see cref="ChangeMarkerMarginTestsBase"/> change-marker margin
/// scenarios; it supplies the mirror's arranged-width and marker-bounds hooks and adds the styled-property
/// registration scenarios.
/// </summary>
[AvaloniaTestClass]
public sealed class ChangeMarkerMarginTests : ChangeMarkerMarginTestsBase
{
	[TestMethod]
	public void MarginWidthProperty_EffectiveDefaultIsFourDip_BaseRegistrationIsSixteenDip()
	{
		// The property is registered on LineStatusMarginBase; ChangeMarkerMargin overrides its default value.
		Assert.AreEqual(16.0, LineStatusMarginBase.MarginWidthProperty.GetDefaultValue(typeof(LineStatusMarginBase)));

		// Creating the margin runs ChangeMarkerMargin's static registration (the metadata override), so the
		// default-value read below does not depend on another test having created an instance first.
		ChangeMarkerMargin margin = CreateMargin();

		Assert.AreEqual(4.0, ChangeMarkerMargin.MarginWidthProperty.GetDefaultValue(typeof(ChangeMarkerMargin)));

		// Avalonia has no FrameworkPropertyMetadata AffectsMeasure/AffectsRender flags. ChangeMarkerMargin
		// registers MarginWidthProperty with AffectsMeasure<ChangeMarkerMargin>, which the behavioral
		// check below pins instead of reading the flag.

		Assert.AreEqual(4.0, margin.MarginWidth);

		margin.Measure(new Size(100.0, 100.0));

		Assert.IsTrue(margin.IsMeasureValid);

		margin.MarginWidth = 8.0;

		Assert.IsFalse(margin.IsMeasureValid, "Changing MarginWidth must invalidate the margin's measure.");
	}

	[TestMethod]
	public void MarkerBrushProperty_DefaultIsFrozenBlue_AffectsRender()
	{
		var defaultValue = (IBrush)ChangeMarkerMargin.MarkerBrushProperty.GetDefaultValue(typeof(ChangeMarkerMargin))!;

		Assert.AreEqual(Color.FromRgb(0x1E, 0x90, 0xFF), ((ISolidColorBrush)defaultValue).Color);

		// Avalonia has no Freeze; the sample default is the immutable (frozen-equivalent) brush the
		// mirror installs, so the default and the sample property are the same instance.
		Assert.IsInstanceOfType<ImmutableSolidColorBrush>(defaultValue);
		Assert.AreSame(ChangeMarkerMargin.SampleMarkerBrush, defaultValue);

		// ChangeMarkerMargin registers MarkerBrushProperty with AffectsRender<ChangeMarkerMargin>.
		// Avalonia exposes no AffectsRender metadata flag and no public "visual is dirty" read, so the
		// flag assertion is dropped; the registration itself is the render-invalidation contract.
	}

	protected override double GetArrangedWidth(LineStatusMarginBase margin) => margin.Bounds.Width;

	protected override Rect CaptureMarkerBounds(LineStatusMarginBase margin, VisualLine visualLine)
	{
		var drawing = new DrawingGroup();

		using (DrawingContext drawingContext = drawing.Open())
			margin.DrawMarker(drawingContext, visualLine, 0.0);

		return drawing.GetBounds();
	}
}
