namespace Nickelony.IDEKit.Core.AutoClosing;

/// <summary>Classifies an auto-closing pair for the resolution rules that differ between bracket-like and quote-like pairs.</summary>
/// <remarks>
/// Configured explicitly per pair, not inferred from the token shape, so a pair whose closing text merely starts with its opening
/// token is not misclassified. The token-run rule - a token typed directly after the same token is left to normal text input so runs can be built -
/// applies to quote-like pairs and to any pair whose opening token and closing text coincide.
/// </remarks>
public enum AutoClosingPairKind
{
	/// <summary>
	/// A bracket-like pair; typing its opening token before an unmatched closing text later on the line is left
	/// to normal text input (see <see cref="AutoClosingPair.CheckUnmatchedClosingText"/>), and it uses the bracket character set as its default
	/// auto-close-before gate.
	/// </summary>
	Bracket = 0,

	/// <summary>
	/// A quote-like pair; typing its opening token over existing closing text can skip that text, typing it directly after itself is left
	/// to normal text input, and it can be suppressed after a word character (<see cref="AutoClosingPair.SuppressAfterWordCharacter"/>)
	/// and uses the quote character set as its default auto-close-before gate.
	/// </summary>
	Quote = 1,
}
