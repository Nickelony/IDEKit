namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Identifies a symbol tag as defined by the LSP specification.
/// </summary>
/// <remarks>
/// The numeric values match the protocol values and are serialized as numbers. A value outside the
/// defined range stays representable as an unnamed enum value.
/// </remarks>
public enum SymbolTag
{
	/// <summary>
	/// The symbol is deprecated.
	/// </summary>
	Deprecated = 1
}
