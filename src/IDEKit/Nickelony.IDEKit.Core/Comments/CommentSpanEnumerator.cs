using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Enumerates the comments in a span of text in a single forward pass, recognizing line comments
/// and block comments together.
/// </summary>
/// <remarks>
/// Delimiters inside string literals and comments are ignored. Create the enumerator through
/// <see cref="CommentOperations.EnumerateComments(ReadOnlySpan{char}, CommentSyntax)"/> and consume
/// it with <c>foreach</c> or with explicit <see cref="MoveNext"/> calls.
/// </remarks>
public ref struct CommentSpanEnumerator
{
	private readonly ReadOnlySpan<char> _text;
	private CommentScanner _scanner;
	private int _floor;
	private CommentSpan _current;

	/// <summary>
	/// Gets the current comment span. Valid only after <see cref="MoveNext"/> has
	/// returned <see langword="true"/>; before the first move the value is the default instance and
	/// does not describe a comment.
	/// </summary>
	public readonly CommentSpan Current => _current;

	/// <summary>
	/// Initializes a new instance of the <see cref="CommentSpanEnumerator"/> struct. Create instances
	/// through <see cref="CommentOperations.EnumerateComments(ReadOnlySpan{char}, CommentSyntax)"/>.
	/// </summary>
	/// <param name="text">The text to scan. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	internal CommentSpanEnumerator(ReadOnlySpan<char> text, CommentSyntax syntax)
	{
		_text = text;
		_scanner = new CommentScanner(text, syntax);
		_floor = 0;
		_current = default;
	}

	/// <summary>
	/// Returns this enumerator so the comment spans can be consumed with <c>foreach</c>.
	/// </summary>
	/// <returns>The current enumerator instance.</returns>
	public readonly CommentSpanEnumerator GetEnumerator()
		=> this;

	/// <summary>
	/// Advances to the next comment span in the text.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> when a comment span is available in <see cref="Current"/>;
	/// otherwise, <see langword="false"/>.
	/// </returns>
	public bool MoveNext()
	{
		// Phase 1: locate the next comment opener in code (outside strings and comments).
		int lineStart = -1;
		int blockStart = -1;

		while (_scanner.MoveNext())
		{
			if (_scanner.LineCommentStartIndex >= 0)
			{
				lineStart = _scanner.LineCommentStartIndex;
				break;
			}

			if (_scanner.BlockCommentStartIndex >= 0)
			{
				blockStart = _scanner.BlockCommentStartIndex;
				break;
			}
		}

		if (lineStart < 0 && blockStart < 0)
			return false;

		if (lineStart >= 0)
		{
			// The span includes the whitespace on the comment's own line - so it can start before the
			// delimiter - plus at most one preceding line terminator, so a comment-only line removes or
			// masks its own line without consuming blank lines above it. It never starts before the
			// previous comment's end.
			int start = lineStart;

			while (start > _floor && char.IsWhiteSpace(_text[start - 1]))
			{
				start--;

				if (LineTerminators.IsTerminator(_text[start]))
				{
					// CRLF is a single line terminator, so the LF pulls its preceding CR in with it.
					if (_text[start] == '\n' && start > _floor && LineTerminators.IsCrLfPair(_text[start - 1], _text[start]))
						start--;

					break;
				}
			}

			// A line comment runs to the line terminator (exclusive) or the end of the text; the
			// scanner already resolved that end when it detected the delimiter.
			int end = _scanner.LineCommentEndIndex;

			_current = new CommentSpan(start, lineStart, end, CommentKind.Line);
			_floor = end;

			return true;
		}

		// Phase 2: continue to the matching block closer; the scanner tracks nesting.
		int blockEnd = -1;

		while (_scanner.MoveNext())
		{
			if (_scanner.BlockCommentEndIndex >= 0)
			{
				blockEnd = _scanner.BlockCommentEndIndex;
				break;
			}
		}

		// An unclosed block comment extends to the end of the text.
		if (blockEnd < 0)
			blockEnd = _text.Length;

		_current = new CommentSpan(blockStart, blockStart, blockEnd, CommentKind.Block);
		_floor = blockEnd;

		return true;
	}
}
