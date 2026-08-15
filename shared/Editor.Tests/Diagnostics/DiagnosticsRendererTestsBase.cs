#if AVALONIAEDIT
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Diagnostics;
using Nickelony.IDEKit.Core.Diagnostics;
using Pen = Avalonia.Media.IPen;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Diagnostics;
using Nickelony.IDEKit.Core.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Pen = System.Windows.Media.Pen;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

/// <summary>
/// The <see cref="DiagnosticsRenderer"/> scenarios whose shape is the same across the editor bindings: the
/// minimum-width widening, the per-severity pen and underline geometry, the batched mixed-severity draw, the
/// normalized empty and reversed ranges, the attached-renderer layer draw, the skip and no-draw cases, the
/// custom pens, and the layer assignment.
/// </summary>
/// <remarks>
/// A binding supplies the engine-specific operations through the hooks below: running the renderer's draw
/// pass into a captured drawing, creating a frozen pen, and asserting the underline geometry's shape (WPF
/// records a <c>StreamGeometry</c> where Avalonia records a platform geometry). The default-pen scenario
/// stays in each binding's own suite, because WPF asserts <c>IsFrozen</c> where Avalonia asserts the
/// immutable sample-pen identity.
/// </remarks>
public abstract class DiagnosticsRendererTestsBase
{
	/// <summary>
	/// Runs the renderer's draw pass into a captured drawing, so the recorded drawings can be inspected.
	/// </summary>
	/// <param name="renderer">The renderer to run.</param>
	/// <param name="textView">The text view to draw against.</param>
	/// <returns>The captured drawing, or <see langword="null"/> when nothing was drawn.</returns>
	protected abstract DrawingGroup? Render(DiagnosticsRenderer renderer, TextView textView);

	/// <summary>
	/// Creates a frozen pen of the given color.
	/// </summary>
	/// <param name="color">The pen color.</param>
	/// <returns>The created pen.</returns>
	protected abstract Pen CreateFrozenPen(Color color);

	/// <summary>
	/// Asserts that the drawn underline geometry has the shape the binding records for an underline.
	/// </summary>
	/// <param name="drawing">The geometry drawing produced by the renderer.</param>
	/// <param name="severity">The severity the underline was drawn for.</param>
	protected abstract void AssertUnderlineGeometryShape(GeometryDrawing drawing, TextDiagnosticSeverity severity);

	[TestMethod]
	public void Draw_WidensRectanglesNarrowerThanMinimumWidth()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument("x"),
			FontSize = 1.0
		};

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		// The premise: the rendered rectangle is narrower than the renderer's 2-DIP minimum.
		// Asserting it here keeps a font-metric change from silently turning this into a vacuous test.
		var segment = new TextSegment { StartOffset = 0, EndOffset = 1 };
		var rects = BackgroundGeometryBuilder
			.GetRectsForSegment(editor.TextArea.TextView, segment, false)
			.ToList();

		Assert.IsNotEmpty(rects);
		Assert.IsTrue(
			rects.All(rect => rect.Width < 2.0),
			"The test font must produce rectangles narrower than the renderer's 2-DIP minimum.");

		var renderer = new DiagnosticsRenderer(
			() => [new TextDiagnosticSegment(0, 1, TextDiagnosticSeverity.Error)]);

		DrawingGroup? drawing = Render(renderer, editor.TextArea.TextView);

		// A zero-width diagnostic (for example an end-of-file error) renders as a widened rectangle
		// instead of being dropped.
		Assert.IsNotNull(drawing);
		Assert.HasCount(1, drawing.Children);

		var geometryDrawing = (GeometryDrawing)drawing.Children[0];

		Assert.IsNotNull(geometryDrawing.Geometry);
		Assert.IsTrue(
			geometryDrawing.Geometry.Bounds.Width >= 1.99,
			"Expected the narrow rectangle to be widened to the renderer's minimum width.");
	}

	[TestMethod]
	public void EnsureRenderableWidth_RectangleNarrowerThanMinimum_IsWidenedToTheMinimum()
	{
		var rect = new Rect(3.0, 4.0, DiagnosticsRenderer.MinimumRenderableRectangleWidth - 0.01, 5.0);

		Rect widened = DiagnosticsRenderer.EnsureRenderableWidth(rect);

		Assert.AreEqual(3.0, widened.X);
		Assert.AreEqual(4.0, widened.Y);
		Assert.AreEqual(DiagnosticsRenderer.MinimumRenderableRectangleWidth, widened.Width);
		Assert.AreEqual(5.0, widened.Height);
	}

	[TestMethod]
	public void EnsureRenderableWidth_RectangleExactlyAtTheMinimum_IsKeptUnchanged()
	{
		// The comparison is inclusive, so a rectangle at the minimum is already renderable and must be
		// returned as measured; a change from ">=" to ">" is caught here.
		var rect = new Rect(1.0, 2.0, DiagnosticsRenderer.MinimumRenderableRectangleWidth, 3.0);

		Rect kept = DiagnosticsRenderer.EnsureRenderableWidth(rect);

		Assert.AreEqual(rect, kept);
	}

	[TestMethod]
	public void EnsureRenderableWidth_RectangleWiderThanTheMinimum_IsKeptUnchanged()
	{
		var rect = new Rect(1.0, 2.0, DiagnosticsRenderer.MinimumRenderableRectangleWidth + 0.01, 3.0);

		Rect kept = DiagnosticsRenderer.EnsureRenderableWidth(rect);

		Assert.AreEqual(rect, kept);
	}

	[TestMethod]
	public void Draw_UsesThePenAndGeometryOfEachSeverity()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument(new string('x', 40)),
			FontSize = 12.0
		};

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		AssertDrawsUnderline(editor, TextDiagnosticSeverity.Error, expectedSquiggle: true);
		AssertDrawsUnderline(editor, TextDiagnosticSeverity.Warning, expectedSquiggle: true);
		AssertDrawsUnderline(editor, TextDiagnosticSeverity.Information, expectedSquiggle: true);
		AssertDrawsUnderline(editor, TextDiagnosticSeverity.Hint, expectedSquiggle: false);
	}

	[TestMethod]
	public void Draw_MultipleSegmentsWithMixedSeverities_BatchesOneGeometryPerSeverity()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument(new string('x', 60)),
			FontSize = 12.0
		};

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		var renderer = new DiagnosticsRenderer(
			() =>
			[
				new TextDiagnosticSegment(0, 5, TextDiagnosticSeverity.Error),
				new TextDiagnosticSegment(10, 15, TextDiagnosticSeverity.Error),
				new TextDiagnosticSegment(20, 25, TextDiagnosticSeverity.Warning),
				new TextDiagnosticSegment(30, 35, TextDiagnosticSeverity.Hint),
			]);

		DrawingGroup? drawing = Render(renderer, editor.TextArea.TextView);

		Assert.IsNotNull(drawing);

		// The two error segments share one batched geometry, so one drawing per severity is produced
		// instead of one drawing per segment.
		Assert.HasCount(3, drawing.Children);

		var errorDrawing = (GeometryDrawing)drawing.Children[0];
		var warningDrawing = (GeometryDrawing)drawing.Children[1];
		var hintDrawing = (GeometryDrawing)drawing.Children[2];

		Assert.AreSame(renderer.ErrorPen, errorDrawing.Pen);
		Assert.AreSame(renderer.WarningPen, warningDrawing.Pen);
		Assert.AreSame(renderer.HintPen, hintDrawing.Pen);

		// The batched error geometry spans both error segments: it must be wider than the geometry
		// produced for the first error segment alone.
		double singleErrorWidth = RenderGeometryWidth(
			editor,
			[new TextDiagnosticSegment(0, 5, TextDiagnosticSeverity.Error)]);

		Assert.IsNotNull(errorDrawing.Geometry);
		Assert.IsGreaterThan(singleErrorWidth, errorDrawing.Geometry.Bounds.Width);
	}

	[TestMethod]
	public void Draw_EmptyRangeAtTopOfDocument_DrawsTheNormalizedSegment()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument("abcdef"),
			FontSize = 12.0
		};

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);
		Assert.AreEqual(0, editor.TextArea.TextView.VisualLines[0].FirstDocumentLine.Offset);

		// An empty range normalizes to a length-1 segment, and the top of the document is visible,
		// so the underline must be drawn even though the requested range is empty.
		double width = RenderGeometryWidth(
			editor,
			[new TextDiagnosticSegment(0, 0, TextDiagnosticSeverity.Error)]);

		Assert.IsGreaterThan(0.0, width);
	}

	[TestMethod]
	public void Draw_ReversedRangeWithinVisibleRange_DrawsTheNormalizedSegment()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 400)));
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// Line 300 is beyond the first viewport, so the scroll moves the first visible offset away from zero.
		editor.ScrollToLine(300);
		editor.UpdateLayout();

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);
		Assert.IsGreaterThan(1, editor.TextArea.TextView.VisualLines[0].FirstDocumentLine.LineNumber);

		int firstVisibleOffset = editor.TextArea.TextView.VisualLines[0].FirstDocumentLine.Offset;

		Assert.IsGreaterThan(0, firstVisibleOffset, "The view must be scrolled past the document start.");

		// The reversed range ends where the visible range starts, so its end offset is not strictly
		// greater than the first visible offset; its normalized form starts two characters into the
		// first visible line and must still be drawn.
		double width = RenderGeometryWidth(
			editor,
			[new TextDiagnosticSegment(firstVisibleOffset + 2, firstVisibleOffset, TextDiagnosticSeverity.Error)]);

		Assert.IsGreaterThan(0.0, width);
	}

	[TestMethod]
	public void Draw_AttachedToARealView_RendersTheUnderlineInTheLayer()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument(new string('x', 40)),
			FontSize = 12.0
		};

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var renderer = new DiagnosticsRenderer(
			() => [new TextDiagnosticSegment(0, 40, TextDiagnosticSeverity.Error)])
		{
			ErrorPen = CreateFrozenPen(Colors.Magenta)
		};

		editor.TextArea.TextView.BackgroundRenderers.Add(renderer);

		var bitmap = TestBitmapRendering.PumpAndRender(editor.TextArea.TextView);

		// The renderer draws in the selection layer; rendering the view must draw its underline.
		Assert.IsTrue(
			TestBitmapRendering.TryGetColorColumnBounds(bitmap, int.MaxValue, Colors.Magenta, out int minColumn, out int maxColumn),
			"Expected the attached renderer to draw the error underline into the rendered view.");
		Assert.IsGreaterThan(1, maxColumn - minColumn);
	}

	[TestMethod]
	public void Draw_SkipsNoneSeveritySegments()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument(new string('x', 40)),
			FontSize = 1.0
		};

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		var renderer = new DiagnosticsRenderer(
			() => [new TextDiagnosticSegment(0, 40, TextDiagnosticSeverity.None)]);

		Assert.IsNull(Render(renderer, editor.TextArea.TextView));
	}

	[TestMethod]
	public void Draw_SkipsUnrecognizedSeveritySegments()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument(new string('x', 40)),
			FontSize = 1.0
		};

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		var renderer = new DiagnosticsRenderer(
			() => [new TextDiagnosticSegment(0, 40, (TextDiagnosticSeverity)42)]);

		// An unrecognized severity is treated like None and draws nothing.
		Assert.IsNull(Render(renderer, editor.TextArea.TextView));
	}

	[TestMethod]
	public void Draw_NullProviderResult_DrawsNothing()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument(new string('x', 40)),
			FontSize = 12.0
		};

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		var renderer = new DiagnosticsRenderer(() => null);

		// A provider that reports "no data" with null is treated like an empty list.
		Assert.IsNull(Render(renderer, editor.TextArea.TextView));
	}

	[TestMethod]
	public void Draw_SegmentOutsideVisibleRange_DrawsNothing()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 400)));
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		// Premise: the segment's line must not be among the rendered visual lines for the test to mean anything.
		Assert.IsFalse(
			editor.TextArea.TextView.VisualLines.Any(line => line.FirstDocumentLine.LineNumber >= 350),
			"Expected line 350 to be outside the visible range.");

		// Line 350 is far below the visible range of the host window.
		int startOffset = document.GetLineByNumber(350).Offset;

		var renderer = new DiagnosticsRenderer(
			() => [new TextDiagnosticSegment(startOffset, startOffset + 4, TextDiagnosticSeverity.Error)]);

		Assert.IsNull(Render(renderer, editor.TextArea.TextView));
	}

	[TestMethod]
	public void Draw_CustomPen_DrawsTheUnderlineWithThatPen()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument(new string('x', 40)),
			FontSize = 12.0
		};

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		var renderer = new DiagnosticsRenderer(
			() => [new TextDiagnosticSegment(0, 40, TextDiagnosticSeverity.Error)]);

		Pen errorPen = CreateFrozenPen(Colors.Magenta);

		renderer.ErrorPen = errorPen;

		DrawingGroup? drawing = Render(renderer, editor.TextArea.TextView);

		Assert.IsNotNull(drawing);
		Assert.HasCount(1, drawing.Children);

		var geometryDrawing = (GeometryDrawing)drawing.Children[0];

		Assert.AreSame(errorPen, geometryDrawing.Pen);
		Assert.IsNotNull(geometryDrawing.Geometry);
		Assert.IsGreaterThan(0.0, geometryDrawing.Geometry.Bounds.Width);
	}

	[TestMethod]
	public void Layer_DefaultsToSelectionAndIsAssignable()
	{
		DiagnosticsRenderer renderer = CreateRenderer();

		// The selection layer is repainted when the view scrolls or its visual lines change, and it
		// exists for any text area; the caret layer redraws on the caret blink instead.
		Assert.AreEqual(KnownLayer.Selection, renderer.Layer);

		renderer.Layer = KnownLayer.Text;

		Assert.AreEqual(KnownLayer.Text, renderer.Layer);
	}

	[TestMethod]
	public void Draw_NullProviderResults_DrawsNothing()
	{
		var editor = new TextEditor { Document = new TextDocument("x") };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		var renderer = new DiagnosticsRenderer(() => null!);

		Assert.IsNull(Render(renderer, editor.TextArea.TextView));
	}

	[TestMethod]
	public void Draw_ViewWithoutDocument_DrawsNothing()
	{
		// An unhosted editor has no document and no valid visual lines; the renderer must return before
		// touching the view's visual lines or querying the segments.
		bool segmentsRequested = false;
		var renderer = new DiagnosticsRenderer(() =>
		{
			segmentsRequested = true;
			return [new TextDiagnosticSegment(0, 1, TextDiagnosticSeverity.Error)];
		});

		DrawingGroup? drawing = Render(renderer, new TextEditor().TextArea.TextView);

		Assert.IsNull(drawing);
		Assert.IsFalse(segmentsRequested, "The segments provider must not be queried for a view that cannot be drawn.");
	}

	[TestMethod]
	public void Draw_CustomPens_DrawEachSeverityWithItsOwnPen()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument(new string('x', 40)),
			FontSize = 12.0
		};

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		var renderer = new DiagnosticsRenderer(
			() =>
			[
				new TextDiagnosticSegment(0, 5, TextDiagnosticSeverity.Error),
				new TextDiagnosticSegment(6, 10, TextDiagnosticSeverity.Warning),
				new TextDiagnosticSegment(11, 15, TextDiagnosticSeverity.Information),
				new TextDiagnosticSegment(16, 20, TextDiagnosticSeverity.Hint),
			]);

		Pen errorPen = CreateFrozenPen(Colors.Magenta);
		Pen warningPen = CreateFrozenPen(Colors.Cyan);
		Pen informationPen = CreateFrozenPen(Colors.Lime);
		Pen hintPen = CreateFrozenPen(Colors.Orange);

		renderer.ErrorPen = errorPen;
		renderer.WarningPen = warningPen;
		renderer.InformationPen = informationPen;
		renderer.HintPen = hintPen;

		DrawingGroup? drawing = Render(renderer, editor.TextArea.TextView);

		Assert.IsNotNull(drawing);
		Assert.HasCount(4, drawing.Children);
		Assert.AreSame(errorPen, ((GeometryDrawing)drawing.Children[0]).Pen);
		Assert.AreSame(warningPen, ((GeometryDrawing)drawing.Children[1]).Pen);
		Assert.AreSame(informationPen, ((GeometryDrawing)drawing.Children[2]).Pen);
		Assert.AreSame(hintPen, ((GeometryDrawing)drawing.Children[3]).Pen);
	}

	[TestMethod]
	public void Draw_ValidVisualLinesWithoutVisibleRange_DrawsNothing()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument(new string('x', 40)),
			FontSize = 12.0,
			Width = 200.0,
			Height = 0.0
		};

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.UpdateLayout();

		// The premise: the view has a valid visual-line state but no visible line to draw against.
		Assert.IsTrue(editor.TextArea.TextView.VisualLinesValid);
		Assert.IsEmpty(editor.TextArea.TextView.VisualLines);

		bool segmentsRequested = false;
		var renderer = new DiagnosticsRenderer(() =>
		{
			segmentsRequested = true;
			return [new TextDiagnosticSegment(0, 5, TextDiagnosticSeverity.Error)];
		});

		DrawingGroup? drawing = Render(renderer, editor.TextArea.TextView);

		// The provider is queried, but the empty visible range filters every segment out.
		Assert.IsTrue(segmentsRequested);
		Assert.IsNull(drawing);
	}

	[TestMethod]
	public void Draw_OnABareTextView_DrawsOnlyTheRendererOnTheBackgroundLayer()
	{
		// A bare TextView has no text area and therefore no selection layer, so a renderer left on the
		// default layer never draws; assigning the background layer before attaching is the documented
		// contract for a host that composes the view itself.
		var textView = new TextView
		{
			Document = new TextDocument(new string('x', 40))
		};

		using HostWindow hostWindow = TestHost.ShowInHostWindow(textView);

		Assert.IsNotEmpty(textView.VisualLines);

		var backgroundLayerRenderer = new DiagnosticsRenderer(
			() => [new TextDiagnosticSegment(0, 40, TextDiagnosticSeverity.Error)])
		{
			ErrorPen = CreateFrozenPen(Colors.Magenta),
			Layer = KnownLayer.Background
		};

		var defaultLayerRenderer = new DiagnosticsRenderer(
			() => [new TextDiagnosticSegment(0, 40, TextDiagnosticSeverity.Error)])
		{
			ErrorPen = CreateFrozenPen(Colors.Blue)
		};

		Assert.AreEqual(
			KnownLayer.Selection,
			defaultLayerRenderer.Layer,
			"The premise: the second renderer keeps the default layer.");

		textView.BackgroundRenderers.Add(backgroundLayerRenderer);
		textView.BackgroundRenderers.Add(defaultLayerRenderer);

		var bitmap = TestBitmapRendering.PumpAndRender(textView);

		Assert.IsTrue(
			TestBitmapRendering.TryGetColorColumnBounds(bitmap, int.MaxValue, Colors.Magenta, out int minColumn, out int maxColumn),
			"Expected the background-layer renderer to draw on a bare text view.");
		Assert.IsGreaterThan(1, maxColumn - minColumn);
		Assert.IsFalse(
			TestBitmapRendering.TryGetColorColumnBounds(bitmap, int.MaxValue, Colors.Blue, out _, out _),
			"A renderer on the default selection layer must not draw on a bare text view.");
	}

	private double RenderGeometryWidth(TextEditor editor, IReadOnlyList<TextDiagnosticSegment> segments)
	{
		var renderer = new DiagnosticsRenderer(() => segments);
		DrawingGroup? drawing = Render(renderer, editor.TextArea.TextView);

		Assert.IsNotNull(drawing, "Expected the renderer to draw the normalized segment.");
		Assert.HasCount(1, drawing.Children);

		var geometryDrawing = (GeometryDrawing)drawing.Children[0];

		Assert.AreSame(renderer.ErrorPen, geometryDrawing.Pen);
		Assert.IsNotNull(geometryDrawing.Geometry);
		return geometryDrawing.Geometry.Bounds.Width;
	}

	private void AssertDrawsUnderline(TextEditor editor, TextDiagnosticSeverity severity, bool expectedSquiggle)
	{
		var renderer = new DiagnosticsRenderer(
			() => [new TextDiagnosticSegment(0, 40, severity)]);

		DrawingGroup? drawing = Render(renderer, editor.TextArea.TextView);

		Assert.IsNotNull(drawing, $"No drawing was produced for {severity}.");
		Assert.HasCount(1, drawing.Children);

		var geometryDrawing = (GeometryDrawing)drawing.Children[0];
		Pen expectedPen = severity switch
		{
			TextDiagnosticSeverity.Error => renderer.ErrorPen,
			TextDiagnosticSeverity.Warning => renderer.WarningPen,
			TextDiagnosticSeverity.Information => renderer.InformationPen,
			_ => renderer.HintPen
		};

		Assert.AreSame(expectedPen, geometryDrawing.Pen, $"Wrong pen used for {severity}.");

		AssertUnderlineGeometryShape(geometryDrawing, severity);

		Assert.IsNotNull(geometryDrawing.Geometry);
		Assert.IsGreaterThan(0.0, geometryDrawing.Geometry.Bounds.Width, $"Empty underline bounds for {severity}.");

		// A squiggle spans the wave height; a hint is a straight line with flat bounds.
		if (expectedSquiggle)
			Assert.IsGreaterThan(0.5, geometryDrawing.Geometry.Bounds.Height, $"Empty squiggle amplitude for {severity}.");
		else
			Assert.AreEqual(0.0, geometryDrawing.Geometry.Bounds.Height, $"Expected flat hint underline bounds for {severity}.");
	}

	protected static DiagnosticsRenderer CreateRenderer()
		=> new(() => []);
}
