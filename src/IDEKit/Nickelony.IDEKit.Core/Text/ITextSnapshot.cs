namespace Nickelony.IDEKit.Core.Text;

/// <summary>Represents an immutable snapshot of text used by editor and language-tooling services.</summary>
/// <remarks>
/// Offsets are zero-based UTF-16 positions and line numbers are one-based. Implementations capture their content at construction time.
/// </remarks>
public interface ITextSnapshot : ITextCharSource
{
	/// <summary>The identifier of the document the text belongs to, or <see langword="null"/> when none was given.</summary>
	/// <remarks>
	/// The snapshot stores the value as given and does not validate it. The host defines what an identifier means, for example a normalized
	/// file path.
	/// </remarks>
	string? DocumentId { get; }

	/// <summary>The number of lines in the text.</summary>
	/// <remarks>
	/// Empty text has one line, and a trailing line terminator starts a final empty line.
	/// </remarks>
	int LineCount { get; }

	/// <summary>The lines of the text, in order.</summary>
	/// <remarks>
	/// The list is a cached read-only view, so the count and indexer are available without enumerating and repeated reads do not allocate.
	/// An implementation with lazy storage may build the view on the first read.
	/// </remarks>
	IReadOnlyList<ITextLine> Lines { get; }

	/// <summary>Retrieves the text within the specified zero-based UTF-16 range.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="offset"/> is negative or greater than <see cref="ITextCharSource.TextLength"/>, or <paramref name="length"/> is negative
	/// or extends beyond the end of the text.
	/// </exception>
	/// <remarks>
	/// An empty range at the end of the text is valid: <paramref name="offset"/> may equal <see cref="ITextCharSource.TextLength"/>
	/// when <paramref name="length"/> is zero. Implementations with string-backed storage may return the backing instance for a range that covers
	/// the entire text instead of copying it.
	/// </remarks>
	string GetText(int offset, int length);

	/// <summary>Returns the line associated with the specified zero-based UTF-16 offset.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="offset"/> is negative or greater than <see cref="ITextCharSource.TextLength"/>.
	/// </exception>
	/// <remarks>
	/// An offset on a line terminator is associated with the preceding line, and the end-of-text offset is associated with the final line.
	/// </remarks>
	ITextLine GetLineByOffset(int offset);

	/// <summary>Returns the line with the specified one-based line number.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="lineNumber"/> is less than 1 or greater than <see cref="LineCount"/>.
	/// </exception>
	ITextLine GetLineByNumber(int lineNumber);
}
