namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Describes an additional annotation the server attached to a completion item, using the LSP <c>CompletionItemTag</c> mapping.</summary>
/// <remarks>
/// The numeric values match the protocol values and are serialized as numbers. A value outside the defined range stays representable
/// as an unnamed enum value; a host that does not recognize a tag ignores it.
/// </remarks>
public enum CompletionItemTag
{
	Deprecated = 1,
}
