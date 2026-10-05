namespace Nickelony.IDEKit.Core.Identifiers;

/// <summary>Determines how an identifier span is located relative to an offset.</summary>
public enum IdentifierSpanMode
{
	/// <summary>
	/// Locates the token at the clamped offset; when the offset is not a token character, the immediately preceding token is preferred when it starts
	/// with a token-start character.
	/// </summary>
	Containing = 0,

	/// <summary>
	/// Locates the token ending at the offset; the character immediately before the offset must be accepted by the part-character rule,
	/// the span never extends past the offset, and the token may begin with a continuation character the policy does not accept as a token start.
	/// </summary>
	EndingAtOffset = 1,

	/// <summary>
	/// Locates the token that contains the character at the clamped offset; when that character is not a token character, no token is reported,
	/// so the preceding-token preference of <see cref="Containing"/> never applies.
	/// </summary>
	AtOffset = 2,
}
