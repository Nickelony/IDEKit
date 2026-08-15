using System.Diagnostics.CodeAnalysis;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Describes the kind of a document-symbol entry, using the LSP <c>SymbolKind</c> mapping.
/// </summary>
/// <remarks>
/// The numeric values match the protocol values and are serialized as numbers. A value outside the
/// defined range stays representable as an unnamed enum value so a newer server does not invalidate a
/// response; hosts that map the kind onto a library taxonomy choose their own fallback because the
/// protocol defines no default symbol kind.
/// </remarks>
public enum SymbolKind
{
	/// <summary>
	/// A file symbol.
	/// </summary>
	File = 1,

	/// <summary>
	/// A module symbol.
	/// </summary>
	Module = 2,

	/// <summary>
	/// A namespace symbol.
	/// </summary>
	Namespace = 3,

	/// <summary>
	/// A package symbol.
	/// </summary>
	Package = 4,

	/// <summary>
	/// A class symbol.
	/// </summary>
	Class = 5,

	/// <summary>
	/// A method symbol.
	/// </summary>
	Method = 6,

	/// <summary>
	/// A property symbol.
	/// </summary>
	Property = 7,

	/// <summary>
	/// A field symbol.
	/// </summary>
	Field = 8,

	/// <summary>
	/// A constructor symbol.
	/// </summary>
	Constructor = 9,

	/// <summary>
	/// An enum symbol.
	/// </summary>
	Enum = 10,

	/// <summary>
	/// An interface symbol.
	/// </summary>
	Interface = 11,

	/// <summary>
	/// A function symbol.
	/// </summary>
	Function = 12,

	/// <summary>
	/// A variable symbol.
	/// </summary>
	Variable = 13,

	/// <summary>
	/// A constant symbol.
	/// </summary>
	Constant = 14,

	/// <summary>
	/// A string symbol.
	/// </summary>
	[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The member name mirrors the LSP SymbolKind wire contract.")]
	String = 15,

	/// <summary>
	/// A number symbol.
	/// </summary>
	Number = 16,

	/// <summary>
	/// A boolean symbol.
	/// </summary>
	Boolean = 17,

	/// <summary>
	/// An array symbol.
	/// </summary>
	Array = 18,

	/// <summary>
	/// An object symbol.
	/// </summary>
	[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The member name mirrors the LSP SymbolKind wire contract.")]
	Object = 19,

	/// <summary>
	/// A key symbol.
	/// </summary>
	Key = 20,

	/// <summary>
	/// A null symbol.
	/// </summary>
	Null = 21,

	/// <summary>
	/// An enum member symbol.
	/// </summary>
	EnumMember = 22,

	/// <summary>
	/// A struct symbol.
	/// </summary>
	Struct = 23,

	/// <summary>
	/// An event symbol.
	/// </summary>
	Event = 24,

	/// <summary>
	/// An operator symbol.
	/// </summary>
	Operator = 25,

	/// <summary>
	/// A type parameter symbol.
	/// </summary>
	TypeParameter = 26
}
