using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Scanning;

/// <summary>
/// Incrementally walks a span of text while tracking whether each position is inside a quoted string literal, a line comment, or a block comment,
/// so scanners recognize delimiters only in code.
/// </summary>
/// <remarks>
/// The state checks run in a fixed order: a pending delimiter remainder is consumed before block-comment matching, so the characters of a detected
/// opener can never pair with what follows. Line comments end at a line terminator, block comments may span lines and nest when enabled,
/// and interpolated string holes are not modeled, so a quote inside a hole can end the string early.
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
	private CommentStates _commentState;

	// Set for the move that consumed a delimiter character (an opener, a closer, or one of their
	// remaining characters); such a move is never reported as code, even when it closed a comment
	// and the position after it is code again.
	private bool _delimiterConsumed;

	private int _blockDepth;
	private int _delimiterRemaining;

	// The scanner's comment states. Line and Block are the comment bodies; DelimiterRemainder is the
	// pending characters of a newly detected block delimiter, orthogonal to the body it belongs to.
	[Flags]
	private enum CommentStates
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

	/// <param name="text">The text to scan.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	internal CommentScanner(ReadOnlySpan<char> text, CommentSyntax syntax)
	{
		BlockCommentSyntax? blockComments = syntax.BlockComments;

		_text = text;
		_strings = new StringLiteralScanner(text, syntax.StringStyle);
		_lineDelimiter = syntax.LineCommentDelimiter;
		_openBlockDelimiter = blockComments?.Open ?? string.Empty;
		_closeBlockDelimiter = blockComments?.Close ?? string.Empty;
		_allowNestedBlockComments = blockComments?.AllowNesting == true;
		_position = 0;
		_commentState = CommentStates.None;
		_delimiterConsumed = false;
		_blockDepth = 0;
		_delimiterRemaining = 0;
		LineCommentStartIndex = -1;
		LineCommentEndIndex = -1;
		BlockCommentStartIndex = -1;
		BlockCommentEndIndex = -1;
	}

	/// <summary>
	/// The index of the last character consumed by the most recent move - the last character of the run when a move consumes several characters
	/// at once - or <c>-1</c> before the first move.
	/// </summary>
	internal readonly int CurrentIndex => _position - 1;

	/// <summary>
	/// Whether the most recent move consumed only code characters; a move that consumed a comment delimiter character - including the closing
	/// delimiter of a comment that ends with it - is not code.
	/// </summary>
	internal readonly bool IsInCode => !IsInNonCodeState && !_delimiterConsumed;

	/// <summary>
	/// Whether the scanner sits inside a string or a comment, counting the string state's pending closing-delimiter characters as content;
	/// <see cref="IsInCode"/> adds the one-shot delimiter flag on top.
	/// </summary>
	private readonly bool IsInNonCodeState => _commentState != CommentStates.None || _strings.IsInString || _strings.HasPendingCloserCharacter;

	/// <summary>The index where a line comment started during the most recent move, or <c>-1</c> when none started.</summary>
	internal int LineCommentStartIndex { get; private set; }

	/// <summary>
	/// The exclusive end of a line comment started during the most recent move - the index of its line terminator, or the text length
	/// when the comment is not terminated - or <c>-1</c> when the most recent move started no line comment.
	/// </summary>
	internal int LineCommentEndIndex { get; private set; }

	/// <summary>The index where the outermost block comment started during the most recent move, or <c>-1</c> when none started.</summary>
	internal int BlockCommentStartIndex { get; private set; }

	/// <summary>The index immediately after a block comment closed during the most recent move, or <c>-1</c> when none closed.</summary>
	internal int BlockCommentEndIndex { get; private set; }

	/// <summary>Advances the scanner and updates the scanning state.</summary>
	/// <returns><see langword="true"/> when the scanner advanced; otherwise, <see langword="false"/> when the text is exhausted.</returns>
	/// <remarks>
	/// A move that starts a line comment consumes the rest of its content in the same move, so the line terminator arrives with the following move.
	/// A move that opens a multi-character construct - a verbatim or triple-quote string opener, a quote run inside a raw string, or a doubled quote
	/// in a verbatim string - consumes that run; every other move consumes exactly one character.
	/// </remarks>
	internal bool MoveNext()
	{
		// Reset the one-shot transition markers for this move.
		LineCommentStartIndex = -1;
		LineCommentEndIndex = -1;
		BlockCommentStartIndex = -1;
		BlockCommentEndIndex = -1;
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
		if ((_commentState & CommentStates.Line) != 0)
		{
			ConsumeInLineComment(c);
		}
		else if ((_commentState & CommentStates.DelimiterRemainder) != 0)
		{
			ConsumeDelimiterRemainder();
		}
		else if (_strings.HasPendingCloserCharacter)
		{
			// The pending characters belong to a closing string delimiter, so the move is not code.
			_strings.ConsumePendingCloserCharacter();
			_delimiterConsumed = true;
		}
		else if ((_commentState & CommentStates.Block) != 0)
		{
			ConsumeInBlockComment(index);
		}
		else if (_strings.IsInString)
		{
			_position += _strings.Consume(index, c);
		}
		else
		{
			ConsumeInCode(index, c);
		}

		return true;
	}

	/// <summary>Consumes a character inside a line comment; a line terminator (CR, LF, or CRLF) ends the comment.</summary>
	private void ConsumeInLineComment(char c)
	{
		if (LineTerminators.IsTerminator(c))
			_commentState &= ~CommentStates.Line;
	}

	/// <summary>
	/// Consumes one of the remaining characters of a newly detected multi-character block-comment delimiter (an opener or a closer).
	/// </summary>
	private void ConsumeDelimiterRemainder()
	{
		// Delimiter characters are part of the delimiter, so the move is never reported as code.
		_delimiterConsumed = true;
		_delimiterRemaining--;

		if (_delimiterRemaining == 0)
			_commentState &= ~CommentStates.DelimiterRemainder;
	}

	// Records that a detected delimiter left the given number of characters pending; the pending state is
	// set only when characters remain, so a one-character delimiter leaves no remainder state behind.
	private void SetDelimiterRemainder(int remaining)
	{
		_delimiterRemaining = remaining;

		if (remaining > 0)
			_commentState |= CommentStates.DelimiterRemainder;
	}

	/// <summary>Consumes a character inside a block comment; only the closer and nested openers matter.</summary>
	private void ConsumeInBlockComment(int index)
	{
		if (IsAt(index, _closeBlockDelimiter))
		{
			_blockDepth--;

			if (_blockDepth == 0)
			{
				BlockCommentEndIndex = index + _closeBlockDelimiter.Length;
				_commentState &= ~CommentStates.Block;
			}

			// The detected character and the remaining closer characters are still part of the comment and
			// must not be reported as code. They are consumed at every depth, so an overlapping closer run
			// (for example "]]]") cannot reuse a closer character for the next nesting level. The detected
			// character was consumed by this move, so only Length - 1 remain; the move is marked as a
			// delimiter move here so a one-character closer behaves like the longer forms, where no
			// remainder move follows to set the mark.
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
	/// Consumes a character in code and checks for openers in precedence order: the string-literal openers of the configured
	/// styles (see <see cref="StringLiteralScanner.ScanOpener"/>), the block opener, and finally the line delimiter.
	/// </summary>
	private void ConsumeInCode(int index, char c)
	{
		int openerExtraCharacters = _strings.ScanOpener(index, c);

		if (openerExtraCharacters >= 0)
		{
			_position += openerExtraCharacters;
			return;
		}

		if (!_openBlockDelimiter.IsEmpty && IsAt(index, _openBlockDelimiter))
		{
			_blockDepth = 1;
			BlockCommentStartIndex = index;
			_commentState |= CommentStates.Block;

			// Consume the opener's remaining characters before closer matching starts. Otherwise
			// the opener's own characters could pair with what follows: for example, the '*' in a
			// "/*" opener with a following '/' would close the comment immediately.
			SetDelimiterRemainder(_openBlockDelimiter.Length - 1);

			return;
		}

		if (!_lineDelimiter.IsEmpty && IsAt(index, _lineDelimiter))
		{
			_commentState |= CommentStates.Line;
			LineCommentStartIndex = index;

			// A line comment runs to the next line terminator and nothing inside it is recognized,
			// so both its end and the position after its content are known here. The terminator
			// itself is consumed by the following move, which also resets the comment state. The end
			// scan is vectorized so a long comment line is not walked character by character.
			int contentStart = index + _lineDelimiter.Length;
			int terminatorIndex = _text[contentStart..].IndexOfAny('\r', '\n');
			int contentEnd = terminatorIndex < 0 ? _text.Length : contentStart + terminatorIndex;

			LineCommentEndIndex = contentEnd;
			_position = contentEnd;
		}
	}

	/// <summary>Determines whether <paramref name="marker"/> begins at <paramref name="index"/>; an empty marker never matches.</summary>
	private readonly bool IsAt(int index, ReadOnlySpan<char> marker)
	{
		return !marker.IsEmpty
			&& index + marker.Length <= _text.Length
			&& _text[index] == marker[0]
			&& _text.Slice(index, marker.Length).SequenceEqual(marker);
	}
}
