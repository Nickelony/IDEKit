#if AVALONIAEDIT
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

/// <summary>
/// Verifies the icon-margin base defaults: the empty default icon draws nothing in a marker draw pass,
/// and the default click action leaves the press unhandled. The icon brushes, geometries, and click
/// actions the concrete margins supply are covered by their own margin tests.
/// </summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class LineStatusIconMarginBaseTests
{
	[TestMethod]
	public void IconGeometry_Default_IsEmpty()
	{
		var margin = new TestIconMargin();

		// The base registration defaults to an empty geometry; a derived margin overrides the property
#if AVALONIAEDIT
		// metadata with its own icon. Avalonia's Geometry.Empty is a distinct StreamGeometry instance,
		// so the mirror asserts the default has empty bounds instead of matching the singleton by
		// reference the way the WPF reference did.
		Rect bounds = margin.IconGeometry.Bounds;

		Assert.AreEqual(0.0, bounds.Width);
		Assert.AreEqual(0.0, bounds.Height);
#else
		// metadata with its own icon.
		Assert.AreSame(Geometry.Empty, margin.IconGeometry);
		Assert.IsTrue(margin.IconGeometry.Bounds.IsEmpty);
#endif
	}

	[TestMethod]
	public void DrawMarker_DefaultEmptyIcon_DrawsNothing()
	{
		var document = new TextDocument("one\r\ntwo");
		var margin = new TestIconMargin();
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginScope(editor, margin);

		VisualLine firstLine = editor.TextArea.TextView.VisualLines[0];

		// The empty default geometry draws nothing at all, so the draw pass leaves an empty visual.
		Assert.IsNull(DrawMarker(margin, firstLine));

		// Control: a non-empty icon does paint, so the empty drawing above is the default geometry's doing
		// and not a harness that never records anything.
		margin.IconGeometry = new RectangleGeometry(new Rect(0.0, 0.0, 10.0, 9.0));

		DrawingGroup? drawing = DrawMarker(margin, firstLine);

		Assert.IsNotNull(drawing);
#if AVALONIAEDIT

		Rect bounds = drawing.GetBounds();

		Assert.IsGreaterThan(0.0, bounds.Width);
		Assert.IsGreaterThan(0.0, bounds.Height);
#else
		Assert.IsFalse(drawing.Bounds.IsEmpty);
#endif
	}

	[TestMethod]
	public void OnMouseLeftButtonDown_DefaultOnIconClicked_LeavesTheEventUnhandled()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var margin = new TestIconMargin();
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginScope(editor, margin);

		VisualLine secondLine = editor.TextArea.TextView.VisualLines
			.First(line => line.FirstDocumentLine.LineNumber == 2);

		var clickPosition = new Point(
			4.0,
			secondLine.GetTextLineVisualYPosition(secondLine.TextLines[0], VisualYPosition.TextTop) + 2.0);

		margin.ClickPosition = clickPosition;

		// The premise: the click position really resolves to a visual line, so the unhandled assertion
		// below covers the base click action and not a failed hit test.
		Assert.IsTrue(margin.TryResolveVisualLine(clickPosition));

#if !AVALONIAEDIT
		var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
		{
			RoutedEvent = UIElement.MouseLeftButtonDownEvent
		};

		margin.RaiseEvent(args);

#endif
		// The base OnIconClicked reports no action, so the press stays unhandled and reaches the editor.
#if AVALONIAEDIT
		// Avalonia's routed PointerPressedEventArgs cannot be built with a deterministic position in a
		// test, so the mirror reports the handled result directly through the resolve-then-activate seam
		// the margin's OnPointerPressed handler runs, instead of asserting PointerPressedEventArgs.Handled.
		Assert.IsFalse(margin.SimulateLeftButtonDown());
#else
		Assert.IsFalse(args.Handled);
#endif
	}

#if AVALONIAEDIT
	private static DrawingGroup? DrawMarker(LineStatusIconMarginBase margin, VisualLine visualLine)
	{
		// Avalonia has no DrawingVisual/RenderOpen; a DrawingGroup captures the same recorded drawing
		// content and exposes an empty Children collection when the margin drew nothing.
		var drawing = new DrawingGroup();

		using (DrawingContext drawingContext = drawing.Open())
			margin.DrawMarker(drawingContext, visualLine, 0.0);

		return drawing.Children.Count == 0 ? null : drawing;
	}
#else
	private static DrawingGroup? DrawMarker(TestIconMargin margin, VisualLine visualLine)
	{
		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
			margin.DrawMarker(drawingContext, visualLine, 0.0);

		return visual.Drawing;
	}
#endif

	private sealed class TestIconMargin : LineStatusIconMarginBase
	{
		public Point? ClickPosition { get; set; }

		public bool TryResolveVisualLine(Point position)
			=> TryGetVisualLineAt(position, out _);

#if AVALONIAEDIT
		/// <summary>
		/// Runs the margin's routed click path for the configured position without a routed pointer event:
		/// the click is resolved to a visual line and the icon action is invoked, returning whether the
		/// press was handled. A test seam, because a synthetic Avalonia pointer event cannot carry a
		/// deterministic position.
		/// </summary>
		/// <returns><see langword="true"/> when the click resolved to a line and was handled.</returns>
		public bool SimulateLeftButtonDown()
			=> ClickPosition is Point position
				&& TryGetVisualLineAt(position, out VisualLine? visualLine)
				&& OnIconClicked(visualLine, position);
#else
		protected override IReadOnlyList<int> GetMarkedLineNumbers() => [1];
#endif

#if AVALONIAEDIT
		protected override IReadOnlyList<int> GetMarkedLineNumbers() => [1];
#else
		// A synthetic mouse event carries no position, so the margin's documented click seam supplies
		// the deterministic position instead of moving the host window under the physical pointer.
		protected override Point ResolveClickPosition(MouseButtonEventArgs e)
			=> ClickPosition ?? base.ResolveClickPosition(e);
#endif
	}
}
