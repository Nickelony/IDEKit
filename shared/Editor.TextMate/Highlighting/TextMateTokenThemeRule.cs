#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Highlighting;
#else
namespace Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
#endif

/// <summary>
/// Describes the visual style associated with one or more TextMate scopes.
/// </summary>
/// <remarks>
/// <para>
/// Instances are commonly produced by deserializing host theme data; the property names and values
/// form that serialized contract and should be kept stable. Deserializers should match property names
/// case-insensitively so that VS Code-style lower-camel names (for example <c>scope</c>,
/// <c>foreground</c>, and <c>fontStyle</c>) resolve to these properties.
/// </para>
/// <para>
/// Only the foreground color and the font traits are applied; a background color is not part of this
/// shape and is ignored.
/// </para>
/// </remarks>
public sealed record TextMateTokenThemeRule
{
	/// <summary>
	/// Gets or initializes the TextMate scope selector or comma-separated selectors matched by the rule.
	/// A blank or whitespace-only scope marks the rule as a theme defaults rule whose values apply to
	/// every token, which is how VS Code theme files carry their base foreground and font style; a
	/// <see langword="null"/> scope, possible through deserialization, is treated the same way.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A selector matches a token scope when the scope equals it or extends it with a dot-separated
	/// suffix. Space-separated selectors describe parent scopes: the rightmost part matches the
	/// innermost scope of the matched path, and the remaining parts must match enclosing scopes in
	/// order, where a part may skip scopes that do not match it and a <c>&gt;</c> child combinator
	/// requires the next part to match the scope that directly encloses the previously matched scope.
	/// </para>
	/// <para>
	/// The exclusion (<c>-</c>), wildcard (<c>*</c>), priority (<c>L:</c>), and parenthesised group
	/// operators belong to injection selectors and are not supported in theme selectors; selectors that
	/// use them, or that place a child combinator without a scope name on each side, never match and
	/// are reported through the resolver's logger.
	/// </para>
	/// </remarks>
	public string Scope { get; init; } = string.Empty;

	/// <summary>
	/// Gets or initializes the foreground color of the rule as a color string. Leave empty to avoid
	/// changing the foreground. Invalid values are ignored and reported through the resolver's logger.
	/// </summary>
	/// <remarks>
	/// The six-digit <c>#RRGGBB</c> and three-digit <c>#RGB</c> forms are read as written. An eight-digit or
	/// four-digit value is always read as the TextMate form - <c>#RRGGBBAA</c> or <c>#RGBA</c> - and
	/// normalized to the platform's <c>#AARRGGBB</c> order before parsing; the platform forms that put the
	/// alpha component first (<c>#AARRGGBB</c> and <c>#ARGB</c>) are <b>not</b> recognized and are
	/// reinterpreted as the TextMate order.
	/// </remarks>
	public string Foreground { get; init; } = string.Empty;

	/// <summary>
	/// Gets or initializes the space-separated font traits of the rule, or <see langword="null"/> to
	/// leave inherited traits unchanged. This property matches the <c>fontStyle</c> field of TextMate
	/// and VS Code theme data.
	/// </summary>
	/// <remarks>
	/// Any present value is an explicit reset first: an empty string clears bold, italic, underline, and
	/// strikethrough, and the recognized trait names then enable individual traits. Recognized values are
	/// case-sensitive: <c>bold</c>, <c>italic</c>, <c>underline</c>, and <c>strikethrough</c>; the
	/// non-standard <c>none</c> keyword is accepted as an explicit spelling of the reset. Unrecognized
	/// values are ignored and reported through the resolver's logger.
	/// </remarks>
	public string? FontStyle { get; init; }
}
