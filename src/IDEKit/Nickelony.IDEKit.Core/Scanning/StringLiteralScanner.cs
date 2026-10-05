using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Scanning;

/// <summary>
/// Scans the string-literal styles of <see cref="StringLiteralStyles"/> for the comment scanner, so the dialect approximations live apart
/// from the incremental comment walk.
/// </summary>
/// <remarks>
/// The styles deliberately approximate dialect groups by the quote shape they need rather than parsing, and each exists because its opener or closer
/// shape cannot be expressed by another style. Interpolated string holes are not modeled, so a quote inside a hole can end the string early,
/// and a quote or quote run directly preceded by a backslash opens no string.
/// </remarks>
internal ref struct StringLiteralScanner
{
	private readonly ReadOnlySpan<char> _text;
	private readonly StringLiteralStyles _style;

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

	/// <param name="text">The text to scan.</param>
	/// <param name="style">The string-literal styles the text may use.</param>
	internal StringLiteralScanner(ReadOnlySpan<char> text, StringLiteralStyles style)
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

	/// <summary>Whether the scan currently sits inside a string literal.</summary>
	internal readonly bool IsInString => _state != StringState.None;

	/// <summary>
	/// Whether a closer detected by the most recent move left characters of its closing delimiter pending, which are string content rather than code.
	/// </summary>
	internal readonly bool HasPendingCloserCharacter => _closerCharactersRemaining > 0;

	/// <summary>Consumes one pending character of a closing delimiter.</summary>
	internal void ConsumePendingCloserCharacter() => _closerCharactersRemaining--;

	/// <summary>Tries to open a string literal with the character at <paramref name="index"/>, which the caller read in code.</summary>
	/// <param name="index">The index of the character in code.</param>
	/// <param name="c">The character at <paramref name="index"/>.</param>
	/// <returns>
	/// The number of characters after the first one that the opener consumed, or <c>-1</c> when no opener starts here; an escaped quote
	/// of a configured style returns <c>0</c> and opens nothing.
	/// </returns>
	/// <remarks>
	/// The openers are checked in a fixed precedence order, because a raw string and a single-line quote start with the same character:
	/// raw strings come first. An escaped quote opens nothing, and a long-bracket opener that does not match leaves the character to the caller's
	/// comment checks.
	/// </remarks>
	internal int ScanOpener(int index, char c)
	{
		if (c == '"' && !IsEscapedQuote(index) && (_style & StringLiteralStyles.TripleDoubleQuoted) != 0)
		{
			int run = CountQuoteRun(index, '"');

			if (run >= 3)
			{
				// The C# raw-string rule: a quote run at least as long as the opener closes the string
				// only on its own line, after whitespace. The compiler reports a longer run there as CS8998,
				// and a run at least that long mid-line as CS9000. The scanner closes on the opener-length
				// prefix of either run and rescans the surplus quotes as code, which the compiler does not do.
				_rawStringQuote = '"';
				_rawStringDelimiterLength = run;
				_state = StringState.RawQuote;

				return 0;
			}
		}

		if (c == '\'' && !IsEscapedQuote(index) && (_style & StringLiteralStyles.TripleSingleQuoted) != 0)
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

		if (c == '[' && (_style & StringLiteralStyles.LongBracketQuoted) != 0 && TryMatchLongBracketOpener(index, out int equalsCount))
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
		if (
			c == '@'
			&& (_style & StringLiteralStyles.VerbatimDoubleQuoted) != 0
			&& index + 1 < _text.Length
			&& (_text[index + 1] == '"' || (_text[index + 1] == '$' && index + 2 < _text.Length && _text[index + 2] == '"'))
		)
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

	/// <summary>Consumes a character inside a string literal.</summary>
	/// <param name="index">The index of the character, which the caller's move already consumed.</param>
	/// <param name="c">The character at <paramref name="index"/>.</param>
	/// <returns>
	/// The number of characters after <paramref name="c"/> that this move consumed as well - a quote run inside a raw string
	/// that cannot close it, or a doubled quote inside a verbatim string.
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

	/// <summary>Consumes a character inside a long-bracket string; only a closer matching the opener can end it.</summary>
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

	/// <summary>Consumes a character inside a raw (multi-line) string; only a matching quote run can close it.</summary>
	/// <remarks>
	/// The quote run is measured once from its first quote: a run shorter than the delimiter is skipped in one move, a run of at least the delimiter
	/// length closes the string, and surplus quotes in a longer run are rescanned as code.
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

	/// <summary>Consumes a character inside a single-line string; only the matching quote can close it.</summary>
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

	/// <summary>Consumes a character inside a verbatim string; a doubled quote is an escaped quote, and any other quote closes the string.</summary>
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
	/// Determines whether the character at <paramref name="quoteIndex"/> is escaped by an odd number of immediately preceding backslashes.
	/// </summary>
	/// <remarks>
	/// Only the openers of the raw and single-line quote styles and the closer of a single-line quote call this check. Verbatim strings read
	/// a doubled quote as the escape instead, and raw and long-bracket strings have no backslash escapes.
	/// </remarks>
	private readonly bool IsEscapedQuote(int quoteIndex)
	{
		var probe = new EscapeRunProbe('\\');

		for (int i = quoteIndex - 1; i >= 0 && probe.Consume(_text[i]); i--)
		{ }

		return probe.IsEscaped;
	}

	/// <summary>Counts the maximal run of <paramref name="quote"/> characters starting at <paramref name="index"/>.</summary>
	private readonly int CountQuoteRun(int index, char quote)
	{
		int run = 0;

		while (index + run < _text.Length && _text[index + run] == quote)
			run++;

		return run;
	}

	/// <summary>
	/// Determines whether a long-bracket opener - <c>[</c> followed by any number of <c>=</c> followed by <c>[</c> - begins at the <c>[</c>
	/// at <paramref name="openerIndex"/>.
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
	/// Determines whether a long-bracket closer - <c>]</c> followed by the opener's number of equals signs followed by <c>]</c> -
	/// matching <paramref name="equalsCount"/> begins at the <c>]</c> at <paramref name="closerIndex"/>.
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

	/// <summary>Determines whether the character opens a single-line string under the configured styles.</summary>
	/// <remarks>
	/// Bitwise checks avoid an <c>Enum.HasFlag</c> call in the per-character scan.
	/// </remarks>
	private readonly bool IsQuoteCharacter(char c)
	{
		return (c == '"' && (_style & StringLiteralStyles.DoubleQuoted) != 0)
			|| (c == '\'' && (_style & StringLiteralStyles.SingleQuoted) != 0)
			|| (c == '`' && (_style & StringLiteralStyles.BacktickQuoted) != 0);
	}
}
