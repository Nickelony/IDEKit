namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// An <see cref="ITextSnapshot"/> backed by an immutable string.
/// Line metadata is materialized on first access (one line entry per line) and recognizes LF, CRLF,
/// and CR line terminators, so a snapshot used only for text or character reads never builds it.
/// </summary>
public sealed class StringTextSnapshot : ITextSnapshot
{
	private readonly string _text;

	// Both members are materialized lazily, so a consumer that only reads the text or characters
	// never pays the line-table cost. The benign race (two threads building equivalent tables) is
	// accepted: the values are immutable once assigned.
	private StringTextLine[]? _lines;
	private IReadOnlyList<ITextLine>? _linesView;

	/// <summary>
	/// Initializes a new instance of the <see cref="StringTextSnapshot"/> class.
	/// </summary>
	/// <param name="text">The source text. A <see langword="null"/> value is treated as an empty string.</param>
	/// <param name="fileName">An optional file name associated with this text.</param>
	public StringTextSnapshot(string? text, string? fileName = null)
	{
		_text = text ?? string.Empty;

		FileName = fileName;
	}

	/// <inheritdoc/>
	public string? FileName { get; }

	/// <inheritdoc/>
	public int TextLength => _text.Length;

	/// <inheritdoc/>
	public int LineCount => LineData.Length;

	/// <inheritdoc/>
	/// <remarks>
	/// The read-only view is created once, on first access, and cached, so repeated reads do not
	/// allocate.
	/// </remarks>
	public IReadOnlyList<ITextLine> Lines => LineView;

	/// <inheritdoc/>
	public char GetCharAt(int offset)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, _text.Length);

		return _text[offset];
	}

	/// <inheritdoc/>
	/// <remarks>
	/// A range that covers the entire text returns the backing instance instead of copying it.
	/// </remarks>
	public string GetText(int offset, int length)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, _text.Length);

		ArgumentOutOfRangeException.ThrowIfNegative(length);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(length, _text.Length - offset);

		return offset == 0 && length == _text.Length
			? _text
			: _text.Substring(offset, length);
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

	/// <summary>
	/// Gets the lazily built line table.
	/// </summary>
	private StringTextLine[] LineData => _lines ??= BuildLines(_text);

	/// <summary>
	/// Gets the lazily created read-only line view.
	/// </summary>
	private IReadOnlyList<ITextLine> LineView => _linesView ??= Array.AsReadOnly(LineData);

	private static StringTextLine[] BuildLines(string text)
	{
		(int[] lineStartOffsets, int[] lineLengths) = TextLineTable.Build(text);
		var lines = new StringTextLine[lineStartOffsets.Length];

		for (int index = 0; index < lines.Length; index++)
			lines[index] = new StringTextLine(lineStartOffsets[index], lineLengths[index], index + 1);

		return lines;
	}

	private sealed class StringTextLine : ITextLine
	{
		public StringTextLine(int offset, int length, int lineNumber)
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
