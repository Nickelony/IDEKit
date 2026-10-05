#if AVALONIAEDIT
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.LineStatus;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.LineStatus;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

/// <summary>
/// The shared <see cref="LineStatusIconMarginBase"/> scenarios that run identically across the editor bindings: the empty default icon draws
/// nothing in a marker draw pass, and the click path resolves the line under a position and reports the click action's result.
/// </summary>
/// <remarks>
/// A binding supplies the engine-specific operations through the hooks below: the empty-geometry assertion, the marker draw pass, and the assertion
/// that a drawing paints. The icon brushes, geometries, and click actions the concrete margins supply are covered by their own margin tests.
/// </remarks>
public abstract class LineStatusIconMarginBaseTestsBase
{
	/// <summary>Asserts that the margin's default icon geometry is empty.</summary>
	/// <param name="margin">The margin whose default icon geometry is read.</param>
	protected abstract void AssertDefaultIconGeometryIsEmpty(LineStatusIconMarginBase margin);

	/// <summary>Runs the margin's marker draw pass for a visual line into a drawing.</summary>
	/// <param name="margin">The margin whose draw pass runs.</param>
	/// <param name="visualLine">The visual line to draw the marker for.</param>
	/// <returns>The recorded drawing, or <see langword="null"/> when the margin drew nothing.</returns>
	protected abstract DrawingGroup? DrawMarker(LineStatusIconMarginBase margin, VisualLine visualLine);

	/// <summary>Asserts that a drawing the margin recorded paints something.</summary>
	/// <param name="drawing">The drawing to inspect.</param>
	protected abstract void AssertDrawingPaints(DrawingGroup drawing);

	/// <summary>Creates a rectangle icon geometry in the form the margin caches its layout for.</summary>
	/// <param name="bounds">The bounds of the icon.</param>
	/// <returns>The geometry; a frozen one where the engine can freeze.</returns>
	protected abstract Geometry CreateCacheableIconGeometry(Rect bounds);

	/// <summary>Returns the bounds a recorded drawing paints, with the transforms it pushed applied.</summary>
	/// <param name="drawing">The drawing to inspect.</param>
	/// <returns>The painted bounds.</returns>
	protected abstract Rect GetDrawingBounds(DrawingGroup drawing);

	[TestMethod]
	public void IconGeometry_Default_IsEmpty()
	{
		var margin = new TestIconMargin();

		// The base registration defaults to an empty geometry; a derived margin overrides the property
		// metadata with its own icon.
		AssertDefaultIconGeometryIsEmpty(margin);
	}

	[TestMethod]
	public void DrawMarker_DefaultEmptyIcon_DrawsNothing()
	{
		var document = new TextDocument("one\r\ntwo");
		var margin = new TestIconMargin();
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginTestHost(editor, margin);

		VisualLine firstLine = editor.TextArea.TextView.VisualLines[0];

		// The empty default geometry draws nothing at all, so the draw pass leaves an empty visual.
		Assert.IsNull(DrawMarker(margin, firstLine));

		// Control: a non-empty icon does paint, so the empty drawing above is the default geometry's doing
		// and not a harness that never records anything.
		margin.IconGeometry = new RectangleGeometry(new Rect(x: 0.0, y: 0.0, width: 10.0, height: 9.0));

		DrawingGroup? drawing = DrawMarker(margin, firstLine);

		Assert.IsNotNull(drawing);
		AssertDrawingPaints(drawing);
	}

	[TestMethod]
	public void DrawMarker_FontSizeChangedBetweenPasses_ScalesTheIconOfTheNextPass()
	{
		var document = new TextDocument("one\r\ntwo");
		var margin = new TestIconMargin { IconGeometry = CreateCacheableIconGeometry(new Rect(x: 0.0, y: 0.0, width: 10.0, height: 9.0)) };
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginTestHost(editor, margin);

		Rect firstPass = GetDrawingBounds(DrawMarker(margin, editor.TextArea.TextView.VisualLines[0])!);

		// A second pass at the same font size reuses the cached layout and must draw the same icon.
		Rect repeatedPass = GetDrawingBounds(DrawMarker(margin, editor.TextArea.TextView.VisualLines[0])!);

		Assert.AreEqual(firstPass.Height, repeatedPass.Height, 0.001);

		editor.FontSize = 24.0;
		editor.UpdateLayout();

		Rect secondPass = GetDrawingBounds(DrawMarker(margin, editor.TextArea.TextView.VisualLines[0])!);

		Assert.AreEqual(firstPass.Height * 2.0, secondPass.Height, 0.001);
		Assert.AreEqual(firstPass.Width * 2.0, secondPass.Width, 0.001);
	}

	[TestMethod]
	public void DrawMarker_IconGeometryReplacedBetweenPasses_DrawsTheNewGeometry()
	{
		var document = new TextDocument("one\r\ntwo");
		var margin = new TestIconMargin { IconGeometry = CreateCacheableIconGeometry(new Rect(x: 0.0, y: 0.0, width: 10.0, height: 9.0)) };
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginTestHost(editor, margin);

		VisualLine firstLine = editor.TextArea.TextView.VisualLines[0];
		Rect firstPass = GetDrawingBounds(DrawMarker(margin, firstLine)!);

		margin.IconGeometry = CreateCacheableIconGeometry(new Rect(x: 0.0, y: 0.0, width: 10.0, height: 6.0));

		Rect secondPass = GetDrawingBounds(DrawMarker(margin, firstLine)!);

		Assert.AreEqual(9.0, firstPass.Height, 0.001);
		Assert.AreEqual(6.0, secondPass.Height, 0.001);
	}

	[TestMethod]
	public void DrawMarker_ScrolledView_PlacesTheIconInsideTheLineByTheViewportRelativeTop()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("text", 400)));
		var margin = new TestIconMargin { IconGeometry = CreateCacheableIconGeometry(new Rect(x: 0.0, y: 0.0, width: 10.0, height: 9.0)) };
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginTestHost(editor, margin);

		editor.ScrollToLine(299);
		editor.UpdateLayout();

		TextView textView = editor.TextArea.TextView;
		VisualLine firstVisibleLine = textView.VisualLines[0];

		// The premise: the view is scrolled, so a viewport-relative top is not the document-relative one.
		Assert.IsGreaterThan(0.0, textView.VerticalOffset);

		Rect bounds = GetDrawingBounds(DrawMarker(margin, firstVisibleLine)!);

		// The draw pass receives the viewport-relative line top, so with a top of zero the icon sits inside the line box.
		Assert.IsGreaterThanOrEqualTo(0.0, bounds.Top);
		Assert.IsLessThanOrEqualTo(firstVisibleLine.Height, bounds.Bottom);
	}

	[TestMethod]
	public void TryHandleClickAt_DefaultOnIconClicked_ReportsTheClickUnhandled()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var margin = new TestIconMargin();
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginTestHost(editor, margin);

		Point clickPosition = GetPositionOnLine(editor, lineNumber: 2);

		// The premise: the click position really resolves to a visual line, so the unhandled assertion
		// below covers the base click action and not a failed hit test.
		Assert.IsTrue(margin.TryResolveVisualLine(clickPosition));

		// The base OnIconClicked reports no action, so the click stays unhandled and the press reaches the editor.
		Assert.IsFalse(margin.TryHandleClickAt(clickPosition));
	}

	[TestMethod]
	public void TryHandleClickAt_LineUnderThePosition_InvokesOnIconClickedWithTheLineAndThePosition()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var margin = new RecordingIconMargin { Handles = true };
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginTestHost(editor, margin);

		Point clickPosition = GetPositionOnLine(editor, lineNumber: 2);

		Assert.IsTrue(margin.TryHandleClickAt(clickPosition));
		Assert.AreEqual(1, margin.ClickCount);
		Assert.AreEqual(2, margin.ClickedLineNumber);
		Assert.AreEqual(clickPosition, margin.ClickedPosition);

		// The click is reported handled only when the action handled it.
		margin.Handles = false;

		Assert.IsFalse(margin.TryHandleClickAt(clickPosition));
		Assert.AreEqual(2, margin.ClickCount);
	}

	[TestMethod]
	public void TryHandleClickAt_UnattachedMargin_DoesNotInvokeOnIconClicked()
	{
		var margin = new RecordingIconMargin { Handles = true };

		// An unattached margin has no text view, so no position resolves to a line.
		Assert.IsFalse(margin.TryHandleClickAt(new Point(4.0, 4.0)));
		Assert.AreEqual(0, margin.ClickCount);
	}

	[TestMethod]
	public void TryHandleClickAt_ScrolledView_ResolvesTheLineByTheViewportOffset()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("text", 400)));
		var margin = new RecordingIconMargin { Handles = true };
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginTestHost(editor, margin);

		editor.ScrollToLine(299);
		editor.UpdateLayout();

		TextView textView = editor.TextArea.TextView;
		VisualLine firstVisibleLine = textView.VisualLines[0];

		// The premise: the view is scrolled, so a position in the margin is the document position less the viewport offset.
		Assert.IsGreaterThan(0.0, textView.VerticalOffset);

		var clickPosition = new Point(4.0, firstVisibleLine.VisualTop - textView.VerticalOffset + 2.0);

		Assert.IsTrue(margin.TryHandleClickAt(clickPosition));
		Assert.AreEqual(firstVisibleLine.FirstDocumentLine.LineNumber, margin.ClickedLineNumber);
	}

	[TestMethod]
	public void TryHandleClickAt_InvalidatedVisualLines_ReportsNoLine()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var margin = new RecordingIconMargin { Handles = true };
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginTestHost(editor, margin);

		Point clickPosition = GetPositionOnLine(editor, lineNumber: 2);

		// A redraw clears the visual lines until the next layout pass, and a click does not force that pass.
		editor.TextArea.TextView.Redraw();

		Assert.IsFalse(editor.TextArea.TextView.VisualLinesValid, "The premise: the redraw invalidated the visual lines.");
		Assert.IsFalse(margin.TryHandleClickAt(clickPosition));
		Assert.AreEqual(0, margin.ClickCount);
	}

	private static Point GetPositionOnLine(TextEditor editor, int lineNumber)
	{
		VisualLine line = editor.TextArea.TextView.VisualLines.First(visualLine => visualLine.FirstDocumentLine.LineNumber == lineNumber);
		return new Point(4.0, line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextTop) + 2.0);
	}

	/// <summary>An icon margin that marks one line and exposes the line resolution of its base.</summary>
	private sealed class TestIconMargin : LineStatusIconMarginBase
	{
		public bool TryResolveVisualLine(Point position) => TryGetVisualLineAt(position, out _);
		protected override IReadOnlyList<int> GetMarkedLineNumbers() => [1];
	}

	/// <summary>An icon margin that records the click action it receives and reports a configured result.</summary>
	private sealed class RecordingIconMargin : LineStatusIconMarginBase
	{
		public bool Handles { get; set; }

		public int ClickCount { get; private set; }

		public int? ClickedLineNumber { get; private set; }

		public Point? ClickedPosition { get; private set; }

		protected override IReadOnlyList<int> GetMarkedLineNumbers() => [1];

		protected override bool OnIconClicked(VisualLine visualLine, Point position)
		{
			ClickCount++;
			ClickedLineNumber = visualLine.FirstDocumentLine.LineNumber;
			ClickedPosition = position;
			return Handles;
		}
	}
}
