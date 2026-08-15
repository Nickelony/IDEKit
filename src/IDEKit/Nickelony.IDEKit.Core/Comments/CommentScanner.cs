using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Incrementally walks a span of text while tracking whether each position is inside a quoted
/// string literal, a line comment, or a block comment, so scanners recognize delimiters only in code.
/// </summary>
/// <remarks>
/// <para>
/// The state checks run in a fixed order: a pending delimiter remainder is consumed before
/// block-comment matching, so the characters of a detected opener can never pair with what follows
/// (the <c>*</c> of a <c>/*</c> opener cannot close the comment with a following <c>/</c>).
/// Single- and double-quoted string styles reset at each line terminator (CR, LF, or CRLF);
/// backtick, triple-quoted (raw), long-bracket, and verbatim strings span lines; line comments end
/// at a line terminator; block comments may span lines and nest when enabled.
/// </para>
/// <para>
/// This type owns the walk and the comment states only. The string-literal dialects - which openers
/// exist, how each body ends, and how a dialect approximates its language - live in
/// <see cref="StringLiteralScanner"/>, which this scanner consults for every character it reads in
/// code.
/// </para>
/// <para>
/// Interpolated string holes are not modeled in any style, so a quote inside a hole can end the
/// string early. A quote or quote run directly preceded by a backslash is not recognized as an
/// opener in any style.
/// </para>
/// </remarks>
internal ref struct CommentScanner
{
	private readonly ReadOnlySpan<char> _text;
	private readonly ReadOnlySpan<char> _lineDelimiter;
	private readonly ReadOnlySpan<char> _openBlockDelimiter;
	private readonly ReadOnlySpan<char> _closeBlockDelimiter;
	private readonly bool _allowNestedBlockComments;

	private int _position;

	// The string-literal dialects of the scanned language; the core below owns the comment states
	// and the walk only.
	private StringLiteralScanner _strings;

	// The comment state; IsInNonCodeState reads this one value together with the string state of
	// StringLiteralScanner, so a new comment state is a new flag instead of another term in a
	// hand-maintained disjunction. _blockDepth and _delimiterRemaining are the active state's parameters.
	private CommentState _commentState;
	// Set for the move that consumed a delimiter character (an opener, a closer, or one of their
	// remaining characters); such a move is never reported as code, even when it closed a comment
	// and the position after it is code again.
	private bool _delimiterConsumed;
	private int _blockDepth;
	private int _delimiterRemaining;

	// The scanner's comment states. Line and Block are the comment bodies; DelimiterRemainder is the
	// pending characters of a just-detected block delimiter, orthogonal to the body it belongs to.
	[Flags]
	private enum CommentState
	{
		// Not inside a comment; the position may still be in a string, which the string scanner owns.
		None = 0,
		// Inside a line comment.
		Line = 1,
		// Inside a block comment (_blockDepth is the nesting depth).
		Block = 2,
		// Consuming the pending characters of a block delimiter (_delimiterRemaining of them).
		DelimiterRemainder = 4,
	}
	private int _lineCommentStart;
	private int _lineCommentEnd;
	private int _blockCommentStart;
	private int _blockCommentEnd;

	/// <summary>
	/// Initializes a scanner over <paramref name="text"/> using the delimiters and string styles of the
	/// supplied <see cref="CommentSyntax"/>.
	/// </summary>
	/// <param name="text">The text to scan.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	public CommentScanner(ReadOnlySpan<char> text, CommentSyntax syntax)
	{
		BlockCommentSyntax? blockComments = syntax.BlockComments;

		_text = text;
		_strings = new StringLiteralScanner(text, syntax.StringStyle);
		_lineDelimiter = syntax.LineCommentDelimiter;
		_openBlockDelimiter = blockComments?.Open ?? string.Empty;
		_closeBlockDelimiter = blockComments?.Close ?? string.Empty;
		_allowNestedBlockComments = blockComments?.AllowNesting == true;
		_position = 0;
		_commentState = CommentState.None;
		_delimiterConsumed = false;
		_blockDepth = 0;
		_delimiterRemaining = 0;
		_lineCommentStart = -1;
		_lineCommentEnd = -1;
		_blockCommentStart = -1;
		_blockCommentEnd = -1;
	}

	/// <summary>
	/// Gets the index of the last character consumed by the most recent move - for a move that consumes
	/// several characters at once (a line-comment body, a quote run inside a raw string, a doubled
	/// quote in a verbatim string, or an opener such as <c>@"</c> or <c>'''</c>), the last character
	/// of the consumed run - or <c>-1</c> before the first move.
	/// </summary>
	public readonly int CurrentIndex => _position - 1;

	/// <summary>
	/// Gets a value indicating whether the most recent move consumed only code characters, so the
	/// consumed character or run is outside strings and comments. A move that consumed a comment
	/// delimiter character - including the closing delimiter of a comment that ends with it - is not
	/// code; the closing quote of a single-line, backtick, or verbatim string is code (see
	/// <see cref="CommentOperations.GetCodeEnd"/>).
	/// </summary>
	public readonly bool IsInCode => !IsInNonCodeState && !_delimiterConsumed;

	/// <summary>
	/// Gets a value indicating whether the scanner sits inside a string or a comment. The string side
	/// counts both the string state and the closing-delimiter characters that were already detected,
	/// because those are content too; <see cref="IsInCode"/> adds the one-shot delimiter flag on top.
	/// </summary>
	private readonly bool IsInNonCodeState
		=> _commentState != CommentState.None
		|| _strings.IsInString
		|| _strings.HasPendingCloserCharacter;

	/// <summary>
	/// Gets the index where a line comment started during the most recent move, or <c>-1</c> when none
	/// started.
	/// </summary>
	public readonly int LineCommentStartIndex => _lineCommentStart;

	/// <summary>
	/// Gets the exclusive end of a line comment started during the most recent move - the index of its
	/// line terminator, or the text length when the comment is not terminated - or <c>-1</c> when the
	/// most recent move started no line comment.
	/// </summary>
	public readonly int LineCommentEndIndex => _lineCommentEnd;

	/// <summary>
	/// Gets the index where the outermost block comment started during the most recent move, or
	/// <c>-1</c> when none started.
	/// </summary>
	public readonly int BlockCommentStartIndex => _blockCommentStart;

	/// <summary>
	/// Gets the index just after a block comment closed during the most recent move, or <c>-1</c> when
	/// none closed.
	/// </summary>
	public readonly int BlockCommentEndIndex => _blockCommentEnd;

	/// <summary>
	/// Advances the scanner and updates the scanning state.
	/// </summary>
	/// <remarks>
	/// A move that starts a line comment consumes the rest of its content in the same move, because
	/// nothing inside a line comment is recognized; the line terminator arrives with the following
	/// move. A move that enters a verbatim string consumes the at sign and the opening quote (three
	/// characters for the <c>@$"</c> order), a triple-single-quote opener consumes its three quotes,
	/// a quote run inside a raw string that cannot close the string consumes the whole run, and a
	/// doubled quote inside a verbatim string consumes both quotes. Every other move consumes exactly
	/// one character.
	/// </remarks>
	/// <returns>
	/// <see langword="true"/> when the scanner advanced; otherwise, <see langword="false"/> when
	/// the text is exhausted.
	/// </returns>
	public bool MoveNext()
	{
		// Reset the one-shot transition markers for this move.
		_lineCommentStart = -1;
		_lineCommentEnd = -1;
		_blockCommentStart = -1;
		_blockCommentEnd = -1;
		_delimiterConsumed = false;

		if (_position >= _text.Length)
			return false;

		int index = _position;
		char c = _text[index];

		_position = index + 1;

		// Dispatch to the handler for the current scanning state. The order of the state checks is
		// significant: a pending delimiter remainder is consumed before block-comment matching, so
		// an opener's own characters can never pair with what follows. String states and comment
		// states are disjoint, so the string literal scanner answers for a string whenever no
		// comment state applies.
		if ((_commentState & CommentState.Line) != 0)
			ConsumeInLineComment(c);
		else if ((_commentState & CommentState.DelimiterRemainder) != 0)
			ConsumeDelimiterRemainder();
		else if (_strings.HasPendingCloserCharacter)
		{
			// The pending characters belong to a closing string delimiter, so the move is not code.
			_strings.ConsumePendingCloserCharacter();
			_delimiterConsumed = true;
		}
		else if ((_commentState & CommentState.Block) != 0)
			ConsumeInBlockComment(index);
		else if (_strings.IsInString)
			_position += _strings.Consume(index, c);
		else
			ConsumeInCode(index, c);

		return true;
	}

	/// <summary>
	/// Consumes a character inside a line comment; a line terminator (CR, LF, or CRLF) ends the comment.
	/// </summary>
	private void ConsumeInLineComment(char c)
	{
		if (LineTerminators.IsTerminator(c))
			_commentState &= ~CommentState.Line;
	}

	/// <summary>
	/// Consumes one of the remaining characters of a just-detected multi-character block-comment
	/// delimiter (an opener or a closer).
	/// </summary>
	private void ConsumeDelimiterRemainder()
	{
		// Delimiter characters are part of the delimiter, so the move is never reported as code.
		_delimiterConsumed = true;
		_delimiterRemaining--;

		if (_delimiterRemaining == 0)
			_commentState &= ~CommentState.DelimiterRemainder;
	}

	// Records that a detected delimiter left the given number of characters pending; the pending state is
	// a zero-value-free flag so a one-character delimiter leaves no remainder state behind.
	private void SetDelimiterRemainder(int remaining)
	{
		_delimiterRemaining = remaining;

		if (remaining > 0)
			_commentState |= CommentState.DelimiterRemainder;
	}

	/// <summary>
	/// Consumes a character inside a block comment; only the closer and nested openers matter.
	/// </summary>
	private void ConsumeInBlockComment(int index)
	{
		if (IsAt(index, _closeBlockDelimiter))
		{
			_blockDepth--;

			if (_blockDepth == 0)
			{
				_blockCommentEnd = index + _closeBlockDelimiter.Length;
				_commentState &= ~CommentState.Block;
			}

			// The detected character and the remaining closer characters are still part of
			// the comment and must not be reported as code. They are consumed at every depth,
			// so an overlapping closer run (for example "]]]") cannot reuse a closer character
			// for the next nesting level. The detected character was consumed by this move,
			// so only Length - 1 remain; the move is marked as a delimiter move here so a
			// one-character closer behaves like the longer forms (no remainder move follows
			// that would set the mark).
			_delimiterConsumed = true;
			SetDelimiterRemainder(_closeBlockDelimiter.Length - 1);
		}
		else if (_allowNestedBlockComments && IsAt(index, _openBlockDelimiter))
		{
			_blockDepth++;

			// Skip the nested opener's remaining characters before the next closer check; the
			// detected character is part of the opener, so the move is not code either.
			_delimiterConsumed = true;
			SetDelimiterRemainder(_openBlockDelimiter.Length - 1);
		}
	}

	/// <summary>
	/// Consumes a character in code and checks for openers in precedence order: the string-literal
	/// openers of the configured styles (see <see cref="StringLiteralScanner.TryOpen"/>), the block
	/// opener, and finally the line delimiter.
	/// </summary>
	private void ConsumeInCode(int index, char c)
	{
		int openerExtraCharacters = _strings.TryOpen(index, c);

		if (openerExtraCharacters >= 0)
		{
			_position += openerExtraCharacters;

			return;
		}

		if (!_openBlockDelimiter.IsEmpty && IsAt(index, _openBlockDelimiter))
		{
			_blockDepth = 1;
			_blockCommentStart = index;
			_commentState |= CommentState.Block;

			// Consume the opener's remaining characters before closer matching starts. Otherwise
			// the opener's own characters could pair with what follows: for example, the '*' in a
			// "/*" opener with a following '/' would close the comment immediately.
			SetDelimiterRemainder(_openBlockDelimiter.Length - 1);

			return;
		}

		if (!_lineDelimiter.IsEmpty && IsAt(index, _lineDelimiter))
		{
			_commentState |= CommentState.Line;
			_lineCommentStart = index;

			// A line comment runs to the next line terminator and nothing inside it is recognized,
			// so both its end and the position after its content are known here. The terminator
			// itself is consumed by the following move, which also resets the comment state. The end
			// scan is vectorized so a long comment line is not walked character by character.
			int contentStart = index + _lineDelimiter.Length;
			int terminatorIndex = _text[contentStart..].IndexOfAny('\r', '\n');
			int contentEnd = terminatorIndex < 0 ? _text.Length : contentStart + terminatorIndex;

			_lineCommentEnd = contentEnd;
			_position = contentEnd;
		}
	}

	/// <summary>
	/// Determines whether <paramref name="marker"/> begins at <paramref name="index"/>. An empty
	/// marker never matches.
	/// </summary>
	private readonly bool IsAt(int index, ReadOnlySpan<char> marker)
		=> !marker.IsEmpty
		&& index + marker.Length <= _text.Length
		&& _text[index] == marker[0]
		&& _text.Slice(index, marker.Length).SequenceEqual(marker);
}
