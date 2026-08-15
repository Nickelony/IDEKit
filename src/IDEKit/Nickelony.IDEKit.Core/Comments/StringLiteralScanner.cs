using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Scans the string-literal styles of <see cref="StringLiteralStyle"/> for the comment scanner, so the
/// dialect approximations live apart from the incremental comment walk.
/// </summary>
/// <remarks>
/// <para>
/// The models are deliberate approximations of three dialect groups rather than parsers: the
/// C-family styles (single/double quoted with backslash escapes, raw strings, verbatim strings), the
/// Python styles (fixed triple quotes, single quotes), and the Lua/JavaScript styles (long brackets,
/// backticks). Each one exists because its opener or closer shape cannot be expressed by another
/// style; a dialect that reuses an existing shape needs no flag at all. That is the cap on adding
/// flags: a new <see cref="StringLiteralStyle"/> flag must come with the opener and body rules here,
/// and a flag without rules is a silent no-op.
/// </para>
/// <para>
/// Nothing here models interpolated string holes, so a quote inside a hole can end the string early,
/// and a quote or quote run directly preceded by a backslash does not open a string in any style.
/// </para>
/// <para>
/// The scanner owns the string state and reports what one move consumed; the caller owns the
/// position, so the walk stays in <see cref="CommentScanner"/> and this type only answers the
/// dialect questions.
/// </para>
/// </remarks>
internal ref struct StringLiteralScanner
{
	private readonly ReadOnlySpan<char> _text;
	private readonly StringLiteralStyle _style;

	// The scanner sits in exactly one string state; the fields below it are that state's parameters and
	// are read only while it is active. IsInString and the dispatch derive from the one state value, so
	// a new string style adds a state instead of another term to a hand-maintained disjunction.
	private StringState _state;
	private char _quoteChar;
	private char _rawStringQuote;
	private int _rawStringDelimiterLength;
	private int _longBracketEqualsCount;
	private int _closerCharactersRemaining;

	// The string-literal states; only one is active at a time.
	private enum StringState
	{
		// Not inside a string.
		None,
		// Inside a single-line quote (_quoteChar).
		Quote,
		// Inside a raw or triple-quoted string (_rawStringQuote, _rawStringDelimiterLength).
		RawQuote,
		// Inside a long-bracket string (_longBracketEqualsCount).
		LongBracket,
		// Inside a verbatim string.
		Verbatim,
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="StringLiteralScanner"/> struct.
	/// </summary>
	/// <param name="text">The text to scan.</param>
	/// <param name="style">The string-literal styles the text may use.</param>
	internal StringLiteralScanner(ReadOnlySpan<char> text, StringLiteralStyle style)
	{
		_text = text;
		_style = style;
		_state = StringState.None;
		_quoteChar = '\0';
		_rawStringQuote = '\0';
		_rawStringDelimiterLength = 0;
		_longBracketEqualsCount = -1;
		_closerCharactersRemaining = 0;
	}

	/// <summary>
	/// Gets a value indicating whether the scan currently sits inside a string literal.
	/// </summary>
	internal readonly bool IsInString => _state != StringState.None;

	/// <summary>
	/// Gets a value indicating whether a closer detected by the most recent move left characters of
	/// its closing delimiter pending, which are string content rather than code.
	/// </summary>
	internal readonly bool HasPendingCloserCharacter => _closerCharactersRemaining > 0;

	/// <summary>
	/// Consumes one pending character of a closing delimiter.
	/// </summary>
	internal void ConsumePendingCloserCharacter() => _closerCharactersRemaining--;

	/// <summary>
	/// Tries to open a string literal with the character at <paramref name="index"/>, which the caller
	/// read in code.
	/// </summary>
	/// <remarks>
	/// The openers are checked in a fixed precedence order, because several of them start with the
	/// same character: raw strings before single-line quotes, and the verbatim forms before a plain
	/// quote. An escaped quote opens nothing, and a long-bracket opener that does not match leaves the
	/// character to the caller's comment checks.
	/// </remarks>
	/// <param name="index">The index of the character in code.</param>
	/// <param name="c">The character at <paramref name="index"/>.</param>
	/// <returns>
	/// The number of characters after the first one that the opener consumed, or <c>-1</c> when no
	/// opener starts here.
	/// </returns>
	internal int TryOpen(int index, char c)
	{
		if (c == '"' && !IsEscapedQuote(index) && (_style & StringLiteralStyle.TripleDoubleQuoted) != 0)
		{
			int run = CountQuoteRun(index, '"');

			if (run >= 3)
			{
				// The C# raw-string rule: the whole opening quote run is the delimiter, and the string
				// closes on a run of at least that length. A closing run longer than the opener is
				// invalid (CS8998); the surplus quotes are re-scanned, so an odd surplus count leaves
				// the scan inside a string until quote parity realigns.
				_rawStringQuote = '"';
				_rawStringDelimiterLength = run;
				_state = StringState.RawQuote;

				return 0;
			}
		}

		if (c == '\'' && !IsEscapedQuote(index) && (_style & StringLiteralStyle.TripleSingleQuoted) != 0)
		{
			int run = CountQuoteRun(index, '\'');

			if (run >= 3)
			{
				// The Python rule: the delimiter is exactly three quotes. The opener consumes the
				// first three quotes, and surplus quotes in the opening run belong to the string
				// content, so a run such as '''' opens a string whose content begins with a quote.
				_rawStringQuote = '\'';
				_rawStringDelimiterLength = 3;
				_state = StringState.RawQuote;

				return 2;
			}
		}

		if (c == '[' && (_style & StringLiteralStyle.LongBracketQuoted) != 0 && TryMatchLongBracketOpener(index, out int equalsCount))
		{
			// A long-bracket opener is '[' followed by any number of '=' followed by '['. Only the
			// first character is consumed here; the rest arrives one character at a time.
			_longBracketEqualsCount = equalsCount;
			_state = StringState.LongBracket;

			return 0;
		}

		// A verbatim string opens with an at sign followed by '"': @"..." and the interpolated
		// $@"..." and @$"..." orders. The opener is consumed in one move so the next move starts
		// on the first content character; the dollar sign is part of the opener and has no other
		// effect because interpolation holes are not modeled. ($"@..." is not an interpolation
		// order: the at sign follows the opening quote and is string content.)
		if (c == '@' && (_style & StringLiteralStyle.VerbatimDoubleQuoted) != 0
			&& index + 1 < _text.Length
			&& (_text[index + 1] == '"'
				|| (_text[index + 1] == '$' && index + 2 < _text.Length && _text[index + 2] == '"')))
		{
			_state = StringState.Verbatim;

			return _text[index + 1] == '$' ? 2 : 1;
		}

		if (IsQuoteCharacter(c))
		{
			if (!IsEscapedQuote(index))
			{
				_quoteChar = c;
				_state = StringState.Quote;
			}

			return 0;
		}

		return -1;
	}

	/// <summary>
	/// Consumes a character inside a string literal.
	/// </summary>
	/// <param name="index">The index of the character, which the caller's move already consumed.</param>
	/// <param name="c">The character at <paramref name="index"/>.</param>
	/// <returns>
	/// The number of characters after <paramref name="c"/> that this move consumed as well - a quote
	/// run inside a raw string that cannot close it, or a doubled quote inside a verbatim string.
	/// </returns>
	internal int Consume(int index, char c)
	{
		if (_state == StringState.LongBracket)
			return ConsumeInLongBracket(index, c);

		if (_state == StringState.RawQuote)
			return ConsumeInRawString(index, c);

		if (_state == StringState.Verbatim)
			return ConsumeInVerbatimString(index, c);

		return ConsumeInQuotedString(index, c);
	}

	/// <summary>
	/// Consumes a character inside a long-bracket string; only a closer matching the opener can end it.
	/// </summary>
	private int ConsumeInLongBracket(int index, char c)
	{
		// A closer is a ']', the opener's equals count, and another ']'
		// (with zero equals, the closer is "]]").
		if (c == ']' && TryMatchLongBracketCloser(index, _longBracketEqualsCount, out int closerLength))
		{
			_longBracketEqualsCount = -1;
			_state = StringState.None;

			// Consume the remainder of the detected closer as string content, not code.
			_closerCharactersRemaining = closerLength - 1;
		}

		return 0;
	}

	/// <summary>
	/// Consumes a character inside a raw (multi-line) string; only a matching quote run can close it.
	/// </summary>
	/// <remarks>
	/// The quote run is measured once from its first quote. A run shorter than the delimiter is
	/// skipped in one move (every quote is content), so a long run costs one scan instead of a
	/// rescan per quote. A run of at least the delimiter length closes the string, and any surplus
	/// quotes in a longer run are rescanned as code.
	/// </remarks>
	private int ConsumeInRawString(int index, char c)
	{
		// Backslashes are content, not escapes.
		if (c != _rawStringQuote)
			return 0;

		int run = CountQuoteRun(index, _rawStringQuote);

		if (run >= _rawStringDelimiterLength)
		{
			_rawStringQuote = '\0';
			_state = StringState.None;

			// Consume the remainder of the detected closer as string content, not code.
			_closerCharactersRemaining = _rawStringDelimiterLength - 1;

			return 0;
		}

		// A run shorter than the delimiter is content; skip it without rescanning it from every
		// quote it contains.
		return run - 1;
	}

	/// <summary>
	/// Consumes a character inside a single-line string; only the matching quote can close it.
	/// </summary>
	private int ConsumeInQuotedString(int index, char c)
	{
		// The string cannot continue onto the next line, except for a backtick string
		// (JavaScript template literals), which spans lines.
		if (LineTerminators.IsTerminator(c) && _quoteChar != '`')
		{
			_quoteChar = '\0';
			_state = StringState.None;
		}
		else if (c == _quoteChar && !IsEscapedQuote(index))
		{
			_quoteChar = '\0';
			_state = StringState.None;
		}

		return 0;
	}

	/// <summary>
	/// Consumes a character inside a verbatim string; a doubled quote is an escaped quote, and any
	/// other quote closes the string.
	/// </summary>
	private int ConsumeInVerbatimString(int index, char c)
	{
		if (c != '"')
			return 0;

		// A doubled quote is content; consume the second quote with the first so it cannot be
		// mistaken for a closer.
		if (index + 1 < _text.Length && _text[index + 1] == '"')
			return 1;

		_state = StringState.None;

		return 0;
	}

	/// <summary>
	/// Determines whether the character at <paramref name="quoteIndex"/> is escaped by an odd number
	/// of immediately preceding backslashes.
	/// </summary>
	/// <remarks>
	/// Backslashes are the escape character of every string style this scanner approximates: the
	/// C-family, Python, and Lua styles read a doubled quote in a verbatim string instead, which the
	/// verbatim path handles separately.
	/// </remarks>
	private readonly bool IsEscapedQuote(int quoteIndex)
	{
		var probe = new EscapeRunProbe('\\');

		for (int i = quoteIndex - 1; i >= 0 && probe.Consume(_text[i]); i--)
		{
		}

		return probe.IsEscaped;
	}

	/// <summary>
	/// Counts the maximal run of <paramref name="quote"/> characters starting at <paramref name="index"/>.
	/// </summary>
	private readonly int CountQuoteRun(int index, char quote)
	{
		int run = 0;

		while (index + run < _text.Length && _text[index + run] == quote)
			run++;

		return run;
	}

	/// <summary>
	/// Determines whether a long-bracket opener begins at the <c>[</c> at <paramref name="openerIndex"/>.
	/// The opener is <c>[</c> followed by any number of <c>=</c> followed by <c>[</c>, for example
	/// <c>[[</c>, <c>[=[</c>, or <c>[==[</c>.
	/// </summary>
	/// <param name="openerIndex">The index of the <c>[</c> that may start a long-bracket opener.</param>
	/// <param name="equalsCount">Receives the number of equals signs (<c>0</c> for <c>[[</c>).</param>
	/// <returns><see langword="true"/> when a long-bracket opener begins at the index.</returns>
	private readonly bool TryMatchLongBracketOpener(int openerIndex, out int equalsCount)
	{
		equalsCount = 0;
		int index = openerIndex + 1;

		while (index < _text.Length && _text[index] == '=')
		{
			equalsCount++;
			index++;
		}

		return index < _text.Length && _text[index] == '[';
	}

	/// <summary>
	/// Determines whether a long-bracket closer matching <paramref name="equalsCount"/> begins at the
	/// <c>]</c> at <paramref name="closerIndex"/>. The closer is <c>]</c> followed by the opener's
	/// number of equals signs followed by <c>]</c>.
	/// </summary>
	/// <param name="closerIndex">The index of the <c>]</c> that may start a long-bracket closer.</param>
	/// <param name="equalsCount">The opener's number of equals signs the closer must match.</param>
	/// <param name="closerLength">Receives the full closer length when the closer matches.</param>
	/// <returns><see langword="true"/> when a matching long-bracket closer begins at the index.</returns>
	private readonly bool TryMatchLongBracketCloser(int closerIndex, int equalsCount, out int closerLength)
	{
		int index = closerIndex + 1;
		int actualEquals = 0;

		while (index < _text.Length && _text[index] == '=')
		{
			actualEquals++;
			index++;
		}

		if (actualEquals != equalsCount)
		{
			closerLength = 0;
			return false;
		}

		if (index < _text.Length && _text[index] == ']')
		{
			closerLength = 2 + equalsCount;
			return true;
		}

		closerLength = 0;
		return false;
	}

	/// <summary>
	/// Determines whether the character opens a single-line string under the configured styles.
	/// </summary>
	/// <remarks>Bitwise checks avoid an <c>Enum.HasFlag</c> call in the per-character scan.</remarks>
	private readonly bool IsQuoteCharacter(char c)
		=> (c == '"' && (_style & StringLiteralStyle.DoubleQuoted) != 0)
		|| (c == '\'' && (_style & StringLiteralStyle.SingleQuoted) != 0)
		|| (c == '`' && (_style & StringLiteralStyle.BacktickQuoted) != 0);
}
