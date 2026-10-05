namespace Nickelony.IDEKit.Core.Text;

/// <summary>Provides a precomputed table of line start offsets and lengths for document content.</summary>
/// <remarks>
/// Line indexes are zero-based, and LF, CRLF, and lone CR are line terminators. Out-of-range line indexes and offsets are clamped
/// rather than rejected, so a caller that tolerates stale host coordinates need not pre-validate them.
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

	/// <summary>The number of logical lines in the document.</summary>
	public int LineCount => _lineLengths.Length;

	/// <summary>The document length in UTF-16 code units.</summary>
	public int TextLength => _text.Length;

	/// <summary>Returns the length of the specified zero-based line.</summary>
	/// <param name="lineIndex">The zero-based line index. Values outside the line range are clamped.</param>
	/// <returns>The line length in UTF-16 code units.</returns>
	public int GetLineLength(int lineIndex) => _lineLengths[ClampLineIndex(lineIndex)];

	/// <summary>Returns the absolute document offset where the specified zero-based line starts.</summary>
	/// <param name="lineIndex">The zero-based line index. Values outside the line range are clamped.</param>
	/// <returns>The absolute document offset.</returns>
	public int GetLineStartOffset(int lineIndex) => _lineStartOffsets[ClampLineIndex(lineIndex)];

	/// <summary>Returns the document offset for the supplied zero-based line and character indices.</summary>
	/// <param name="lineIndex">The zero-based line index. Values outside the line range are clamped.</param>
	/// <param name="character">
	/// The zero-based character index within the line. Negative values clamp to 0; values beyond the line length clamp to the line length.
	/// </param>
	/// <returns>The absolute document offset.</returns>
	public int GetOffset(int lineIndex, int character)
	{
		int safeLine = ClampLineIndex(lineIndex);
		int safeCharacter = Math.Clamp(character, 0, _lineLengths[safeLine]);

		return _lineStartOffsets[safeLine] + safeCharacter;
	}

	/// <summary>Returns the document offset for the supplied zero-based position.</summary>
	/// <param name="position">The zero-based line and character position. Values outside the document are clamped.</param>
	/// <returns>The absolute document offset.</returns>
	public int GetOffset(TextPosition position) => GetOffset(position.Line, position.Character);

	/// <summary>Converts a position range to the corresponding offset range.</summary>
	/// <param name="range">The position range to convert.</param>
	/// <param name="offsetRange">Receives the offset range when the conversion succeeds.</param>
	/// <returns><see langword="true"/> when the range maps to a representable offset range.</returns>
	/// <remarks>
	/// Each endpoint is converted like <see cref="GetOffset(TextPosition)"/>, so values outside the document are clamped. An empty range
	/// (both endpoints mapping to the same offset) succeeds with a zero-length result, because that conversion is exact; the conversion fails
	/// only when the mapped end precedes the mapped start, because <see cref="TextRange"/> cannot represent an inverted range.
	/// </remarks>
	public bool TryGetOffsets(TextPositionRange range, out TextRange offsetRange)
	{
		int start = GetOffset(range.Start);
		int end = GetOffset(range.End);

		if (end < start)
		{
			offsetRange = default;
			return false;
		}

		offsetRange = TextRange.FromBounds(start, end);
		return true;
	}

	/// <summary>Returns the text of the specified zero-based line as a memory slice without its trailing line terminator.</summary>
	/// <param name="lineIndex">The zero-based line index. Values outside the line range are clamped.</param>
	/// <returns>The line text as a memory slice over the source text.</returns>
	/// <remarks>
	/// The slice is a view over this map's immutable source text, not a copy.
	/// </remarks>
	internal ReadOnlyMemory<char> GetLineMemory(int lineIndex)
	{
		int safeLine = ClampLineIndex(lineIndex);
		return _text.AsMemory(_lineStartOffsets[safeLine], _lineLengths[safeLine]);
	}

	/// <summary>Returns the text of the specified zero-based line as a span without its trailing line terminator.</summary>
	/// <param name="lineIndex">The zero-based line index. Values outside the line range are clamped.</param>
	/// <returns>The line text as a span over the source text.</returns>
	/// <remarks>
	/// The span is a view over this map's immutable source text, not a copy.
	/// </remarks>
	internal ReadOnlySpan<char> GetLineSpan(int lineIndex) => GetLineMemory(lineIndex).Span;

	/// <summary>Returns the text of the specified zero-based line without its trailing line terminator.</summary>
	/// <param name="lineIndex">The zero-based line index. Values outside the line range are clamped.</param>
	public string GetLineText(int lineIndex)
	{
		int safeLine = ClampLineIndex(lineIndex);
		return _text.Substring(_lineStartOffsets[safeLine], _lineLengths[safeLine]);
	}

	/// <summary>Converts an offset into a zero-based line and character position.</summary>
	/// <param name="offset">The zero-based UTF-16 document offset. Values outside the document are clamped.</param>
	/// <returns>The containing position.</returns>
	/// <remarks>
	/// The character index is measured from the line start and clamped to the line length, so the result is always a position
	/// a host or protocol can accept. The offset between the CR and the LF of a CRLF terminator maps to the end of the preceding line,
	/// so converting that position back with <see cref="GetOffset(int, int)"/> returns the CR's offset. The conversion is lossless
	/// for every other offset in the document.
	/// </remarks>
	public TextPosition GetPosition(int offset)
	{
		int safeOffset = Math.Clamp(offset, 0, TextLength);
		int lineIndex = LineIndexSearch.FindLineIndex(_lineStartOffsets, safeOffset);
		int character = Math.Min(safeOffset - _lineStartOffsets[lineIndex], _lineLengths[lineIndex]);

		return new(lineIndex, character);
	}

	/// <summary>Converts an offset range into the positions of its start and end.</summary>
	/// <param name="range">The offset range to convert. An endpoint outside the document is clamped.</param>
	/// <returns>The positions of the range start and the range end.</returns>
	/// <remarks>
	/// Each endpoint converts like <see cref="GetPosition(int)"/>.
	/// </remarks>
	public TextPositionRange GetPositionRange(TextRange range) => new(GetPosition(range.Offset), GetPosition(range.EndOffset));

	/// <summary>Converts an offset range into the positions of its start and end with one forward scan and no line map.</summary>
	/// <param name="text">The document text the range belongs to.</param>
	/// <param name="range">The offset range to convert. An endpoint past the end of the text clamps to it.</param>
	/// <returns>The positions of the range start and the range end.</returns>
	/// <remarks>
	/// The scan stops at the range end and allocates nothing, so a caller that sees each text version once, such as a per-keystroke path,
	/// avoids building a line map. The results equal those of <see cref="GetPositionRange(TextRange)"/> on a map built from the same text,
	/// including the position between the CR and the LF of a CRLF terminator, which is the end of the preceding line.
	/// </remarks>
	public static TextPositionRange GetPositionRange(ReadOnlySpan<char> text, TextRange range)
	{
		int endOffset = Math.Min(range.EndOffset, text.Length);
		int startOffset = Math.Min(range.Offset, endOffset);

		int line = 0;
		int lineStart = 0;
		int scanned = 0;

		ScanTo(text, limit: startOffset, ref line, ref lineStart, ref scanned);
		var start = new TextPosition(line, scanned - lineStart);

		ScanTo(text, limit: endOffset, ref line, ref lineStart, ref scanned);
		return new TextPositionRange(start, new TextPosition(line, scanned - lineStart));
	}

	private int ClampLineIndex(int lineIndex) => Math.Clamp(lineIndex, 0, _lineLengths.Length - 1);

	// Advances the scan to the limit, counting the terminators that end at or before it. A limit between the CR and the LF of a CRLF stops on the
	// CR without consuming it, so the character is measured from the CR and a later scan sees the terminator as a pair.
	private static void ScanTo(ReadOnlySpan<char> text, int limit, ref int line, ref int lineStart, ref int scanned)
	{
		while (scanned < limit)
		{
			int terminatorIndex = text[scanned..limit].IndexOfAny('\r', '\n');

			if (terminatorIndex < 0)
			{
				scanned = limit;
				return;
			}

			int terminator = scanned + terminatorIndex;
			int terminatorLength = LineTerminators.GetTerminatorLength(text, terminator);

			if (terminator + terminatorLength > limit)
			{
				scanned = terminator;
				return;
			}

			line++;
			lineStart = terminator + terminatorLength;
			scanned = lineStart;
		}
	}

	/// <summary>Builds a line map from the supplied content.</summary>
	/// <param name="text">The document text to analyze. A <see langword="null"/> value is treated as empty.</param>
	public static TextLineMap Build(string? text)
	{
		text ??= string.Empty;
		(int[] startOffsets, int[] lengths) = TextLineIndexBuilder.Build(text);

		return new(text, startOffsets, lengths);
	}
}
