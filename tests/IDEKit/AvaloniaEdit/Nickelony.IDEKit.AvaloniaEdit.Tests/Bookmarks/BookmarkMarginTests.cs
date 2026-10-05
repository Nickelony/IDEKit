using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Bookmarks;
using Nickelony.IDEKit.AvaloniaEdit.LineStatus;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

/// <summary>
/// The AvaloniaEdit binding of the shared <see cref="BookmarkMarginTestsBase"/> bookmark-margin scenarios; it supplies the binding's arranged-width
/// and marker-bounds hooks and adds the styled-property registration scenario.
/// </summary>
/// <remarks>
/// The whole-surface hit-target contract is pinned here: the margin paints a transparent fill over its bounds so Avalonia's hit
/// test resolves it on any row, verified by <see cref="InputHitTest_InsideMargin_ReturnsTheMarginAsTheHitTarget"/>. The routed press path is covered
/// by <see cref="PointerPressed_MouseButtonKind_ControlsIconActivation"/>.
/// </remarks>
[AvaloniaTestClass]
public sealed class BookmarkMarginTests : BookmarkMarginTestsBase
{
	[TestMethod]
	public void MarginWidthProperty_DefaultAndMetadata()
	{
		Assert.AreEqual(16.0, BookmarkMargin.MarginWidthProperty.GetDefaultValue(typeof(BookmarkMargin)));

		// Avalonia has no FrameworkPropertyMetadata AffectsMeasure/AffectsRender flags. The base
		// registration calls AffectsMeasure<LineStatusMarginBase>(MarginWidthProperty), which the
		// behavioral check below pins instead of reading the flag.
		BookmarkMargin margin = CreateMargin(new TextDocument("one\r\ntwo"));

		margin.Measure(new Size(100.0, 100.0));

		Assert.IsTrue(margin.IsMeasureValid);

		margin.MarginWidth = 24.0;

		Assert.IsFalse(margin.IsMeasureValid, "Changing MarginWidth must invalidate the margin's measure.");
	}

	[TestMethod]
	public void IconBrushProperty_DefaultIsAmber_IsImmutable()
	{
		// Reading the sample brush runs BookmarkMargin's static registration (the metadata override), so the
		// default-value read below does not depend on another test having created an instance first.
		IBrush sampleBrush = BookmarkMargin.SampleIconBrush;

		var defaultValue = (IBrush)BookmarkMargin.IconBrushProperty.GetDefaultValue(typeof(BookmarkMargin))!;

		Assert.AreSame(sampleBrush, defaultValue);
		Assert.AreEqual(Color.FromRgb(0xE6, 0xA2, 0x3C), ((ISolidColorBrush)defaultValue).Color);

		// Avalonia has no Freeze; the sample default is an immutable (frozen-equivalent) brush,
		// so the default and the sample property are the same instance.
		Assert.IsInstanceOfType<ImmutableSolidColorBrush>(defaultValue);
		// LineStatusIconMarginBase registers IconBrushProperty with AffectsRender<LineStatusIconMarginBase>; Avalonia exposes no
		// public "visual is dirty" read to assert that behavior, so the pinned contract here is the
		// immutable default brush.
	}

	[TestMethod]
	public void IconGeometryProperty_DefaultIsSampleIcon()
	{
		// Reading the sample geometry runs BookmarkMargin's static registration (the metadata override), so
		// the default-value read below does not depend on another test having created an instance first.
		Geometry sampleGeometry = BookmarkMargin.SampleIconGeometry;

		var defaultValue = (Geometry)BookmarkMargin.IconGeometryProperty.GetDefaultValue(typeof(BookmarkMargin))!;

		Assert.IsNotNull(defaultValue);

		// Avalonia has no Freeze; the sample property hands out a private clone so a host that assigns it
		// cannot mutate the shared default in place, but the clone and the default describe the same shape.
		Assert.AreNotSame(sampleGeometry, defaultValue);
		Assert.AreEqual(sampleGeometry.Bounds, defaultValue.Bounds);

		Rect bounds = defaultValue.Bounds;

		Assert.IsGreaterThan(0.0, bounds.Width);
		Assert.IsGreaterThan(0.0, bounds.Height);
	}

	[TestMethod]
	public void InputHitTest_InsideMargin_ReturnsTheMarginAsTheHitTarget()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var tracker = new BookmarkTracker(() => document);
		var margin = new BookmarkMargin(tracker);
		var editor = new TextEditor { Document = document };

		using var host = new HostedMarginTestHost(editor, margin);

		// The margin accepts hit tests across its whole surface so a click on any row reaches it.
		IInputElement? hitElement = margin.InputHitTest(new Point(4.0, 4.0));

		Assert.AreSame(margin, hitElement);
	}

	[TestMethod]
	public void PointerPressed_MouseButtonKind_ControlsIconActivation()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var tracker = new BookmarkTracker(() => document);
		var margin = new BookmarkMargin(tracker);
		var editor = new TextEditor { Document = document };

		using var host = new HostedMarginTestHost(editor, margin);

		VisualLine secondLine = editor.TextArea.TextView.VisualLines.First(line => line.FirstDocumentLine.LineNumber == 2);
		double clickY = secondLine.GetTextLineVisualYPosition(secondLine.TextLines[0], VisualYPosition.TextTop) + 2.0;

		var clickPosition = new Point(4.0, clickY);

		// A non-left pointer kind must not activate the icon and must leave the event unhandled so the host's
		// context gesture can still run.
		PointerPressedEventArgs rightPress = RaisePointerPressed(margin, clickPosition, PointerUpdateKind.RightButtonPressed);

		Assert.IsFalse(rightPress.Handled);
		Assert.IsEmpty(tracker.GetMarkedLineNumbers());

		// A left press activates the icon and marks the row it landed on.
		PointerPressedEventArgs leftPress = RaisePointerPressed(margin, clickPosition, PointerUpdateKind.LeftButtonPressed);

		Assert.IsTrue(leftPress.Handled);
		Assert.HasCount(1, tracker.GetMarkedLineNumbers());
	}

	// Raises a pointer press on the margin with the supplied pointer kind, exercising the margin's routed
	// press path; the margin is the root visual of the event, so the root position is the margin position.
	private static PointerPressedEventArgs RaisePointerPressed(Interactive element, Point position, PointerUpdateKind pointerUpdateKind)
	{
		RawInputModifiers modifiers =
			pointerUpdateKind == PointerUpdateKind.LeftButtonPressed ? RawInputModifiers.LeftMouseButton : RawInputModifiers.RightMouseButton;

		var eventArgs = new PointerPressedEventArgs(
			source: element,
			pointer: new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true),
			rootVisual: element,
			rootVisualPosition: position,
			timestamp: 0UL,
			properties: new PointerPointProperties(modifiers, pointerUpdateKind),
			modifiers: KeyModifiers.None
		);

		element.RaiseEvent(eventArgs);

		return eventArgs;
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
