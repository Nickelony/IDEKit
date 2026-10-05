using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

/// <summary>Classifies a <see cref="DocumentSymbol"/>, and a workspace symbol, by its language construct.</summary>
/// <remarks>
/// The numeric values mirror the LSP <c>SymbolKind</c> numbers and start at 1, so zero is not a member and <c>default(DocumentSymbolKind)</c>
/// is not a valid kind. The type stays protocol-independent otherwise: hosts map a member onto their own symbol or icon model, and the protocol
/// bridge in <c>Nickelony.LanguageServer.Provider</c> maps each protocol kind to its member explicitly.
/// </remarks>
public enum DocumentSymbolKind
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

	[SuppressMessage("Design", "CA1720:Identifier contains type name", Justification = "The member name mirrors the LSP SymbolKind contract.")]
	String = 15,

	Number = 16,

	Boolean = 17,

	Array = 18,

	[SuppressMessage("Design", "CA1720:Identifier contains type name", Justification = "The member name mirrors the LSP SymbolKind contract.")]
	Object = 19,

	Key = 20,

	Null = 21,

	EnumMember = 22,

	Struct = 23,

	Event = 24,

	Operator = 25,

	TypeParameter = 26,
}
