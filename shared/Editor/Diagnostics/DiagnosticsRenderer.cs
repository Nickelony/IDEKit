#if AVALONIAEDIT
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Brush = Avalonia.Media.IBrush;
using Pen = Avalonia.Media.IPen;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
#endif
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Infrastructure;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Diagnostics;
#else
namespace Nickelony.IDEKit.AvalonEdit.Diagnostics;
#endif

/// <summary>
/// Renders diagnostic underlines in a text view.
/// </summary>
/// <remarks>
/// <para>
/// The segments provider and the pens are queried during rendering and are not monitored for changes.
/// Hosts must invalidate the text view when they change, for example with <see cref="TextView.Redraw()"/>;
/// <see cref="TextView.InvalidateLayer(KnownLayer)"/> does not invalidate only the named layer; it
/// re-measures the view instead.
/// </para>
/// <para>
/// The four pens are the theming seam for the underline colors; each starts as a non-normative
/// sample (see <see cref="SampleErrorPen"/> and the sibling sample properties) so a renderer is
/// visible without configuration, and hosts should assign theme-appropriate pens. Changing a pen
/// does not redraw the view by itself.
/// </para>
/// <para>
/// A provider that returns <see langword="null"/> is treated as producing no data. Providers must not
/// throw, because an exception from a provider surfaces inside the render pass.
/// </para>
/// <para>
/// Segments are clamped against the document of the text view that is being drawn, so the rendered
/// geometry always matches the drawn document. A view without a document, or with invalid visual
/// lines, draws nothing.
/// </para>
/// <para>
/// The renderer draws in <see cref="KnownLayer.Selection"/> by default, the layer the editor's own
/// current-line renderer uses, which is repainted when the view scrolls or its visual lines change.
/// Add the instance to the text view's background renderers; assign <see cref="Layer"/> before
/// attaching it when a different layer is required.
/// </para>
/// <para>
/// <see cref="Draw(TextView, DrawingContext)"/> runs on the text view's UI thread and keeps its
/// per-pass state there, so assign the layer and the pens from that thread as well.
/// </para>
/// </remarks>
public sealed class DiagnosticsRenderer : IBackgroundRenderer
{
	/// <summary>
	/// The width below which an underline rectangle is widened, so a rectangle that would render as a
	/// hairline (an end-of-file diagnostic, an empty line, or a thin glyph) stays visible.
	/// </summary>
	internal const double MinimumRenderableRectangleWidth = 2.0;

	private static readonly Brush s_errorBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(224, 220, 76, 60));
	private static readonly Brush s_warningBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(224, 226, 165, 44));
	private static readonly Brush s_informationBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(224, 88, 170, 255));
	private static readonly Brush s_hintBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(192, 166, 166, 166));

	private static readonly Pen s_sampleErrorPen = BrushHelpers.CreateFrozenPen(s_errorBrush, 1.4);
	private static readonly Pen s_sampleWarningPen = BrushHelpers.CreateFrozenPen(s_warningBrush, 1.4);
	private static readonly Pen s_sampleInformationPen = BrushHelpers.CreateFrozenPen(s_informationBrush, 1.4);
	private static readonly Pen s_sampleHintPen = BrushHelpers.CreateFrozenDashedPen(s_hintBrush, 1.5, [1.0, 3.0]);

	private Pen _errorPen = s_sampleErrorPen;
	private Pen _warningPen = s_sampleWarningPen;
	private Pen _informationPen = s_sampleInformationPen;
	private Pen _hintPen = s_sampleHintPen;

	// Reused across render passes: the lists are consumed synchronously while the pass runs, so no
	// per-pass allocation is needed for the batching. The segment is reused the same way; it is
	// mutable state of a single-threaded renderer (see the type remarks).
	private readonly List<Rect> _errorRects = [];
	private readonly List<Rect> _warningRects = [];
	private readonly List<Rect> _informationRects = [];
	private readonly List<Rect> _hintRects = [];
	private readonly TextSegment _segment = new();

	/// <summary>
	/// Gets the sample pen the renderer installs for error underlines as its non-normative default.
	/// </summary>
	/// <remarks>
	/// The sample exists so the renderer is visible without configuration. Assign
	/// <see cref="ErrorPen"/> for the host's own theme.
	/// </remarks>
	public static Pen SampleErrorPen => s_sampleErrorPen;

	/// <summary>
	/// Gets the sample pen the renderer installs for warning underlines as its non-normative default.
	/// </summary>
	/// <remarks>
	/// The sample exists so the renderer is visible without configuration. Assign
	/// <see cref="WarningPen"/> for the host's own theme.
	/// </remarks>
	public static Pen SampleWarningPen => s_sampleWarningPen;

	/// <summary>
	/// Gets the sample pen the renderer installs for information underlines as its non-normative default.
	/// </summary>
	/// <remarks>
	/// The sample exists so the renderer is visible without configuration. Assign
	/// <see cref="InformationPen"/> for the host's own theme.
	/// </remarks>
	public static Pen SampleInformationPen => s_sampleInformationPen;

	/// <summary>
	/// Gets the sample pen the renderer installs for hint underlines as its non-normative default.
	/// </summary>
	/// <remarks>
	/// The sample exists so the renderer is visible without configuration. Assign
	/// <see cref="HintPen"/> for the host's own theme.
	/// </remarks>
	public static Pen SampleHintPen => s_sampleHintPen;

	/// <summary>
	/// Gets or sets the pen used to draw error underlines.
	/// </summary>
	/// <remarks>
	/// The default is a sample pen; hosts should assign a theme-appropriate pen. Changing the pen does
	/// not redraw the view by itself.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The assigned pen is <see langword="null"/>.</exception>
	public Pen ErrorPen
	{
		get => _errorPen;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			_errorPen = value;
		}
	}

	/// <summary>
	/// Gets or sets the pen used to draw warning underlines.
	/// </summary>
	/// <remarks>
	/// The default is a sample pen; see <see cref="ErrorPen"/> for the theming guidance.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The assigned pen is <see langword="null"/>.</exception>
	public Pen WarningPen
	{
		get => _warningPen;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			_warningPen = value;
		}
	}

	/// <summary>
	/// Gets or sets the pen used to draw information underlines.
	/// </summary>
	/// <remarks>
	/// The default is a sample pen; see <see cref="ErrorPen"/> for the theming guidance.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The assigned pen is <see langword="null"/>.</exception>
	public Pen InformationPen
	{
		get => _informationPen;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			_informationPen = value;
		}
	}

	/// <summary>
	/// Gets or sets the pen used to draw hint underlines.
	/// </summary>
	/// <remarks>
	/// The default is a sample pen; see <see cref="ErrorPen"/> for the theming guidance.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The assigned pen is <see langword="null"/>.</exception>
	public Pen HintPen
	{
		get => _hintPen;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			_hintPen = value;
		}
	}

	private readonly Func<IReadOnlyList<TextDiagnosticSegment>?> _segmentsProvider;

	/// <summary>
	/// Initializes a new instance of the <see cref="DiagnosticsRenderer"/> class.
	/// </summary>
	/// <param name="segmentsProvider">
	/// Provides the diagnostic segments to render, or <see langword="null"/> when there are none; a
	/// <see langword="null"/> result draws nothing.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="segmentsProvider"/> is <see langword="null"/>.
	/// </exception>
	public DiagnosticsRenderer(Func<IReadOnlyList<TextDiagnosticSegment>?> segmentsProvider)
	{
		ArgumentNullException.ThrowIfNull(segmentsProvider);

		_segmentsProvider = segmentsProvider;
	}

	/// <summary>
	/// Gets or sets the layer the renderer draws in.
	/// </summary>
	/// <remarks>
	/// Defaults to <see cref="KnownLayer.Selection"/>, which is repainted when the text view scrolls or
	/// its visual lines change and exists for any text area. A bare <see cref="TextView"/> without a
	/// text area has no selection or caret layer, so such a view draws nothing until the layer is
	/// assigned to <see cref="KnownLayer.Background"/> or <see cref="KnownLayer.Text"/>. Assign the
	/// layer before adding the renderer to the view's background renderers.
	/// </remarks>
	public KnownLayer Layer { get; set; } = KnownLayer.Selection;

	/// <inheritdoc/>
	/// <remarks>
	/// <para>
	/// The provider supplies offset ranges; each range is clamped against the drawn document before the
	/// visibility test, so an out-of-document range paints the document's last character and an empty
	/// or reversed range collapses to a single character. Severities map to underline styles:
	/// <see cref="TextDiagnosticSeverity.Error"/>, <see cref="TextDiagnosticSeverity.Warning"/>, and
	/// <see cref="TextDiagnosticSeverity.Information"/> draw squiggly underlines, and
	/// <see cref="TextDiagnosticSeverity.Hint"/> draws a dashed straight underline;
	/// <see cref="TextDiagnosticSeverity.None"/> and values outside the defined members draw nothing.
	/// The severity-to-shape mapping is this renderer's fixed convention; a host that needs different
	/// underline shapes adds its own <see cref="IBackgroundRenderer"/>.
	/// </para>
	/// <para>
	/// A render pass draws nothing while the view has no document or its visual lines are invalid, and
	/// it queries the provider on every pass while the renderer's layer is drawn.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textView"/> or <paramref name="drawingContext"/> is <see langword="null"/>.
	/// </exception>
	public void Draw(TextView textView, DrawingContext drawingContext)
	{
		ArgumentNullException.ThrowIfNull(textView);
		ArgumentNullException.ThrowIfNull(drawingContext);

		// The renderer runs inside a render pass; a view that has not completed a layout pass
		// (its visual lines are invalid) or has no document cannot be drawn against.
		if (!textView.VisualLinesValid || textView.Document is null)
			return;

		IReadOnlyList<TextDiagnosticSegment>? segments = _segmentsProvider();

		if (segments is null || segments.Count == 0)
			return;

		TextDocument document = textView.Document;

		// Rectangles are collected per severity so each pen builds one geometry per render pass
		// instead of one geometry per segment; the fields are cleared instead of reallocated.
		_errorRects.Clear();
		_warningRects.Clear();
		_informationRects.Clear();
		_hintRects.Clear();

		foreach (TextDiagnosticSegment segment in segments)
		{
			List<Rect>? rects = segment.Severity switch
			{
				TextDiagnosticSeverity.Error => _errorRects,
				TextDiagnosticSeverity.Warning => _warningRects,
				TextDiagnosticSeverity.Information => _informationRects,
				TextDiagnosticSeverity.Hint => _hintRects,
				_ => null
			};

			if (rects is null)
				continue;

			// Normalization runs before the visibility test: an empty or reversed range becomes a
			// length-1 segment, and the test must use the normalized offsets because those are the
			// offsets the underline is drawn for.
			if (TextRange.Normalize(document.TextLength, segment.StartOffset, segment.EndOffset) is not TextRange normalizedRange)
				continue;

			int startOffset = normalizedRange.Offset;
			int endOffset = normalizedRange.EndOffset;

			// The visibility test runs before the segment is materialized: most segments of a large
			// diagnostic set are outside the visible range, and a discarded segment costs no allocation.
			if (!IntersectsVisibleRange(textView, startOffset, endOffset))
				continue;

			_segment.StartOffset = startOffset;
			_segment.EndOffset = endOffset;

			foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, _segment, false))
			{
				if (rect.Height <= 0.0)
					continue;

				// Rectangles narrower than the minimum (end-of-file diagnostics, empty lines, thin glyphs)
				// are widened instead of dropped so the underline stays visible.
				rects.Add(EnsureRenderableWidth(rect));
			}
		}

		DrawSquigglyUnderline(drawingContext, _errorRects, _errorPen);
		DrawSquigglyUnderline(drawingContext, _warningRects, _warningPen);
		DrawSquigglyUnderline(drawingContext, _informationRects, _informationPen);
		DrawStraightUnderline(drawingContext, _hintRects, _hintPen);
	}

	/// <summary>
	/// Determines whether the normalized segment can intersect the document range that is currently visible.
	/// </summary>
	/// <remarks>
	/// The caller guarantees that the text view's visual lines are valid.
	/// </remarks>
	private static bool IntersectsVisibleRange(TextView textView, int startOffset, int endOffset)
	{
		var visualLines = textView.VisualLines;

		// A valid visual-line state can still be empty (for example before layout completed), and the
		// render pass must draw nothing in that case instead of indexing an empty list.
		if (visualLines.Count == 0)
			return false;

		// Segment ends are exclusive, so a segment ending where the visible range starts cannot be visible.
		return endOffset > visualLines[0].FirstDocumentLine.Offset
			&& startOffset <= visualLines[^1].LastDocumentLine.EndOffset;
	}

	/// <summary>
	/// Widens a rectangle narrower than <see cref="MinimumRenderableRectangleWidth"/> (an end-of-file
	/// diagnostic, an empty line, or a thin glyph) so the underline stays visible; a rectangle that is
	/// already at least that wide is returned unchanged.
	/// </summary>
	/// <param name="rect">The rectangle measured for the diagnostic segment.</param>
	/// <returns>The rectangle to draw.</returns>
	internal static Rect EnsureRenderableWidth(Rect rect)
		=> rect.Width >= MinimumRenderableRectangleWidth
			? rect
			: new Rect(rect.X, rect.Y, MinimumRenderableRectangleWidth, rect.Height);

	/// <summary>
	/// Freezes a completed geometry so it can be shared and drawn without further change.
	/// </summary>
	/// <remarks>
	/// A geometry whose type is already immutable once its context is closed needs no freeze, so this
	/// is a no-op where the engine does not expose one.
	/// </remarks>
	/// <param name="geometry">The geometry to freeze.</param>
	private static void FreezeGeometry(StreamGeometry geometry)
	{
#if !AVALONIAEDIT
		geometry.Freeze();
#endif
	}

	/// <summary>
	/// Draws the squiggly underline for the supplied rectangles as one batched geometry.
	/// </summary>
	/// <param name="drawingContext">The drawing context of the render pass.</param>
	/// <param name="rects">The rectangles to underline; <see langword="null"/> or empty draws nothing.</param>
	/// <param name="pen">The pen that strokes the geometry.</param>
	private static void DrawSquigglyUnderline(DrawingContext drawingContext, List<Rect>? rects, Pen pen)
	{
		if (rects is null || rects.Count == 0)
			return;

		var geometry = new StreamGeometry();

		using (StreamGeometryContext context = geometry.Open())
		{
			foreach (Rect rect in rects)
				AppendSquiggleFigure(context, rect);
		}

		FreezeGeometry(geometry);
		drawingContext.DrawGeometry(null, pen, geometry);
	}

	/// <summary>
	/// Appends one zig-zag figure for a rectangle to the geometry under construction.
	/// </summary>
	/// <param name="context">The geometry context to append to.</param>
	/// <param name="rect">The line-box rectangle to underline.</param>
	private static void AppendSquiggleFigure(StreamGeometryContext context, Rect rect)
	{
		// A 1.6-DIP amplitude keeps the zig-zag legible under the 1.4-DIP error pen, and the wave is
		// offset so that neither stroke leaves the rectangle: the down stroke ends above the line box's
		// bottom edge instead of bleeding into the next line's leading.
		const double amplitude = 1.6;
		const double step = 4.0;
		const double penBleed = 0.7;

		double baseline = rect.Bottom - amplitude - penBleed;

		bool goingUp = true;

#if AVALONIAEDIT
		context.BeginFigure(new Point(rect.Left, baseline), false);

		for (double x = rect.Left; x < rect.Right; x += step)
		{
			double nextX = Math.Min(x + (step / 2.0), rect.Right);
			double y = baseline + (goingUp ? -amplitude : amplitude);
			context.LineTo(new Point(nextX, y), true);

			goingUp = !goingUp;

			nextX = Math.Min(x + step, rect.Right);
			context.LineTo(new Point(nextX, baseline), true);
		}

		context.EndFigure(false);
#else
		context.BeginFigure(new Point(rect.Left, baseline), false, false);

		for (double x = rect.Left; x < rect.Right; x += step)
		{
			double nextX = Math.Min(x + (step / 2.0), rect.Right);
			double y = baseline + (goingUp ? -amplitude : amplitude);
			context.LineTo(new Point(nextX, y), true, false);

			goingUp = !goingUp;

			nextX = Math.Min(x + step, rect.Right);
			context.LineTo(new Point(nextX, baseline), true, false);
		}
#endif
	}

	/// <summary>
	/// Draws the straight underline for the supplied rectangles as one batched geometry.
	/// </summary>
	/// <param name="drawingContext">The drawing context of the render pass.</param>
	/// <param name="rects">The rectangles to underline; <see langword="null"/> or empty draws nothing.</param>
	/// <param name="pen">The pen that strokes the geometry.</param>
	private static void DrawStraightUnderline(DrawingContext drawingContext, List<Rect>? rects, Pen pen)
	{
		if (rects is null || rects.Count == 0)
			return;

		// All rectangles are drawn as one geometry, matching the batched squiggly underlines.
		var geometry = new StreamGeometry();

		using (StreamGeometryContext context = geometry.Open())
		{
			foreach (Rect rect in rects)
			{
				double y = rect.Bottom - 1.0;

#if AVALONIAEDIT
				context.BeginFigure(new Point(rect.Left, y), false);
				context.LineTo(new Point(rect.Right, y), true);
				context.EndFigure(false);
#else
				context.BeginFigure(new Point(rect.Left, y), false, false);
				context.LineTo(new Point(rect.Right, y), true, false);
#endif
			}
		}

		FreezeGeometry(geometry);
		drawingContext.DrawGeometry(null, pen, geometry);
	}
}
