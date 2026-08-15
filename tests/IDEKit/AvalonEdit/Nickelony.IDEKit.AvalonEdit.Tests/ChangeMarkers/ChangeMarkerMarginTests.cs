using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.ChangeMarkers;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

/// <summary>
/// The AvalonEdit binding of the shared <see cref="ChangeMarkerMarginTestsBase"/> change-marker margin
/// scenarios; it supplies the WPF arranged-width and marker-bounds hooks and adds the
/// <c>FrameworkPropertyMetadata</c> registration scenarios.
/// </summary>
[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed class ChangeMarkerMarginTests : ChangeMarkerMarginTestsBase
{
	[TestMethod]
	public void MarginWidthProperty_EffectiveDefaultIsFourDip_BaseRegistrationIsSixteenDip()
	{
		// The property is registered on LineStatusMarginBase; ChangeMarkerMargin overrides its default value.
		Assert.AreEqual(16.0, LineStatusMarginBase.MarginWidthProperty.DefaultMetadata.DefaultValue);

		// Creating the margin runs ChangeMarkerMargin's static registration (the metadata override), so the
		// metadata read below does not depend on another test having created an instance first.
		ChangeMarkerMargin margin = CreateMargin();

		var metadata = (FrameworkPropertyMetadata)ChangeMarkerMargin.MarginWidthProperty.GetMetadata(typeof(ChangeMarkerMargin));

		Assert.AreEqual(4.0, metadata.DefaultValue);
		Assert.IsTrue(metadata.AffectsMeasure);
		Assert.IsFalse(metadata.AffectsRender);

		// The effective default on a margin instance is the overridden value.
		Assert.AreEqual(4.0, margin.MarginWidth);
	}

	[TestMethod]
	public void MarkerBrushProperty_DefaultIsFrozenBlue_AffectsRender()
	{
		var defaultValue = (SolidColorBrush)ChangeMarkerMargin.MarkerBrushProperty.DefaultMetadata.DefaultValue;

		Assert.AreEqual(Color.FromRgb(0x1E, 0x90, 0xFF), defaultValue.Color);
		Assert.IsTrue(defaultValue.IsFrozen);

		var metadata = (FrameworkPropertyMetadata)ChangeMarkerMargin.MarkerBrushProperty.GetMetadata(typeof(ChangeMarkerMargin));

		Assert.IsTrue(metadata.AffectsRender);
		Assert.IsFalse(metadata.AffectsMeasure);
	}

	protected override double GetArrangedWidth(LineStatusMarginBase margin) => margin.ActualWidth;

	protected override Rect CaptureMarkerBounds(LineStatusMarginBase margin, VisualLine visualLine)
	{
		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
			margin.DrawMarker(drawingContext, visualLine, 0.0);

		DrawingGroup? drawing = visual.Drawing;

		Assert.IsNotNull(drawing);

		return drawing.Bounds;
	}
}
