#if AVALONIAEDIT
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Diagnostics;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Pen = Avalonia.Media.IPen;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Diagnostics;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Pen = System.Windows.Media.Pen;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

/// <summary>The shared <see cref="DiagnosticsRenderer"/> scenarios that run identically across the editor bindings.</summary>
/// <remarks>
/// A binding supplies the engine-specific operations through the hooks below: running the renderer's draw pass into a captured drawing,
/// creating a frozen pen, and asserting the underline geometry's shape (one engine records a <c>StreamGeometry</c>, the other a platform geometry).
/// The default-pen scenario stays in each binding's own suite, because one engine asserts <c>IsFrozen</c> and the other the immutable
/// sample-pen identity.
/// </remarks>
public abstract partial class DiagnosticsRendererTestsBase
{
	/// <summary>Runs the renderer's draw pass into a captured drawing, so the recorded drawings can be inspected.</summary>
	/// <param name="renderer">The renderer to run.</param>
	/// <param name="textView">The text view to draw against.</param>
	/// <returns>The captured drawing, or <see langword="null"/> when nothing was drawn.</returns>
	protected abstract DrawingGroup? Render(DiagnosticsRenderer renderer, TextView textView);

	/// <summary>Creates a frozen pen of the given color.</summary>
	/// <param name="color">The pen color.</param>
	/// <returns>The created pen.</returns>
	protected abstract Pen CreateFrozenPen(Color color);

	/// <summary>Asserts that the drawn underline geometry has the shape the binding records for an underline.</summary>
	/// <param name="drawing">The geometry drawing produced by the renderer.</param>
	/// <param name="severity">The severity the underline was drawn for.</param>
	protected abstract void AssertUnderlineGeometryShape(GeometryDrawing drawing, DiagnosticSeverity severity);

	[TestMethod]
	public void Draw_WidensRectanglesNarrowerThanMinimumWidth()
	{
		var editor = new TextEditor { Document = new TextDocument("x"), FontSize = 1.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		// The premise: the rendered rectangle is narrower than the renderer's 2-DIP minimum.
		// Asserting it here keeps a font-metric change from silently turning this into a vacuous test.
		var segment = new TextSegment { StartOffset = 0, EndOffset = 1 };
		var rects = BackgroundGeometryBuilder.GetRectsForSegment(editor.TextArea.TextView, segment, false).ToList();

		Assert.IsNotEmpty(rects);
		Assert.IsTrue(rects.All(rect => rect.Width < 2.0), "The test font must produce rectangles narrower than the renderer's 2-DIP minimum.");

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(0, 1), DiagnosticSeverity.Error)
		);

		DrawingGroup? drawing = Render(renderer, editor.TextArea.TextView);

		// A rectangle narrower than the minimum (for example a zero-width end-of-file error) renders as a
		// widened rectangle instead of being dropped.
		Assert.IsNotNull(drawing);
		Assert.HasCount(1, drawing.Children);

		var geometryDrawing = (GeometryDrawing)drawing.Children[0];

		Assert.IsNotNull(geometryDrawing.Geometry);
		Assert.IsTrue(geometryDrawing.Geometry.Bounds.Width >= 1.99, "Expected the narrow rectangle to be widened to the renderer's minimum width.");
	}

	[TestMethod]
	public void EnsureRenderableWidth_RectangleNarrowerThanMinimum_IsWidenedToTheMinimum()
	{
		var rect = new Rect(x: 3.0, y: 4.0, width: DiagnosticsRenderer.MinimumRenderableRectangleWidth - 0.01, height: 5.0);

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
		var rect = new Rect(x: 1.0, y: 2.0, width: DiagnosticsRenderer.MinimumRenderableRectangleWidth, height: 3.0);

		Rect kept = DiagnosticsRenderer.EnsureRenderableWidth(rect);

		Assert.AreEqual(rect, kept);
	}

	[TestMethod]
	public void EnsureRenderableWidth_RectangleWiderThanTheMinimum_IsKeptUnchanged()
	{
		var rect = new Rect(x: 1.0, y: 2.0, width: DiagnosticsRenderer.MinimumRenderableRectangleWidth + 0.01, height: 3.0);

		Rect kept = DiagnosticsRenderer.EnsureRenderableWidth(rect);

		Assert.AreEqual(rect, kept);
	}

	[TestMethod]
	public void Draw_UsesThePenAndGeometryOfEachSeverity()
	{
		var editor = new TextEditor { Document = new TextDocument(new string('x', 40)), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		AssertDrawsUnderline(editor, DiagnosticSeverity.Error, expectedSquiggle: true);
		AssertDrawsUnderline(editor, DiagnosticSeverity.Warning, expectedSquiggle: true);
		AssertDrawsUnderline(editor, DiagnosticSeverity.Information, expectedSquiggle: true);
		AssertDrawsUnderline(editor, DiagnosticSeverity.Hint, expectedSquiggle: false);
	}

	[TestMethod]
	public void Draw_MultipleSegmentsWithMixedSeverities_BatchesOneGeometryPerSeverity()
	{
		var editor = new TextEditor { Document = new TextDocument(new string('x', 60)), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(0, 5), DiagnosticSeverity.Error),
			new DiagnosticSegment(TextRange.FromBounds(10, 15), DiagnosticSeverity.Error),
			new DiagnosticSegment(TextRange.FromBounds(20, 25), DiagnosticSeverity.Warning),
			new DiagnosticSegment(TextRange.FromBounds(30, 35), DiagnosticSeverity.Hint)
		);

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
		double singleErrorWidth = RenderGeometryWidth(editor, [new DiagnosticSegment(TextRange.FromBounds(0, 5), DiagnosticSeverity.Error)]);

		Assert.IsNotNull(errorDrawing.Geometry);
		Assert.IsGreaterThan(singleErrorWidth, errorDrawing.Geometry.Bounds.Width);
	}

	[TestMethod]
	public void Draw_EmptyRangeAtTopOfDocument_DrawsTheNormalizedSegment()
	{
		var editor = new TextEditor { Document = new TextDocument("abcdef"), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);
		Assert.AreEqual(0, editor.TextArea.TextView.VisualLines[0].FirstDocumentLine.Offset);

		// An empty range normalizes to a length-1 segment, and the top of the document is visible,
		// so the underline must be drawn even though the requested range is empty.
		double width = RenderGeometryWidth(editor, [new DiagnosticSegment(TextRange.FromBounds(0, 0), DiagnosticSeverity.Error)]);

		Assert.IsGreaterThan(0.0, width);
	}

	[TestMethod]
	public void Draw_EmptyRangeAtTheFirstVisibleOffset_DrawsTheNormalizedSegment()
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

		// The empty range ends where the visible range starts, so its end offset is not strictly
		// greater than the first visible offset; its normalized form is one character long and must
		// still be drawn.
		double width = RenderGeometryWidth(editor, [new DiagnosticSegment(new TextRange(firstVisibleOffset, 0), DiagnosticSeverity.Error)]);

		Assert.IsGreaterThan(0.0, width);
	}

	[TestMethod]
	public void Draw_AttachedToARealView_RendersTheUnderlineInTheLayer()
	{
		var editor = new TextEditor { Document = new TextDocument(new string('x', 40)), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(0, 40), DiagnosticSeverity.Error)
		);

		renderer.ErrorPen = CreateFrozenPen(Colors.Magenta);

		editor.TextArea.TextView.BackgroundRenderers.Add(renderer);

		var bitmap = TestBitmapRendering.PumpAndRender(editor.TextArea.TextView);

		// The renderer draws in the selection layer; rendering the view must draw its underline.
		Assert.IsTrue(
			TestBitmapRendering.TryGetColorColumnBounds(
				bitmap,
				stripWidth: int.MaxValue,
				target: Colors.Magenta,
				out int minColumn,
				out int maxColumn
			),

			"Expected the attached renderer to draw the error underline into the rendered view."
		);

		Assert.IsGreaterThan(1, maxColumn - minColumn);
	}

	[TestMethod]
	public void Draw_SkipsNoneSeveritySegments()
	{
		var editor = new TextEditor { Document = new TextDocument(new string('x', 40)), FontSize = 1.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(0, 40), DiagnosticSeverity.None)
		);

		Assert.IsNull(Render(renderer, editor.TextArea.TextView));
	}

	[TestMethod]
	public void Draw_SkipsUnrecognizedSeveritySegments()
	{
		var editor = new TextEditor { Document = new TextDocument(new string('x', 40)), FontSize = 1.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(0, 40), (DiagnosticSeverity)42)
		);

		// An unrecognized severity is treated like None and draws nothing.
		Assert.IsNull(Render(renderer, editor.TextArea.TextView));
	}

	[TestMethod]
	public void ClearSegments_AfterApply_DrawsNothingAndFindsNothing()
	{
		var editor = new TextEditor { Document = new TextDocument(new string('x', 40)), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(0, 40), DiagnosticSeverity.Error)
		);

		Assert.IsNotNull(Render(renderer, editor.TextArea.TextView));

		renderer.ClearSegments();

		Assert.IsNull(Render(renderer, editor.TextArea.TextView));
		Assert.IsFalse(renderer.TryFindSegmentAt(3, out int index));
		Assert.AreEqual(-1, index);
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
			"Expected line 350 to be outside the visible range."
		);

		// Line 350 is far below the visible range of the host window.
		int startOffset = document.GetLineByNumber(350).Offset;

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(startOffset, startOffset + 4), DiagnosticSeverity.Error)
		);

		Assert.IsNull(Render(renderer, editor.TextArea.TextView));
	}

	[TestMethod]
	public void Draw_CustomPen_DrawsTheUnderlineWithThatPen()
	{
		var editor = new TextEditor { Document = new TextDocument(new string('x', 40)), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(0, 40), DiagnosticSeverity.Error)
		);

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
	public void ApplySegments_OnAViewWithoutDocument_HoldsNothing()
	{
		// An unhosted text view has no document, so there is nothing to anchor the segments to.
		var textView = new TextView();
		DiagnosticsRenderer renderer = CreateRenderer(textView, new DiagnosticSegment(TextRange.FromBounds(0, 1), DiagnosticSeverity.Error));

		Assert.IsNull(Render(renderer, textView));
		Assert.IsFalse(renderer.TryFindSegmentAt(0, out _));
	}

	[TestMethod]
	public void ApplySegments_NullList_ThrowsArgumentNullException()
	{
		DiagnosticsRenderer renderer = CreateRenderer();

		Assert.ThrowsExactly<ArgumentNullException>(() => renderer.ApplySegments(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => new DiagnosticsRenderer(null!));
	}

	[TestMethod]
	public void Draw_CustomPens_DrawEachSeverityWithItsOwnPen()
	{
		var editor = new TextEditor { Document = new TextDocument(new string('x', 40)), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(0, 5), DiagnosticSeverity.Error),
			new DiagnosticSegment(TextRange.FromBounds(6, 10), DiagnosticSeverity.Warning),
			new DiagnosticSegment(TextRange.FromBounds(11, 15), DiagnosticSeverity.Information),
			new DiagnosticSegment(TextRange.FromBounds(16, 20), DiagnosticSeverity.Hint)
		);

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
			Height = 0.0,
		};

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.UpdateLayout();

		// The premise: the view has a valid visual-line state but no visible line to draw against.
		Assert.IsTrue(editor.TextArea.TextView.VisualLinesValid);
		Assert.IsEmpty(editor.TextArea.TextView.VisualLines);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(0, 5), DiagnosticSeverity.Error)
		);

		// The segment is applied, but the empty visible range filters it out.
		Assert.IsTrue(renderer.TryFindSegmentAt(0, out _));
		Assert.IsNull(Render(renderer, editor.TextArea.TextView));
	}

	[TestMethod]
	public void Draw_OnABareTextView_DrawsOnlyTheRendererOnTheBackgroundLayer()
	{
		// A bare TextView has no text area and therefore no selection layer, so a renderer left on the
		// default layer never draws; assigning the background layer before attaching is the documented
		// contract for a host that composes the view itself.
		var textView = new TextView { Document = new TextDocument(new string('x', 40)) };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(textView);

		Assert.IsNotEmpty(textView.VisualLines);

		DiagnosticsRenderer backgroundLayerRenderer = CreateRenderer(
			textView,
			new DiagnosticSegment(TextRange.FromBounds(0, 40), DiagnosticSeverity.Error)
		);

		backgroundLayerRenderer.ErrorPen = CreateFrozenPen(Colors.Magenta);
		backgroundLayerRenderer.Layer = KnownLayer.Background;

		DiagnosticsRenderer defaultLayerRenderer = CreateRenderer(
			textView,
			new DiagnosticSegment(TextRange.FromBounds(0, 40), DiagnosticSeverity.Error)
		);

		defaultLayerRenderer.ErrorPen = CreateFrozenPen(Colors.Blue);

		Assert.AreEqual(KnownLayer.Selection, defaultLayerRenderer.Layer, "The premise: the second renderer keeps the default layer.");

		textView.BackgroundRenderers.Add(backgroundLayerRenderer);
		textView.BackgroundRenderers.Add(defaultLayerRenderer);

		var bitmap = TestBitmapRendering.PumpAndRender(textView);

		Assert.IsTrue(
			TestBitmapRendering.TryGetColorColumnBounds(
				bitmap,
				stripWidth: int.MaxValue,
				target: Colors.Magenta,
				out int minColumn,
				out int maxColumn
			),

			"Expected the background-layer renderer to draw on a bare text view."
		);

		Assert.IsGreaterThan(1, maxColumn - minColumn);

		Assert.IsFalse(
			TestBitmapRendering.TryGetColorColumnBounds(bitmap, stripWidth: int.MaxValue, target: Colors.Blue, minColumn: out _, maxColumn: out _),
			"A renderer on the default selection layer must not draw on a bare text view."
		);
	}

	[TestMethod]
	public void ApplySegments_InsertionBeforeASegment_MovesItsUnderlineAndItsLookup()
	{
		var editor = new TextEditor { Document = new TextDocument("call error here"), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(5, 10), DiagnosticSeverity.Error)
		);

		Rect before = RenderGeometryBounds(renderer, editor);

		editor.Document.Insert(0, "xxxx");
		editor.TextArea.TextView.EnsureVisualLines();

		// The underline sits where a set applied to the edited text would put it, so it follows "error" to its new offset.
		Rect after = RenderGeometryBounds(renderer, editor);

		Rect expected = RenderGeometryBounds(
			CreateRenderer(editor.TextArea.TextView, new DiagnosticSegment(TextRange.FromBounds(9, 14), DiagnosticSeverity.Error)),
			editor
		);

		Assert.AreEqual(expected, after);
		Assert.IsGreaterThan(before.X, after.X);

		Assert.IsTrue(renderer.TryFindSegmentAt(9, out int index));
		Assert.AreEqual(0, index);
		Assert.IsFalse(renderer.TryFindSegmentAt(5, out _));
	}

	[TestMethod]
	public void ApplySegments_DeletionInsideASegment_ShrinksItsUnderline()
	{
		var editor = new TextEditor { Document = new TextDocument("call errorous here"), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(5, 13), DiagnosticSeverity.Error)
		);

		editor.Document.Remove(10, 3);
		editor.TextArea.TextView.EnsureVisualLines();

		Rect expected = RenderGeometryBounds(
			CreateRenderer(editor.TextArea.TextView, new DiagnosticSegment(TextRange.FromBounds(5, 10), DiagnosticSeverity.Error)),
			editor
		);

		Assert.AreEqual(expected, RenderGeometryBounds(renderer, editor));
		Assert.IsFalse(renderer.TryFindSegmentAt(10, out _));
	}

	[TestMethod]
	public void ApplySegments_DeletionOverTheWholeSegment_RemovesItsUnderlineUntilTheNextApply()
	{
		var editor = new TextEditor { Document = new TextDocument("call error here"), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(5, 10), DiagnosticSeverity.Error)
		);

		editor.Document.Remove(4, 7);
		editor.TextArea.TextView.EnsureVisualLines();

		Assert.IsNull(Render(renderer, editor.TextArea.TextView));
		Assert.IsFalse(renderer.TryFindSegmentAt(4, out _));

		renderer.ApplySegments([new DiagnosticSegment(TextRange.FromBounds(0, 4), DiagnosticSeverity.Error)]);

		Assert.IsNotNull(Render(renderer, editor.TextArea.TextView));
		Assert.IsTrue(renderer.TryFindSegmentAt(0, out _));
	}

	[TestMethod]
	public void ApplySegments_PositionUnderADeletion_KeepsItsUnderline()
	{
		var editor = new TextEditor { Document = new TextDocument("call error here"), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		DiagnosticsRenderer renderer = CreateRenderer(editor.TextArea.TextView, new DiagnosticSegment(new TextRange(7, 0), DiagnosticSeverity.Error));

		editor.Document.Remove(5, 5);
		editor.TextArea.TextView.EnsureVisualLines();

		// A position marks a place in the text rather than a span, so the deletion moves it to where the text was cut.
		Assert.IsNotNull(Render(renderer, editor.TextArea.TextView));
		Assert.IsTrue(renderer.TryFindSegmentAt(5, out _));
	}

	[TestMethod]
	public void ApplySegments_ThenADocumentReplacement_DrawsNothingUntilTheNextApply()
	{
		var editor = new TextEditor { Document = new TextDocument("call error here"), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(5, 10), DiagnosticSeverity.Error)
		);

		editor.Document = new TextDocument("call error here");
		editor.TextArea.TextView.EnsureVisualLines();

		Assert.IsNull(Render(renderer, editor.TextArea.TextView));
		Assert.IsFalse(renderer.TryFindSegmentAt(6, out _));

		renderer.ApplySegments([new DiagnosticSegment(TextRange.FromBounds(5, 10), DiagnosticSeverity.Error)]);

		Assert.IsNotNull(Render(renderer, editor.TextArea.TextView));
	}

	[TestMethod]
	public void TryFindSegmentAt_OverlappingSegments_AnswersTheFirstInAppliedOrder()
	{
		var editor = new TextEditor { Document = new TextDocument(new string('x', 20)), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(4, 6), DiagnosticSeverity.Warning),
			new DiagnosticSegment(TextRange.FromBounds(0, 10), DiagnosticSeverity.Error)
		);

		Assert.IsTrue(renderer.TryFindSegmentAt(5, out int inner));
		Assert.AreEqual(0, inner);

		Assert.IsTrue(renderer.TryFindSegmentAt(1, out int outer));
		Assert.AreEqual(1, outer);

		// The end of a span is exclusive.
		Assert.IsFalse(renderer.TryFindSegmentAt(10, out _));
	}

	[TestMethod]
	public void ApplySegments_InsertionAtASegmentsStart_MovesItWithoutGrowingIt()
	{
		var editor = new TextEditor { Document = new TextDocument("call error here"), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(5, 10), DiagnosticSeverity.Error)
		);

		editor.Document.Insert(5, "zz");

		Assert.IsTrue(renderer.TryGetSegmentRange(0, out TextRange range));
		Assert.AreEqual(new TextRange(7, 5), range);
		Assert.IsFalse(renderer.TryFindSegmentAt(5, out _));
	}

	[TestMethod]
	public void ApplySegments_InsertionAtASegmentsEnd_LeavesItAsItWas()
	{
		var editor = new TextEditor { Document = new TextDocument("call error here"), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(5, 10), DiagnosticSeverity.Error)
		);

		editor.Document.Insert(10, "zz");

		Assert.IsTrue(renderer.TryGetSegmentRange(0, out TextRange range));
		Assert.AreEqual(new TextRange(5, 5), range);
	}

	[TestMethod]
	public void ApplySegments_DeletionSpanningTwoSegments_ShrinksBoth()
	{
		var editor = new TextEditor { Document = new TextDocument("call error here"), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(0, 4), DiagnosticSeverity.Warning),
			new DiagnosticSegment(TextRange.FromBounds(5, 10), DiagnosticSeverity.Error)
		);

		// "ll er" goes, which leaves "caror here": "ca" of the first segment and "ror" of the second.
		editor.Document.Remove(2, 5);

		Assert.IsTrue(renderer.TryGetSegmentRange(0, out TextRange first));
		Assert.AreEqual(new TextRange(0, 2), first);
		Assert.IsTrue(renderer.TryGetSegmentRange(1, out TextRange second));
		Assert.AreEqual(new TextRange(2, 3), second);
	}

	[TestMethod]
	public void ApplySegments_UndoOfAnInsertion_MovesTheSegmentBack()
	{
		var editor = new TextEditor { Document = new TextDocument("call error here"), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(5, 10), DiagnosticSeverity.Error)
		);

		editor.Document.Insert(0, "xxxx");
		Assert.IsTrue(editor.Undo());

		Assert.IsTrue(renderer.TryGetSegmentRange(0, out TextRange range));
		Assert.AreEqual(new TextRange(5, 5), range);
	}

	[TestMethod]
	public void TryGetSegmentRange_AfterTheTextIsDeleted_OrOutsideTheList_AnswersNothing()
	{
		var editor = new TextEditor { Document = new TextDocument("call error here"), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(5, 10), DiagnosticSeverity.Error),
			new DiagnosticSegment(new TextRange(12, 0), DiagnosticSeverity.Hint)
		);

		editor.Document.Remove(4, 7);

		Assert.IsFalse(renderer.TryGetSegmentRange(0, out _));
		Assert.IsTrue(renderer.TryGetSegmentRange(1, out TextRange position), "A position survives the deletion.");
		Assert.AreEqual(new TextRange(5, 0), position);
		Assert.IsFalse(renderer.TryGetSegmentRange(2, out _));
		Assert.IsFalse(renderer.TryGetSegmentRange(-1, out _));
	}

	[TestMethod]
	public void TryFindSegmentAt_APositionAtTheDocumentEnd_IsFoundAtTheLastCharacter()
	{
		var editor = new TextEditor { Document = new TextDocument("abc"), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// The position past the last character is painted on that character, so a pointer over it finds the diagnostic.
		DiagnosticsRenderer renderer = CreateRenderer(editor.TextArea.TextView, new DiagnosticSegment(new TextRange(3, 0), DiagnosticSeverity.Error));

		Assert.IsTrue(renderer.TryFindSegmentAt(2, out int atLastCharacter));
		Assert.AreEqual(0, atLastCharacter);
		Assert.IsTrue(renderer.TryFindSegmentAt(3, out _));
		Assert.IsFalse(renderer.TryFindSegmentAt(1, out _));
	}

	private Rect RenderGeometryBounds(DiagnosticsRenderer renderer, TextEditor editor)
	{
		DrawingGroup? drawing = Render(renderer, editor.TextArea.TextView);

		Assert.IsNotNull(drawing, "Expected the renderer to draw an underline.");
		Assert.HasCount(1, drawing.Children);

		var geometryDrawing = (GeometryDrawing)drawing.Children[0];

		Assert.IsNotNull(geometryDrawing.Geometry);

		return geometryDrawing.Geometry.Bounds;
	}

	private double RenderGeometryWidth(TextEditor editor, IReadOnlyList<DiagnosticSegment> segments)
	{
		var renderer = new DiagnosticsRenderer(editor.TextArea.TextView);
		renderer.ApplySegments(segments);

		DrawingGroup? drawing = Render(renderer, editor.TextArea.TextView);

		Assert.IsNotNull(drawing, "Expected the renderer to draw the normalized segment.");
		Assert.HasCount(1, drawing.Children);

		var geometryDrawing = (GeometryDrawing)drawing.Children[0];

		Assert.AreSame(renderer.ErrorPen, geometryDrawing.Pen);
		Assert.IsNotNull(geometryDrawing.Geometry);

		return geometryDrawing.Geometry.Bounds.Width;
	}

	private void AssertDrawsUnderline(TextEditor editor, DiagnosticSeverity severity, bool expectedSquiggle)
	{
		DiagnosticsRenderer renderer = CreateRenderer(editor.TextArea.TextView, new DiagnosticSegment(TextRange.FromBounds(0, 40), severity));

		DrawingGroup? drawing = Render(renderer, editor.TextArea.TextView);

		Assert.IsNotNull(drawing, $"No drawing was produced for {severity}.");
		Assert.HasCount(1, drawing.Children);

		var geometryDrawing = (GeometryDrawing)drawing.Children[0];

		Pen expectedPen = severity switch
		{
			DiagnosticSeverity.Error => renderer.ErrorPen,
			DiagnosticSeverity.Warning => renderer.WarningPen,
			DiagnosticSeverity.Information => renderer.InformationPen,
			_ => renderer.HintPen,
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

	protected static DiagnosticsRenderer CreateRenderer() => new(new TextView());

	private static DiagnosticsRenderer CreateRenderer(TextView textView, params DiagnosticSegment[] segments)
	{
		var renderer = new DiagnosticsRenderer(textView);
		renderer.ApplySegments(segments);
		return renderer;
	}
}
