namespace Nickelony.IDEKit.Core.AutoClosing;

/// <summary>
/// Classifies an auto-closing pair for the resolution rules that differ between bracket-like and
/// quote-like pairs.
/// </summary>
/// <remarks>
/// <para>
/// The kind is configured explicitly per pair; it is not inferred from the shape of the tokens, so an
/// unrelated pair whose closing text happens to start with its opening token is not misclassified.
/// </para>
/// <para>
/// The token-run rule - typing a token directly after the same token is left to normal text input so
/// runs can be built - applies to every quote-like pair and to any pair whose opening token and closing
/// text coincide, whatever its kind. A symmetric bracket-like pair (for example <c>|</c>) therefore
/// follows the quote rule rather than appending a third token.
/// </para>
/// </remarks>
public enum TextAutoClosingPairKind
{
	/// <summary>
	/// A bracket-like pair (for example parentheses, braces, or brackets). Typing the opening token in
	/// front of its closing text is left to normal text input, and the pair uses the bracket character set
	/// as its default auto-close-before gate.
	/// </summary>
	Bracket = 0,

	/// <summary>
	/// A quote-like pair (for example double quotes, single quotes, or backticks). Typing the opening
	/// token over existing closing text can skip that text; typing the same token directly after the
	/// token is left to normal text input; the pair can be suppressed after a word character (see
	/// <see cref="TextAutoClosingPair.SuppressAfterWordCharacter"/>); and it uses the quote character
	/// set as its default auto-close-before gate.
	/// </summary>
	Quote = 1
}
