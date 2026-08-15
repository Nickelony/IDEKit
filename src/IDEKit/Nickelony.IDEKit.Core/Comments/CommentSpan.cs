namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Describes the location of a comment found while scanning text.
/// </summary>
public readonly record struct CommentSpan
{
	/// <summary>
	/// Gets the offset at which the comment span begins.
	/// </summary>
	/// <remarks>
	/// For a line comment this includes the whitespace on the comment's own line and, when the line
	/// holds only that comment, the single line terminator that ends the preceding line (the span that
	/// removal and masking operate on). Blank lines above a comment-only line stay outside the span.
	/// For a block comment the value equals <see cref="DelimiterStart"/> because block comments do
	/// not consume preceding whitespace.
	/// </remarks>
	public int StartOffset { get; }

	/// <summary>
	/// Gets the offset of the comment delimiter itself: the line-comment delimiter, or the
	/// block-comment opener. The value is always at or after <see cref="StartOffset"/>.
	/// </summary>
	public int DelimiterStart { get; }

	/// <summary>
	/// Gets the offset one past the end of the comment.
	/// </summary>
	/// <remarks>
	/// For a line comment this is the offset of the line terminator (CR, LF, or CRLF), which is
	/// left outside the span, or the end of the text. For a block comment it is the offset just
	/// after the closer, or the end of the text when the comment is unclosed.
	/// </remarks>
	public int EndOffset { get; }

	/// <summary>
	/// Gets the kind of comment this span describes.
	/// </summary>
	public CommentKind Kind { get; }

	/// <summary>
	/// Gets a value indicating whether this is a line comment, as opposed to a block comment.
	/// </summary>
	public bool IsLineComment => Kind == CommentKind.Line;

	/// <summary>
	/// Gets a value indicating whether this is a block comment, as opposed to a line comment.
	/// </summary>
	public bool IsBlockComment => Kind == CommentKind.Block;

	/// <summary>
	/// Gets the length of the comment span in UTF-16 code units.
	/// </summary>
	public int Length => EndOffset - StartOffset;

	/// <summary>
	/// Initializes a new instance of the <see cref="CommentSpan"/> struct.
	/// </summary>
	/// <param name="startOffset">The offset at which the reported span begins.</param>
	/// <param name="delimiterStart">The offset of the comment delimiter (the line-comment delimiter or the block-comment opener).</param>
	/// <param name="endOffset">The offset one past the end of the comment.</param>
	/// <param name="kind">The kind of comment the span describes.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="startOffset"/> is negative, <paramref name="delimiterStart"/> is before
	/// <paramref name="startOffset"/>, or <paramref name="endOffset"/> is before <paramref name="delimiterStart"/>.
	/// </exception>
	public CommentSpan(int startOffset, int delimiterStart, int endOffset, CommentKind kind)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(startOffset);
		ArgumentOutOfRangeException.ThrowIfLessThan(delimiterStart, startOffset);
		ArgumentOutOfRangeException.ThrowIfLessThan(endOffset, delimiterStart);

		StartOffset = startOffset;
		DelimiterStart = delimiterStart;
		EndOffset = endOffset;
		Kind = kind;
	}

	/// <summary>
	/// Returns the span in half-open interval notation, for example <c>[4..10)</c>.
	/// </summary>
	/// <returns>A string in the form <c>[StartOffset..EndOffset)</c>.</returns>
	public override string ToString()
		=> $"[{StartOffset}..{EndOffset})";
}
