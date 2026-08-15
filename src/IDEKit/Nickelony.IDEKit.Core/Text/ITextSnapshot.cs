namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Represents an immutable snapshot of text used by editor and language-tooling services.
/// </summary>
/// <remarks>
/// Offsets are zero-based UTF-16 positions and line numbers are one-based. Implementations
/// capture their content at construction time. The zero-based offset is the canonical address of this
/// assembly, so a line number here is the one-based identity the line-marker surface needs, while the
/// coordinate types address the same text through zero-based line indexes and
/// <see cref="TextLineMap"/> converts between the two families.
/// </remarks>
public interface ITextSnapshot
{
	/// <summary>
	/// Gets the optional file name associated with this text source.
	/// </summary>
	string? FileName { get; }

	/// <summary>
	/// Gets the total number of UTF-16 code units in the text.
	/// </summary>
	int TextLength { get; }

	/// <summary>
	/// Gets the total number of lines in the text.
	/// </summary>
	int LineCount { get; }

	/// <summary>
	/// Gets all lines in the text, in order.
	/// </summary>
	/// <remarks>
	/// The list is a cached read-only view, so the count and indexer are available without
	/// enumerating and repeated reads do not allocate. An implementation with lazy storage may build
	/// the view on the first read.
	/// </remarks>
	IReadOnlyList<ITextLine> Lines { get; }

	/// <summary>
	/// Gets the character at the specified zero-based UTF-16 offset.
	/// </summary>
	/// <param name="offset">The zero-based UTF-16 offset of the character to retrieve.</param>
	/// <returns>The character at the specified offset.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="offset"/> is negative or not less than <see cref="TextLength"/>.
	/// </exception>
	char GetCharAt(int offset);

	/// <summary>
	/// Retrieves the text within the specified zero-based UTF-16 range.
	/// </summary>
	/// <remarks>
	/// An empty range at the end of the text is valid: <paramref name="offset"/> may equal
	/// <see cref="TextLength"/> when <paramref name="length"/> is zero. Implementations with
	/// string-backed storage may return the backing instance for a range that covers the entire
	/// text instead of copying it.
	/// </remarks>
	/// <param name="offset">The zero-based start offset of the text to retrieve.</param>
	/// <param name="length">The number of UTF-16 code units to retrieve.</param>
	/// <returns>The text in the specified range.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="offset"/> is negative or greater than <see cref="TextLength"/>, or
	/// <paramref name="length"/> is negative or extends beyond the end of the text.
	/// </exception>
	string GetText(int offset, int length);

	/// <summary>
	/// Gets the line associated with the specified zero-based UTF-16 offset.
	/// </summary>
	/// <remarks>
	/// An offset on a line terminator is associated with the preceding line, and the end-of-text
	/// offset is associated with the final line.
	/// </remarks>
	/// <param name="offset">The zero-based UTF-16 offset to locate.</param>
	/// <returns>The line containing the specified offset.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="offset"/> is negative or greater than <see cref="TextLength"/>.
	/// </exception>
	ITextLine GetLineByOffset(int offset);

	/// <summary>
	/// Gets the line with the specified one-based line number.
	/// </summary>
	/// <param name="lineNumber">The one-based line number to retrieve.</param>
	/// <returns>The line at the specified line number.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="lineNumber"/> is less than 1 or greater than <see cref="LineCount"/>.
	/// </exception>
	ITextLine GetLineByNumber(int lineNumber);
}
