namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Shared helpers for the comment-operation tests.
/// </summary>
internal static class CommentTestSupport
{
	/// <summary>
	/// Returns the span start of the first comment in <paramref name="text"/>, or <c>-1</c> when
	/// the text holds no comment.
	/// </summary>
	/// <param name="text">The text to search.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>The span start of the first comment, or <c>-1</c>.</returns>
	internal static int FindCommentStart(string text, CommentSyntax syntax)
		=> CommentOperations.FindComment(text, syntax) is { } comment ? comment.StartOffset : -1;
}
