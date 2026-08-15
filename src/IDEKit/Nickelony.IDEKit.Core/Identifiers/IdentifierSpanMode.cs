namespace Nickelony.IDEKit.Core.Identifiers;

/// <summary>
/// Determines how an identifier span is located relative to an offset.
/// </summary>
public enum IdentifierSpanMode
{
	/// <summary>
	/// Locates the token at the clamped offset. When the offset itself is not a token character,
	/// the immediately preceding token is preferred when it starts with a token-start character.
	/// </summary>
	Containing = 0,

	/// <summary>
	/// Locates a token ending at the offset (the word being typed). The character immediately before
	/// the offset must be accepted by the part-character rule, and the span never extends past the
	/// offset. The resolved token may begin with a continuation character that the policy does not
	/// accept as a token start.
	/// </summary>
	EndingAtOffset = 1
}
