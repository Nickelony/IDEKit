namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Provides a precomputed table of line start offsets and lengths for document content.
/// LF, CRLF, and lone CR are treated as line terminators.
/// </summary>
/// <remarks>
/// Line indexes are zero-based. Unlike <see cref="StringTextSnapshot"/>, which throws for
/// out-of-range line numbers and offsets, this map clamps them, so callers that must tolerate stale
/// host coordinates can use it without pre-validating them.
/// </remarks>
public sealed class TextLineMap
{
	private readonly string _text;
	private readonly int[] _lineStartOffsets;
	private readonly int[] _lineLengths;

	private TextLineMap(string text, int[] lineStartOffsets, int[] lineLengths)
	{
		_text = text;
		_lineStartOffsets = lineStartOffsets;
		_lineLengths = lineLengths;
	}

	/// <summary>
	/// Gets the number of logical lines in the document.
	/// </summary>
	public int LineCount => _lineLengths.Length;

	/// <summary>
	/// Gets the total document length in UTF-16 code units.
	/// </summary>
	public int TextLength => _text.Length;

	/// <summary>
	/// Gets the length of the specified zero-based line.
	/// </summary>
	/// <param name="lineIndex">The zero-based line index. Values outside the line range are clamped.</param>
	/// <returns>The line length in UTF-16 code units.</returns>
	public int GetLineLength(int lineIndex) => _lineLengths[ClampLineIndex(lineIndex)];

	/// <summary>
	/// Gets the absolute document offset where the specified zero-based line starts.
	/// </summary>
	/// <param name="lineIndex">The zero-based line index. Values outside the line range are clamped.</param>
	/// <returns>The absolute document offset.</returns>
	public int GetLineStartOffset(int lineIndex) => _lineStartOffsets[ClampLineIndex(lineIndex)];

	/// <summary>
	/// Returns the document offset for the supplied zero-based line and character indices.
	/// </summary>
	/// <param name="lineIndex">The zero-based line index. Values outside the line range are clamped.</param>
	/// <param name="character">The zero-based character index within the line. Negative values clamp to 0; values beyond the line length clamp to the line length.</param>
	/// <returns>The absolute document offset.</returns>
	public int GetOffset(int lineIndex, int character)
	{
		int safeLine = ClampLineIndex(lineIndex);
		int safeCharacter = Math.Clamp(character, 0, _lineLengths[safeLine]);
		return _lineStartOffsets[safeLine] + safeCharacter;
	}

	/// <summary>
	/// Returns the document offset for the supplied zero-based position.
	/// </summary>
	/// <param name="position">The zero-based line and character position. Values outside the document are clamped.</param>
	/// <returns>The absolute document offset.</returns>
	public int GetOffset(TextPosition position)
		=> GetOffset(position.Line, position.Character);

	/// <summary>
	/// Converts a position range to the corresponding offset range.
	/// </summary>
	/// <remarks>
	/// Both endpoints are converted like <see cref="GetOffset(TextPosition)"/>, so values outside the
	/// document are clamped. An empty range (both endpoints mapping to the same offset) succeeds with
	/// a zero-length range, because the conversion represents it exactly. The conversion fails when
	/// the mapped end precedes the mapped start (a reversed range), because <see cref="TextRange"/>
	/// cannot represent an inverted range. <see cref="Editing.TextRangeOffsetResolver.TryResolveOffsets(TextLineMap, TextPositionRange, Func{char, bool}, out TextRange)"/> is
	/// the caller-facing resolution helper: it builds its representable path on this method and falls
	/// back to an anchor exactly when this conversion fails or yields a zero-length range.
	/// </remarks>
	/// <param name="range">The position range to convert.</param>
	/// <param name="offsetRange">Receives the offset range when the conversion succeeds.</param>
	/// <returns><see langword="true"/> when the range maps to a representable offset range.</returns>
	public bool TryGetOffsets(TextPositionRange range, out TextRange offsetRange)
	{
		int start = GetOffset(range.Start);
		int end = GetOffset(range.End);

		if (end < start)
		{
			offsetRange = default;
			return false;
		}

		offsetRange = new(start, end - start);
		return true;
	}

	/// <summary>
	/// Gets the text of the specified zero-based line as a memory slice without its trailing line
	/// terminator.
	/// </summary>
	/// <remarks>
	/// The slice references this map's source text; it stays valid only while that text is unchanged.
	/// It is a view over the backing string rather than a copy, so a caller that needs an allocation-free
	/// character source (for example a boundary walk) can take the memory instead of materializing the
	/// line as a string.
	/// </remarks>
	/// <param name="lineIndex">The zero-based line index. Values outside the line range are clamped.</param>
	/// <returns>The line text as a memory slice over the source text.</returns>
	internal ReadOnlyMemory<char> GetLineMemory(int lineIndex)
	{
		int safeLine = ClampLineIndex(lineIndex);
		return _text.AsMemory(_lineStartOffsets[safeLine], _lineLengths[safeLine]);
	}

	/// <summary>
	/// Gets the text of the specified zero-based line as a span without its trailing line terminator.
	/// </summary>
	/// <remarks>
	/// The span references this map's source text; it stays valid only while that text is unchanged.
	/// </remarks>
	/// <param name="lineIndex">The zero-based line index. Values outside the line range are clamped.</param>
	/// <returns>The line text as a span over the source text.</returns>
	internal ReadOnlySpan<char> GetLineSpan(int lineIndex) => GetLineMemory(lineIndex).Span;

	/// <summary>
	/// Gets the text of the specified zero-based line without its trailing line terminator.
	/// </summary>
	/// <param name="lineIndex">The zero-based line index. Values outside the line range are clamped.</param>
	/// <returns>The line text.</returns>
	public string GetLineText(int lineIndex)
	{
		int safeLine = ClampLineIndex(lineIndex);
		return _text.Substring(_lineStartOffsets[safeLine], _lineLengths[safeLine]);
	}

	/// <summary>
	/// Converts an offset into a zero-based line and character position.
	/// </summary>
	/// <remarks>
	/// The character index is measured from the line start and clamped to the line length, so the
	/// result is always a position a host or protocol can accept: an offset inside a line terminator
	/// maps to the end of the preceding line. The conversion is lossless for every offset outside a
	/// terminator; inside a CRLF terminator both characters map to the line end, so
	/// <see cref="GetOffset(int, int)"/> cannot reproduce the exact terminator offset.
	/// </remarks>
	/// <param name="offset">The zero-based UTF-16 document offset. Values outside the document are clamped.</param>
	/// <returns>The containing position.</returns>
	public TextPosition GetPosition(int offset)
	{
		int safeOffset = Math.Clamp(offset, 0, TextLength);
		int lineIndex = LineIndexSearch.FindLineIndex(_lineStartOffsets, safeOffset);
		int character = Math.Min(safeOffset - _lineStartOffsets[lineIndex], _lineLengths[lineIndex]);

		return new(lineIndex, character);
	}

	private int ClampLineIndex(int lineIndex) => Math.Clamp(lineIndex, 0, _lineLengths.Length - 1);

	/// <summary>
	/// Builds a line map from the supplied content.
	/// </summary>
	/// <param name="text">The document text to analyze. A <see langword="null"/> value is treated as empty.</param>
	/// <returns>A line map for the supplied content.</returns>
	public static TextLineMap Build(string? text)
	{
		text ??= string.Empty;
		(int[] startOffsets, int[] lengths) = TextLineTable.Build(text);

		return new(text, startOffsets, lengths);
	}
}
