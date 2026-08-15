namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Specifies which string-literal styles are recognized while scanning for comments.
/// </summary>
/// <remarks>
/// <para>
/// Members are flags and combine for languages that support several quote characters, for example
/// <c>DoubleQuoted | SingleQuoted | BacktickQuoted</c> for JavaScript. The backtick,
/// triple-quoted, and long-bracket styles describe multi-line strings. A multi-line string that
/// never closes keeps the scan inside the string to the end of the text, so comment delimiters
/// after it are not recognized.
/// </para>
/// <para>
/// A member describes the quote shape it scans and names the dialect it models; when the shape
/// cannot match every dialect exactly, the member documents the approximation it accepts (for
/// example fixed three-quote strings for Java text blocks).
/// </para>
/// </remarks>
[Flags]
public enum StringLiteralStyle
{
	/// <summary>
	/// No string awareness; any delimiter occurrence starts a comment.
	/// This matches INI-like formats and languages without string literals.
	/// </summary>
	None = 0,

	/// <summary>
	/// Double-quoted strings with backslash escapes, as in the C family (C, C++, C#, Java).
	/// </summary>
	/// <remarks>
	/// C# verbatim strings need the separate <see cref="VerbatimDoubleQuoted"/> style, because they
	/// disable backslash escapes and use a doubled quote as the escape.
	/// </remarks>
	DoubleQuoted = 1 << 0,

	/// <summary>
	/// Single-quoted strings with backslash escapes, as in Python, JavaScript, and Lua.
	/// </summary>
	SingleQuoted = 1 << 1,

	/// <summary>
	/// Backtick-quoted strings, as in JavaScript template literals. These strings may span lines.
	/// </summary>
	/// <remarks>
	/// A backslash escapes the closing quote (the JavaScript convention); languages with different
	/// escaping rules, such as Go raw strings, are approximated.
	/// </remarks>
	BacktickQuoted = 1 << 2,

	/// <summary>
	/// Multi-line strings delimited by a run of three or more double quotes, as in C# raw string
	/// literals.
	/// </summary>
	/// <remarks>
	/// Models the C# raw-string rule: the opening quote run is the delimiter and the string closes
	/// on a run of at least that length, so content that must contain a three-quote sequence uses a
	/// longer opener. Fixed-three dialects (Java text blocks, Kotlin raw strings, Swift multi-line
	/// strings, and Python's triple-double-quoted strings) are approximated: the rule matches while
	/// the opening run is exactly three quotes and diverges for longer runs. Backslash escapes are
	/// not processed in this scanning mode, with one exception: a quote run directly preceded by a
	/// backslash is not recognized as an opener.
	/// </remarks>
	TripleDoubleQuoted = 1 << 3,

	/// <summary>
	/// Multi-line strings delimited by exactly three single quotes, as in Python docstrings.
	/// </summary>
	/// <remarks>
	/// Models the Python rule: the delimiter is exactly three quotes, surplus quotes in the opening
	/// run belong to the string content, and the string closes on the first run of three or more
	/// quotes. Backslash escapes are not processed; Python's escaped quotes inside such strings are
	/// not modeled, and an opener directly preceded by a backslash is not recognized. When a closing run carries a surplus quote, that quote is rescanned as code, so
	/// a sequence such as <c>'''a'''' # note</c> can open a single-quoted string whose content
	/// hides the trailing comment; valid Python does not produce that shape, so only recovery
	/// behavior is affected.
	/// </remarks>
	TripleSingleQuoted = 1 << 4,

	/// <summary>
	/// Multi-line strings delimited by Lua-style long brackets: <c>[[ ... ]]</c>,
	/// <c>[=[ ... ]=]</c>, and so on, where the opener and closer carry the same number
	/// of equals signs.
	/// </summary>
	/// <remarks>
	/// These strings may span lines and process no escape sequences. Lua's long-bracket comments
	/// (<c>--[==[ ... ]==]</c>) cannot be expressed through <see cref="CommentSyntax"/> delimiters;
	/// only a fixed comment form such as <c>--[[</c> can be configured.
	/// </remarks>
	LongBracketQuoted = 1 << 5,

	/// <summary>
	/// Verbatim double-quoted strings preceded by an at sign, as in C# (<c>@"..."</c> and the
	/// interpolated <c>$@"..."</c> and <c>@$"..."</c> orders), where a doubled quote is an escaped
	/// quote and backslashes are literal characters. These strings may span lines.
	/// </summary>
	/// <remarks>
	/// Both interpolation orders are recognized through the at sign. Interpolation holes are not
	/// modeled in any style: a quote inside a hole can end the string early.
	/// </remarks>
	VerbatimDoubleQuoted = 1 << 6,
}
