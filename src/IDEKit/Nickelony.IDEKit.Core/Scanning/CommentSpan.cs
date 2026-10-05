using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Scanning;

/// <summary>Describes the location of a comment found while scanning text.</summary>
public readonly record struct CommentSpan
{
	/// <summary>The range of the comment.</summary>
	/// <remarks>
	/// For a line comment the range includes the whitespace on the comment's own line plus the single preceding line terminator it absorbs
	/// when the line holds only that comment, and it ends at the offset of the line terminator, which stays outside the range, or at the end
	/// of the text. For a block comment the range starts at <see cref="DelimiterStart"/> and ends immediately after the closer, or at the end
	/// of the text when the comment is unclosed.
	/// </remarks>
	public TextRange Range { get; }

	/// <summary>The offset of the comment delimiter: the line-comment delimiter or the block-comment opener.</summary>
	/// <remarks>
	/// The value lies within <see cref="Range"/>, at or after its start.
	/// </remarks>
	public int DelimiterStart { get; }

	public CommentKind Kind { get; }

	public bool IsLineComment => Kind == CommentKind.Line;

	public bool IsBlockComment => Kind == CommentKind.Block;

	/// <param name="range">The range of the comment.</param>
	/// <param name="delimiterStart">The offset of the comment delimiter (the line-comment delimiter or the block-comment opener).</param>
	/// <param name="kind">The kind of comment the span describes.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="delimiterStart"/> is before the start of <paramref name="range"/> or after its end.
	/// </exception>
	public CommentSpan(TextRange range, int delimiterStart, CommentKind kind)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(delimiterStart, range.Offset);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(delimiterStart, range.EndOffset);

		Range = range;
		DelimiterStart = delimiterStart;
		Kind = kind;
	}

	/// <summary>Returns the range in half-open interval notation, for example <c>[4..10)</c>.</summary>
	public override string ToString() => Range.ToString();
}
