namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Describes an annotation on a document symbol, using the LSP <c>SymbolTag</c> mapping.</summary>
/// <remarks>
/// The numeric values match the protocol values and are serialized as numbers. A value outside the defined range stays representable as an unnamed
/// enum value.
/// </remarks>
public enum SymbolTag
{
	Deprecated = 1,
}
