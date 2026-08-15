namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Describes an additional annotation the server attached to a completion item, using the LSP
/// <c>CompletionItemTag</c> mapping.
/// </summary>
/// <remarks>
/// The numeric values match the protocol values and are serialized as numbers. A value outside the
/// defined range stays representable as an unnamed enum value; a host that does not recognize a
/// tag ignores it.
/// </remarks>
public enum CompletionItemTag
{
	/// <summary>
	/// The item is deprecated; hosts conventionally render it with a strikethrough while keeping it
	/// selectable and committable.
	/// </summary>
	Deprecated = 1
}
