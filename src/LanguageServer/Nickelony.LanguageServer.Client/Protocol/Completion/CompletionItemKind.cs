namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Describes the kind of a completion item, using the LSP <c>CompletionItemKind</c> mapping.</summary>
/// <remarks>
/// The numeric values match the protocol values and are serialized as numbers. A value outside the defined range stays representable
/// as an unnamed enum value; the protocol defines no default kind, so hosts that map the kind onto a library taxonomy choose their own fallback.
/// </remarks>
public enum CompletionItemKind
{
	Text = 1,

	Method = 2,

	Function = 3,

	Constructor = 4,

	Field = 5,

	Variable = 6,

	Class = 7,

	Interface = 8,

	Module = 9,

	Property = 10,

	Unit = 11,

	Value = 12,

	Enum = 13,

	Keyword = 14,

	Snippet = 15,

	Color = 16,

	File = 17,

	Reference = 18,

	Folder = 19,

	EnumMember = 20,

	Constant = 21,

	Struct = 22,

	Event = 23,

	Operator = 24,

	TypeParameter = 25,
}
