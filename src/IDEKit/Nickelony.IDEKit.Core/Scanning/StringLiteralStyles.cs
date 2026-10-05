namespace Nickelony.IDEKit.Core.Scanning;

/// <summary>Specifies which string-literal styles are recognized while scanning for comments.</summary>
/// <remarks>
/// Members are flags and combine for languages that support several quote characters,
/// for example <c>DoubleQuoted | SingleQuoted | BacktickQuoted</c> for JavaScript; the backtick, triple-quoted, long-bracket,
/// and verbatim double-quoted styles describe multi-line strings. A multi-line string that never closes keeps the scan inside the string to the end
/// of the text, so comment delimiters after it are not recognized.
/// </remarks>
[Flags]
public enum StringLiteralStyles
{
	/// <summary>
	/// No string awareness; any delimiter occurrence starts a comment, matching INI-like formats and languages without string literals.
	/// </summary>
	None = 0,

	/// <summary>Double-quoted strings with backslash escapes.</summary>
	/// <remarks>
	/// C# verbatim strings need the separate <see cref="VerbatimDoubleQuoted"/> style, because they disable backslash escapes and use a doubled quote
	/// as the escape.
	/// </remarks>
	DoubleQuoted = 1 << 0,

	/// <summary>Single-quoted strings with backslash escapes.</summary>
	SingleQuoted = 1 << 1,

	/// <summary>Backtick-quoted strings with backslash escapes.</summary>
	/// <remarks>
	/// These strings may span lines. A backslash escapes the closing quote (the JavaScript convention); languages with different escaping rules,
	/// such as Go raw strings, are approximated.
	/// </remarks>
	BacktickQuoted = 1 << 2,

	/// <summary>Multi-line strings delimited by a run of three or more double quotes.</summary>
	/// <remarks>
	/// The opening quote run is the delimiter and the string closes on a run of at least that length, so content that must contain a three-quote
	/// sequence uses a longer opener. Fixed-three dialects are approximated, and backslash escapes are not processed except that a quote run directly
	/// preceded by a backslash is not recognized as an opener.
	/// </remarks>
	TripleDoubleQuoted = 1 << 3,

	/// <summary>Multi-line strings delimited by exactly three single quotes.</summary>
	/// <remarks>
	/// The delimiter is exactly three quotes; surplus quotes in the opening run belong to the string, which closes on the first run of three
	/// or more quotes. Backslash escapes are not processed, and a surplus quote in the closing run is rescanned as code, so recovery can hide
	/// a trailing comment; valid Python does not produce that shape.
	/// </remarks>
	TripleSingleQuoted = 1 << 4,

	/// <summary>
	/// Multi-line strings delimited by Lua-style long brackets: <c>[[ ... ]]</c>, <c>[=[ ... ]=]</c>, and so on, where the opener and closer carry
	/// the same number of equals signs.
	/// </summary>
	/// <remarks>
	/// These strings may span lines and process no escape sequences. Lua's long-bracket comments (<c>--[==[ ... ]==]</c>) cannot be expressed
	/// through <see cref="CommentSyntax"/> delimiters; only a fixed comment form such as <c>--[[</c> can be configured.
	/// </remarks>
	LongBracketQuoted = 1 << 5,

	/// <summary>
	/// Verbatim double-quoted strings preceded by an at sign (<c>@"..."</c> and the interpolated <c>$@"..."</c> and <c>@$"..."</c> orders),
	/// where a doubled quote is an escaped quote, backslashes are literal characters, and the string may span lines.
	/// </summary>
	/// <remarks>
	/// The at sign precedes the quote, and the dollar sign may come before or after the at sign. Interpolation holes are not modeled in any style:
	/// a quote inside a hole can end the string early.
	/// </remarks>
	VerbatimDoubleQuoted = 1 << 6,
}
