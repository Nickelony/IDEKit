namespace Nickelony.IDEKit.TextMate;

/// <summary>Describes the visual style associated with one or more TextMate scopes.</summary>
/// <remarks>
/// <para>
/// Instances are commonly produced by deserializing host theme data; the property names and values form that serialized
/// contract and should be kept stable. Deserializers should match property names case-insensitively so that VS Code-style lower-camel names
/// (for example <c>scope</c>, <c>foreground</c>, and <c>fontStyle</c>) resolve to these properties.
/// </para>
/// <para>
/// Only the foreground color and the font traits are applied; a background color is not part of this shape and is ignored.
/// </para>
/// </remarks>
public sealed record TextMateTokenThemeRule
{
	/// <summary>The TextMate scope selector or comma-separated selectors matched by the rule.</summary>
	/// <remarks>
	/// <para>
	/// A <see langword="null"/> scope from deserialized data, or one that is blank or whitespace-only once its leading and trailing
	/// commas are trimmed (for example <c>,</c>), makes the rule a theme defaults rule whose values apply to every token. VS Code theme files carry
	/// their base foreground and font style this way.
	/// </para>
	/// <para>
	/// A selector matches a token scope when the scope equals it or extends it with a dot-separated suffix.
	/// Space-separated selectors describe parent scopes: the rightmost part matches the innermost scope of the matched path, and the remaining parts
	/// must match enclosing scopes in order, where a part may skip scopes that do not match it and a <c>&gt;</c> child combinator requires the next
	/// part to match the scope that directly encloses the last matched scope.
	/// </para>
	/// <para>
	/// The exclusion (<c>-</c>), wildcard (<c>*</c>), priority (<c>L:</c>), and parenthesized group operators belong to injection selectors
	/// and are not supported in theme selectors; selectors that use them, or that place a child combinator without a scope name on each side,
	/// never match and are reported through the resolver's logger.
	/// </para>
	/// </remarks>
	public string? Scope { get; init; } = string.Empty;

	/// <summary>
	/// The foreground color of the rule as a color string; an empty value leaves the foreground unchanged, and an invalid value is ignored
	/// and reported through the resolver's logger.
	/// </summary>
	/// <remarks>
	/// The recognized forms are the six-digit <c>#RRGGBB</c>, the three-digit <c>#RGB</c>, the eight-digit <c>#RRGGBBAA</c>,
	/// and the four-digit <c>#RGBA</c>; the alpha component comes last, as TextMate and VS Code theme data spell it. An eight-digit or four-digit
	/// value is always read in that order, so a value that means <c>#AARRGGBB</c> or <c>#ARGB</c> is not read that way: the same digits are read
	/// with the alpha component last. A color name or any other value is invalid.
	/// </remarks>
	public string Foreground { get; init; } = string.Empty;

	/// <summary>
	/// The space-separated font traits of the rule, or <see langword="null"/> to leave inherited traits unchanged; the value maps
	/// to the <c>fontStyle</c> field of TextMate and VS Code theme data.
	/// </summary>
	/// <remarks>
	/// Any present value is an explicit reset first: an empty string clears bold, italic, underline, and strikethrough, and the recognized trait
	/// names then enable individual traits. Recognized values are case-sensitive: <c>bold</c>, <c>italic</c>, <c>underline</c>, <c>strikethrough</c>;
	/// the non-standard <c>none</c> keyword is another spelling of the reset. Unrecognized values are ignored and reported
	/// through the resolver's logger.
	/// </remarks>
	public string? FontStyle { get; init; }
}
