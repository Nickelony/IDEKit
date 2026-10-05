using System.Collections.ObjectModel;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
#if AVALONIAEDIT
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Media;
using Brush = Avalonia.Media.IBrush;
using Pen = Avalonia.Media.IPen;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Media;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Diagnostics;

#else

namespace Nickelony.IDEKit.AvalonEdit.Diagnostics;

#endif

/// <summary>Renders diagnostic underlines in a text view.</summary>
/// <remarks>
/// <see cref="ApplySegments"/> anchors each segment to the document the view shows, so an underline follows the edits made after it until the next
/// <see cref="ApplySegments"/> replaces the set: an insertion before a segment moves it, and a deletion over it shrinks it, or removes its underline
/// when the deletion covers it whole. A document replaced in the view draws nothing until the next <see cref="ApplySegments"/>.
/// Each pen starts as a sample so the renderer is visible without configuration. Assign pens that match the host's theme, because the sample
/// colors are fixed, and invalidate the view after a pen changes. The renderer draws in <see cref="KnownLayer.Selection"/> by default,
/// and <see cref="Layer"/> describes when to change it. The renderer is single-threaded, so call its members from the thread that owns the view.
/// </remarks>
public sealed class DiagnosticsRenderer : IBackgroundRenderer
{
	/// <summary>The width below which an underline rectangle is widened, so a rectangle that would render as a hairline stays visible.</summary>
	internal const double MinimumRenderableRectangleWidth = 2.0;

	private static readonly Brush s_errorBrush = BrushAndPenFactory.CreateBrush(Color.FromArgb(a: 224, r: 220, g: 76, b: 60));
	private static readonly Brush s_warningBrush = BrushAndPenFactory.CreateBrush(Color.FromArgb(a: 224, r: 226, g: 165, b: 44));
	private static readonly Brush s_informationBrush = BrushAndPenFactory.CreateBrush(Color.FromArgb(a: 224, r: 88, g: 170, b: 255));
	private static readonly Brush s_hintBrush = BrushAndPenFactory.CreateBrush(Color.FromArgb(a: 192, r: 166, g: 166, b: 166));

	private Pen _errorPen = SampleErrorPen;
	private Pen _warningPen = SampleWarningPen;
	private Pen _informationPen = SampleInformationPen;
	private Pen _hintPen = SampleHintPen;

	// Reused across geometry builds: the lists are consumed synchronously while a build runs, so a build
	// allocates nothing for the batching. The segment is reused the same way; it is
	// mutable state of a single-threaded renderer (see the type remarks).
	private readonly List<Rect> _errorRects = [];
	private readonly List<Rect> _warningRects = [];
	private readonly List<Rect> _informationRects = [];
	private readonly List<Rect> _hintRects = [];

	private readonly TextSegment _segment = new();
	private readonly UnderlineFigureBuilder _figureBuilder = new();

	// The geometry of the last build, one batched geometry per severity (null when no rectangle of the severity was visible), and the
	// key of the view and segments state it was built for. A pass whose key matches draws these without collecting rectangles.
	private UnderlineCacheKey? _geometryKey;

	private StreamGeometry? _errorGeometry;
	private StreamGeometry? _warningGeometry;
	private StreamGeometry? _informationGeometry;
	private StreamGeometry? _hintGeometry;

	// Advances whenever the applied set changes or is cleared, so the segment set and its severities are a component of the key.
	private int _segmentGeneration;

	private readonly TextView _textView;

	// The applied segments, anchored to the document they were applied to; null stands for no applied set.
	private TextSegmentCollection<UnderlineSegment>? _segments;
	private UnderlineSegment[] _segmentsByIndex = [];
	private TextDocument? _segmentsDocument;

	/// <summary>The sample pen the renderer installs by default for error underlines.</summary>
	/// <remarks>
	/// Assign <see cref="ErrorPen"/> for the host's own theme.
	/// </remarks>
	public static Pen SampleErrorPen { get; } = BrushAndPenFactory.CreatePen(s_errorBrush, 1.4);

	/// <summary>The sample pen the renderer installs by default for warning underlines.</summary>
	/// <remarks>
	/// Assign <see cref="WarningPen"/> for the host's own theme.
	/// </remarks>
	public static Pen SampleWarningPen { get; } = BrushAndPenFactory.CreatePen(s_warningBrush, 1.4);

	/// <summary>The sample pen the renderer installs by default for information underlines.</summary>
	/// <remarks>
	/// Assign <see cref="InformationPen"/> for the host's own theme.
	/// </remarks>
	public static Pen SampleInformationPen { get; } = BrushAndPenFactory.CreatePen(s_informationBrush, 1.4);

	/// <summary>The sample pen the renderer installs by default for hint underlines.</summary>
	/// <remarks>
	/// Assign <see cref="HintPen"/> for the host's own theme.
	/// </remarks>
	public static Pen SampleHintPen { get; } = BrushAndPenFactory.CreateDashedPen(s_hintBrush, 1.5, [1.0, 3.0]);

	/// <summary>The pen that draws error underlines.</summary>
	/// <exception cref="ArgumentNullException">The assigned pen is <see langword="null"/>.</exception>
	/// <remarks>
	/// The default is a sample pen; hosts should assign a theme-appropriate pen. Changing the pen does not redraw the view by itself.
	/// </remarks>
	public Pen ErrorPen
	{
		get => _errorPen;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			_errorPen = value;
		}
	}

	/// <summary>The pen that draws warning underlines.</summary>
	/// <exception cref="ArgumentNullException">The assigned pen is <see langword="null"/>.</exception>
	/// <remarks>
	/// The default is a sample pen; see <see cref="ErrorPen"/> for the theming guidance.
	/// </remarks>
	public Pen WarningPen
	{
		get => _warningPen;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			_warningPen = value;
		}
	}

	/// <summary>The pen that draws information underlines.</summary>
	/// <exception cref="ArgumentNullException">The assigned pen is <see langword="null"/>.</exception>
	/// <remarks>
	/// The default is a sample pen; see <see cref="ErrorPen"/> for the theming guidance.
	/// </remarks>
	public Pen InformationPen
	{
		get => _informationPen;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			_informationPen = value;
		}
	}

	/// <summary>The pen that draws hint underlines.</summary>
	/// <exception cref="ArgumentNullException">The assigned pen is <see langword="null"/>.</exception>
	/// <remarks>
	/// The default is a sample pen; see <see cref="ErrorPen"/> for the theming guidance.
	/// </remarks>
	public Pen HintPen
	{
		get => _hintPen;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			_hintPen = value;
		}
	}

	/// <param name="textView">The text view whose document the segments anchor to, and whose layer the renderer redraws when they change.</param>
	/// <exception cref="ArgumentNullException"><paramref name="textView"/> is <see langword="null"/>.</exception>
	public DiagnosticsRenderer(TextView textView)
	{
		ArgumentNullException.ThrowIfNull(textView);
		_textView = textView;
	}

	/// <summary>The layer the renderer draws in.</summary>
	/// <remarks>
	/// Defaults to <see cref="KnownLayer.Selection"/>; assign a different layer before attaching when the view has no text area.
	/// </remarks>
	public KnownLayer Layer { get; set; } = KnownLayer.Selection;

	/// <summary>The number of times the underline geometry was built from the visible segments.</summary>
	internal int GeometryBuildCount { get; private set; }

	/// <summary>Replaces the underlined segments with a new set, anchored to the document the view shows now, and redraws the layer.</summary>
	/// <param name="segments">The segments to underline; the renderer copies them, and an empty list underlines nothing.</param>
	/// <exception cref="ArgumentNullException"><paramref name="segments"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns the text view.</exception>
	/// <remarks>
	/// Each range is clamped to the document. A range that is empty after the clamp marks a position, which underlines one character
	/// and survives every edit, while a range that a deletion empties underlines nothing, so replacing the whole text empties every span.
	/// The offsets are read against the document as it is when the call runs, so a set computed for older text underlines at its offsets
	/// in the current text until the next set. A view without a document holds no segments.
	/// </remarks>
	public void ApplySegments(IReadOnlyList<DiagnosticSegment> segments)
	{
		ArgumentNullException.ThrowIfNull(segments);
		_textView.Dispatcher.VerifyAccess();

		if (_textView.Document is not TextDocument document)
		{
			ClearSegments();
			return;
		}

		// The set changes below, so a geometry built for the earlier one must not match any key from here on, even if the call throws part-way.
		_segmentGeneration++;

		TextSegmentCollection<UnderlineSegment> anchored =
			_segments is not null && ReferenceEquals(_segmentsDocument, document) ? _segments : new TextSegmentCollection<UnderlineSegment>(document);

		anchored.Clear();

		int textLength = document.TextLength;
		var byIndex = new UnderlineSegment[segments.Count];

		for (int index = 0; index < segments.Count; index++)
		{
			DiagnosticSegment segment = segments[index];

			int startOffset = Math.Min(segment.Range.Offset, textLength);
			int endOffset = Math.Min(segment.Range.EndOffset, textLength);

			byIndex[index] = new UnderlineSegment(index, segment.Severity, marksPosition: endOffset == startOffset)
			{
				StartOffset = startOffset,
				EndOffset = endOffset,
			};

			anchored.Add(byIndex[index]);
		}

		_segments = anchored;
		_segmentsByIndex = byIndex;
		_segmentsDocument = document;
		_textView.InvalidateLayer(Layer);
	}

	/// <summary>Removes every underlined segment and redraws the layer.</summary>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns the text view.</exception>
	public void ClearSegments()
	{
		_textView.Dispatcher.VerifyAccess();

		if (_segments is null)
			return;

		_segmentGeneration++;
		_segments = null;
		_segmentsByIndex = [];
		_segmentsDocument = null;

		// Nothing draws until the next apply, so the geometry of the cleared set is released.
		_geometryKey = null;
		_errorGeometry = null;
		_warningGeometry = null;
		_informationGeometry = null;
		_hintGeometry = null;

		_textView.InvalidateLayer(Layer);
	}

	/// <summary>Returns where an applied segment sits in the document as it is now.</summary>
	/// <param name="index">The position of the segment in the list that <see cref="ApplySegments"/> took.</param>
	/// <param name="range">Receives the anchored range of the segment; an empty range at its offset for a position.</param>
	/// <returns>
	/// <see langword="true"/> when the segment still has text to underline, or marks a position; <see langword="false"/> when a deletion
	/// removed its text, the index is outside the applied list, or the view shows another document.
	/// </returns>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns the text view.</exception>
	public bool TryGetSegmentRange(int index, out TextRange range)
	{
		_textView.Dispatcher.VerifyAccess();

		range = default;

		if (_segments is null || !ReferenceEquals(_textView.Document, _segmentsDocument) || index < 0 || index >= _segmentsByIndex.Length)
			return false;

		UnderlineSegment segment = _segmentsByIndex[index];

		if (segment.Length == 0 && !segment.MarksPosition)
			return false;

		range = new TextRange(segment.StartOffset, segment.Length);
		return true;
	}

	/// <summary>Finds the applied segment that covers an offset of the document as it is now.</summary>
	/// <param name="offset">The zero-based offset in the current document.</param>
	/// <param name="index">
	/// Receives the position of the segment in the list that <see cref="ApplySegments"/> took, or <c>-1</c> when no segment covers the offset.
	/// </param>
	/// <returns><see langword="true"/> when a segment covers the offset; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns the text view.</exception>
	/// <remarks>
	/// A segment covers the offsets from its anchored start up to its anchored end, and a position covers its own offset,
	/// as <c>Diagnostic.ContainsOffset</c> reads a range; a position at the end of the document also covers the last character, where it is painted.
	/// Where several segments cover the offset, the first one in the applied list answers.
	/// </remarks>
	public bool TryFindSegmentAt(int offset, out int index)
	{
		_textView.Dispatcher.VerifyAccess();

		index = -1;

		if (_segments is null || _textView.Document is not TextDocument document || !ReferenceEquals(document, _segmentsDocument))
			return false;

		foreach (UnderlineSegment segment in _segments.FindSegmentsContaining(offset))
		{
			if (segment.Covers(offset) && (index < 0 || segment.Index < index))
				index = segment.Index;
		}

		// A position at the end of the document has no character of its own, so it is painted on the last one and found there too.
		int textLength = document.TextLength;

		if (textLength > 0 && offset == textLength - 1)
		{
			foreach (UnderlineSegment segment in _segments.FindSegmentsContaining(textLength))
			{
				if (segment.MarksPosition && segment.StartOffset == textLength && (index < 0 || segment.Index < index))
					index = segment.Index;
			}
		}

		return index >= 0;
	}

	/// <inheritdoc/>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textView"/> or <paramref name="drawingContext"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// A position at the end of the document paints its last character. Error, Warning,
	/// and Information draw a zig-zag underline, Hint draws a straight one, and None draws nothing; a host that needs different underline
	/// shapes adds its own <see cref="IBackgroundRenderer"/>. A pass reuses the previous underline geometry until the view lays out again or scrolls,
	/// the document changes, or the applied segments change; a pen assigned between passes applies to the next pass.
	/// </remarks>
	public void Draw(TextView textView, DrawingContext drawingContext)
	{
		ArgumentNullException.ThrowIfNull(textView);
		ArgumentNullException.ThrowIfNull(drawingContext);

		// The renderer runs inside a render pass; a view that has not completed a layout pass
		// (its visual lines are invalid) or has no document cannot be drawn against.
		if (!textView.VisualLinesValid || textView.Document is null)
			return;

		// Anchors of another document describe text this view does not show.
		if (_segments is null || _segments.Count == 0 || !ReferenceEquals(textView.Document, _segmentsDocument))
			return;

		// The visual-line list is captured once, right after the validity check, and shared by every
		// visibility test below instead of being re-read through the view per segment.
		ReadOnlyCollection<VisualLine> visualLines = textView.VisualLines;

		// A valid visual-line state can still be empty (for example before layout completed), and the
		// render pass must draw nothing in that case instead of indexing an empty list.
		if (visualLines.Count == 0)
			return;

		TextDocument document = textView.Document;
		Vector scrollOffset = textView.ScrollOffset;

		// Invariant: the rectangles that the view reports for the applied segments are a function of exactly these inputs, so a geometry
		// built for a key that matches is the geometry this pass would build. The view replaces its visual-line collection whenever it
		// lays out again (text, font, wrapping, size, and flow changes all do), but it moves the scroll offset before that, so the
		// offset is part of the key. A document change moves the anchored segments, and a new applied set advances the generation.
		// The pens are not inputs: the geometry holds no pen, and every pass strokes it with the pen the renderer holds at that time.
		var key = new UnderlineCacheKey(
			visualLines,
			scrollOffsetX: scrollOffset.X,
			scrollOffsetY: scrollOffset.Y,
			documentVersion: document.Version,
			segmentGeneration: _segmentGeneration
		);

		if (_geometryKey is not UnderlineCacheKey builtKey || !builtKey.Matches(key))
		{
			RebuildGeometries(textView, document, visualLines, segments: _segments);
			_geometryKey = key;
		}

		DrawUnderline(drawingContext, _errorGeometry, _errorPen);
		DrawUnderline(drawingContext, _warningGeometry, _warningPen);
		DrawUnderline(drawingContext, _informationGeometry, _informationPen);
		DrawUnderline(drawingContext, _hintGeometry, _hintPen);
	}

	/// <summary>Collects the visible rectangles of the applied segments per severity and builds one batched geometry for each severity.</summary>
	/// <param name="textView">The text view the rectangles are measured in.</param>
	/// <param name="document">The document the view shows.</param>
	/// <param name="visualLines">The visual lines of the view, which bound the visible offset range.</param>
	/// <param name="segments">The applied segments of the document.</param>
	private void RebuildGeometries(
		TextView textView,
		TextDocument document,
		ReadOnlyCollection<VisualLine> visualLines,
		TextSegmentCollection<UnderlineSegment> segments
	)
	{
		int visibleStartOffset = visualLines[0].FirstDocumentLine.Offset;
		int visibleEndOffset = visualLines[^1].LastDocumentLine.EndOffset;

		// Rectangles are collected per severity so each pen strokes one geometry per severity
		// instead of one geometry per segment; the fields are cleared instead of reallocated.
		_errorRects.Clear();
		_warningRects.Clear();
		_informationRects.Clear();
		_hintRects.Clear();

		// The collection answers the segments that touch the visible range, so the segments of the lines off screen cost nothing.
		foreach (UnderlineSegment segment in segments.FindOverlappingSegments(visibleStartOffset, visibleEndOffset - visibleStartOffset))
		{
			List<Rect>? rects = segment.Severity switch
			{
				DiagnosticSeverity.Error => _errorRects,
				DiagnosticSeverity.Warning => _warningRects,
				DiagnosticSeverity.Information => _informationRects,
				DiagnosticSeverity.Hint => _hintRects,
				_ => null,
			};

			// A deletion that covered the whole segment left no text to underline.
			if (rects is null || (segment.Length == 0 && !segment.MarksPosition))
				continue;

			// Normalization runs before the visibility test: a position becomes a length-1
			// segment, and the test must use the normalized offsets because those are the
			// offsets the underline is drawn for.
			if (TextRange.Normalize(new TextRange(segment.StartOffset, segment.Length), document.TextLength) is not TextRange normalizedRange)
				continue;

			int startOffset = normalizedRange.Offset;
			int endOffset = normalizedRange.EndOffset;

			if (!IntersectsVisibleRange(visibleStartOffset, visibleEndOffset, startOffset, endOffset))
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

		// The geometries are assigned together after every build step succeeded, so a throwing rectangle query leaves the previous build whole.
		StreamGeometry? errorGeometry = CreateGeometry(_errorRects, _figureBuilder.BuildSquiggle);
		StreamGeometry? warningGeometry = CreateGeometry(_warningRects, _figureBuilder.BuildSquiggle);
		StreamGeometry? informationGeometry = CreateGeometry(_informationRects, _figureBuilder.BuildSquiggle);
		StreamGeometry? hintGeometry = CreateGeometry(_hintRects, _figureBuilder.BuildStraight);

		_errorGeometry = errorGeometry;
		_warningGeometry = warningGeometry;
		_informationGeometry = informationGeometry;
		_hintGeometry = hintGeometry;

		GeometryBuildCount++;
	}

	/// <summary>Determines whether the normalized segment can intersect the document range that is currently visible.</summary>
	private static bool IntersectsVisibleRange(int visibleStartOffset, int visibleEndOffset, int startOffset, int endOffset)
	{
		// Segment ends are exclusive, so a segment ending where the visible range starts cannot be visible.
		// The upper bound is inclusive: a segment that begins on the last visible line is still visible.
		return endOffset > visibleStartOffset && startOffset <= visibleEndOffset;
	}

	/// <summary>
	/// Widens a rectangle narrower than <see cref="MinimumRenderableRectangleWidth"/> (an end-of-file diagnostic, an empty line, or a thin glyph)
	/// so the underline stays visible; a rectangle that is already at least that wide is returned unchanged.
	/// </summary>
	/// <param name="rect">The rectangle measured for the diagnostic segment.</param>
	/// <returns>The rectangle to draw.</returns>
	internal static Rect EnsureRenderableWidth(Rect rect)
	{
		return rect.Width >= MinimumRenderableRectangleWidth
			? rect
			: new Rect(x: rect.X, y: rect.Y, width: MinimumRenderableRectangleWidth, height: rect.Height);
	}

	/// <summary>Writes one figure per rectangle into one geometry that stays immutable, so every later pass can stroke the same instance.</summary>
	/// <param name="rects">The rectangles to underline.</param>
	/// <param name="buildFigure">Builds the open polyline of one rectangle.</param>
	/// <returns>The finished geometry, or <see langword="null"/> when there is no rectangle to underline.</returns>
	private static StreamGeometry? CreateGeometry(List<Rect> rects, Func<Rect, IReadOnlyList<Point>> buildFigure)
	{
		if (rects.Count == 0)
			return null;

		var geometry = new StreamGeometry();

		using (StreamGeometryContext context = geometry.Open())
		{
			foreach (Rect rect in rects)
			{
				IReadOnlyList<Point> figure = buildFigure(rect);

				DiagnosticsGeometrySeam.BeginFigure(context, figure[0]);

				for (int index = 1; index < figure.Count; index++)
					DiagnosticsGeometrySeam.LineTo(context, figure[index]);

				DiagnosticsGeometrySeam.EndFigure(context);
			}
		}

		DiagnosticsGeometrySeam.Freeze(geometry);
		return geometry;
	}

	/// <summary>Strokes a batched underline geometry with a pen.</summary>
	/// <param name="drawingContext">The drawing context of the render pass.</param>
	/// <param name="geometry">The geometry to stroke; <see langword="null"/> draws nothing.</param>
	/// <param name="pen">The pen that strokes the geometry.</param>
	private static void DrawUnderline(DrawingContext drawingContext, StreamGeometry? geometry, Pen pen)
	{
		if (geometry is not null)
			drawingContext.DrawGeometry(null, pen, geometry);
	}

	/// <summary>One applied segment, whose offsets the collection moves with the edits of its document.</summary>
	private sealed class UnderlineSegment(int index, DiagnosticSeverity severity, bool marksPosition) : TextSegment
	{
		/// <summary>The position of the segment in the list that <see cref="ApplySegments"/> took.</summary>
		internal int Index { get; } = index;

		internal DiagnosticSeverity Severity { get; } = severity;

		/// <summary>Whether the applied range was empty, so the segment marks a position that no deletion removes.</summary>
		internal bool MarksPosition { get; } = marksPosition;

		internal bool Covers(int offset) => MarksPosition ? offset == StartOffset : offset >= StartOffset && offset < EndOffset;
	}
}
