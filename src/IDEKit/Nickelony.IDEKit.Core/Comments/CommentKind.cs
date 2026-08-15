namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Identifies the kind of a comment described by a <see cref="CommentSpan"/>.
/// </summary>
public enum CommentKind
{
	/// <summary>
	/// A comment that runs to the next line terminator; see <see cref="CommentSpan"/> for the span
	/// absorption rules.
	/// </summary>
	Line = 0,

	/// <summary>
	/// A comment delimited by an opener and a closer, or by the end of the text when unclosed.
	/// </summary>
	Block = 1
}
