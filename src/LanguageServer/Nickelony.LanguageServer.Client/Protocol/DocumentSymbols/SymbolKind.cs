using System.Diagnostics.CodeAnalysis;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Describes the kind of a document-symbol entry, using the LSP <c>SymbolKind</c> mapping.</summary>
/// <remarks>
/// The numeric values match the protocol values and are serialized as numbers. A value outside the defined range stays representable as an unnamed
/// enum value so a newer server does not invalidate a response. Hosts that map the kind onto a library taxonomy choose their own fallback,
/// because the protocol defines no default symbol kind.
/// </remarks>
public enum SymbolKind
{
	File = 1,

	Module = 2,

	Namespace = 3,

	Package = 4,

	Class = 5,

	Method = 6,

	Property = 7,

	Field = 8,

	Constructor = 9,

	Enum = 10,

	Interface = 11,

	Function = 12,

	Variable = 13,

	Constant = 14,

	[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The member name mirrors the LSP SymbolKind wire contract.")]
	String = 15,

	Number = 16,

	Boolean = 17,

	Array = 18,

	[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The member name mirrors the LSP SymbolKind wire contract.")]
	Object = 19,

	Key = 20,

	Null = 21,

	EnumMember = 22,

	Struct = 23,

	Event = 24,

	Operator = 25,

	TypeParameter = 26,
}
