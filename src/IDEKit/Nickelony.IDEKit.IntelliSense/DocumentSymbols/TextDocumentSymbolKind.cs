using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.IntelliSense.DocumentSymbols;

/// <summary>
/// Classifies a <see cref="TextDocumentSymbol"/> by its language construct.
/// </summary>
/// <remarks>
/// The members mirror the LSP <c>SymbolKind</c> values one-to-one, with the protocol numerics as
/// their values, but the type stays protocol-independent: hosts map a member onto their own symbol
/// or icon model. Zero is not a member, so <c>default(TextDocumentSymbolKind)</c> is not a valid
/// kind. The protocol bridge lives with the protocol boundary in
/// <c>Nickelony.LanguageServer.Client</c>.
/// </remarks>
public enum TextDocumentSymbolKind
{
	/// <summary>A file symbol.</summary>
	File = 1,

	/// <summary>A module symbol.</summary>
	Module = 2,

	/// <summary>A namespace symbol.</summary>
	Namespace = 3,

	/// <summary>A package symbol.</summary>
	Package = 4,

	/// <summary>A class symbol.</summary>
	Class = 5,

	/// <summary>A method symbol.</summary>
	Method = 6,

	/// <summary>A property symbol.</summary>
	Property = 7,

	/// <summary>A field symbol.</summary>
	Field = 8,

	/// <summary>A constructor symbol.</summary>
	Constructor = 9,

	/// <summary>An enum symbol.</summary>
	Enum = 10,

	/// <summary>An interface symbol.</summary>
	Interface = 11,

	/// <summary>A function symbol.</summary>
	Function = 12,

	/// <summary>A variable symbol.</summary>
	Variable = 13,

	/// <summary>A constant symbol.</summary>
	Constant = 14,

	/// <summary>A string symbol.</summary>
	[SuppressMessage(
		"Design",
		"CA1720:Identifier contains type name",
		Justification = "The member name mirrors the LSP SymbolKind contract.")]
	String = 15,

	/// <summary>A number symbol.</summary>
	Number = 16,

	/// <summary>A boolean symbol.</summary>
	Boolean = 17,

	/// <summary>An array symbol.</summary>
	Array = 18,

	/// <summary>An object symbol.</summary>
	[SuppressMessage(
		"Design",
		"CA1720:Identifier contains type name",
		Justification = "The member name mirrors the LSP SymbolKind contract.")]
	Object = 19,

	/// <summary>A key symbol.</summary>
	Key = 20,

	/// <summary>A <see langword="null"/> symbol.</summary>
	Null = 21,

	/// <summary>An enum member symbol.</summary>
	EnumMember = 22,

	/// <summary>A struct symbol.</summary>
	Struct = 23,

	/// <summary>An event symbol.</summary>
	Event = 24,

	/// <summary>An operator symbol.</summary>
	Operator = 25,

	/// <summary>A type parameter symbol.</summary>
	TypeParameter = 26
}
