namespace Nickelony.IDEKit.Core.Text;

/// <summary>An <see cref="ITextSnapshot"/> backed by an immutable string.</summary>
/// <remarks>
/// Line metadata is materialized on first access, so a snapshot that reads only text or characters never builds it. It holds one entry per line
/// and recognizes LF, CRLF, and lone CR as line terminators. Each entry is one object, because <see cref="Lines"/> and <see cref="GetLineByOffset"/>
/// return <see cref="ITextLine"/> instances; a packed offset table would need another <see cref="ITextLine"/> shape, and the published contract keeps
/// this one.
/// </remarks>
public sealed class StringTextSnapshot : ITextSnapshot
{
	private readonly string _text;

	// The line table and its read-only view are materialized lazily, so a consumer that only reads the
	// text or characters never pays the line-table cost. The benign race (two threads building
	// equivalent tables) is accepted: the values are immutable once assigned.
	private StringTextLine[]? _lines;
	private IReadOnlyList<ITextLine>? _linesView;

	/// <param name="text">The source text. A <see langword="null"/> value is treated as an empty string.</param>
	/// <param name="documentId">An optional identifier of the document this text belongs to.</param>
	public StringTextSnapshot(string? text, string? documentId = null)
	{
		_text = text ?? string.Empty;
		DocumentId = documentId;
	}

	/// <inheritdoc/>
	public string? DocumentId { get; }

	/// <inheritdoc/>
	public int TextLength => _text.Length;

	/// <inheritdoc/>
	public int LineCount => LineData.Length;

	/// <inheritdoc/>
	public IReadOnlyList<ITextLine> Lines => LineView;

	/// <inheritdoc/>
	public char GetCharAt(int offset)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, _text.Length);

		return _text[offset];
	}

	/// <inheritdoc/>
	public string GetText(int offset, int length)
	{
		TextOffsetValidation.ValidateOffset(textLength: _text.Length, offset, parameterName: nameof(offset), offsetDescription: "string snapshot");
		ArgumentOutOfRangeException.ThrowIfNegative(length);

		TextOffsetValidation.ValidateRange(
			textLength: _text.Length,
			range: new TextRange(offset, length),
			parameterName: nameof(length),
			rangeDescription: "string snapshot"
		);

		return offset == 0 && length == _text.Length ? _text : _text.Substring(offset, length);
	}

	/// <inheritdoc/>
	public ITextLine GetLineByOffset(int offset)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, _text.Length);

		StringTextLine[] lines = LineData;

		return lines[LineIndexSearch.FindLineIndex<StringTextLine>(lines, offset)];
	}

	/// <inheritdoc/>
	public ITextLine GetLineByNumber(int lineNumber)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(lineNumber, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(lineNumber, LineData.Length);

		return LineData[lineNumber - 1];
	}

	private StringTextLine[] LineData => _lines ??= BuildLines(_text);

	private IReadOnlyList<ITextLine> LineView => _linesView ??= Array.AsReadOnly(LineData);

	private static StringTextLine[] BuildLines(string text)
	{
		(int[] lineStartOffsets, int[] lineLengths) = TextLineIndexBuilder.Build(text);
		var lines = new StringTextLine[lineStartOffsets.Length];

		for (int index = 0; index < lines.Length; index++)
			lines[index] = new StringTextLine(lineStartOffsets[index], lineLengths[index], index + 1);

		return lines;
	}

	private sealed class StringTextLine : ITextLine
	{
		internal StringTextLine(int offset, int length, int lineNumber)
		{
			Offset = offset;
			Length = length;
			EndOffset = offset + length;
			LineNumber = lineNumber;
		}

		public int Offset { get; }

		public int Length { get; }

		public int EndOffset { get; }

		public int LineNumber { get; }
	}
}
