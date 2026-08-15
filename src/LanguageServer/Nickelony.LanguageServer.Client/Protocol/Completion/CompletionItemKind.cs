namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Describes the kind of a completion item, using the LSP <c>CompletionItemKind</c> mapping.
/// </summary>
/// <remarks>
/// The numeric values match the protocol values and are serialized as numbers. A value outside the
/// defined range stays representable as an unnamed enum value; the protocol defines no default kind, so
/// hosts that map the kind onto a library taxonomy choose their own fallback (the <c>Interop</c> bridge
/// maps values outside the protocol range to <c>TextCompletionItemKind.Generic</c>).
/// </remarks>
public enum CompletionItemKind
{
	/// <summary>
	/// A text completion item, such as a snippet or a plain word.
	/// </summary>
	Text = 1,

	/// <summary>
	/// A method completion item.
	/// </summary>
	Method = 2,

	/// <summary>
	/// A function completion item.
	/// </summary>
	Function = 3,

	/// <summary>
	/// A constructor completion item.
	/// </summary>
	Constructor = 4,

	/// <summary>
	/// A field completion item.
	/// </summary>
	Field = 5,

	/// <summary>
	/// A variable completion item.
	/// </summary>
	Variable = 6,

	/// <summary>
	/// A class completion item.
	/// </summary>
	Class = 7,

	/// <summary>
	/// An interface completion item.
	/// </summary>
	Interface = 8,

	/// <summary>
	/// A module completion item.
	/// </summary>
	Module = 9,

	/// <summary>
	/// A property completion item.
	/// </summary>
	Property = 10,

	/// <summary>
	/// A unit completion item.
	/// </summary>
	Unit = 11,

	/// <summary>
	/// A value completion item.
	/// </summary>
	Value = 12,

	/// <summary>
	/// An enumeration completion item.
	/// </summary>
	Enum = 13,

	/// <summary>
	/// A keyword completion item.
	/// </summary>
	Keyword = 14,

	/// <summary>
	/// A snippet completion item.
	/// </summary>
	Snippet = 15,

	/// <summary>
	/// A color completion item.
	/// </summary>
	Color = 16,

	/// <summary>
	/// A file completion item.
	/// </summary>
	File = 17,

	/// <summary>
	/// A reference completion item.
	/// </summary>
	Reference = 18,

	/// <summary>
	/// A folder completion item.
	/// </summary>
	Folder = 19,

	/// <summary>
	/// An enumeration member completion item.
	/// </summary>
	EnumMember = 20,

	/// <summary>
	/// A constant completion item.
	/// </summary>
	Constant = 21,

	/// <summary>
	/// A struct completion item.
	/// </summary>
	Struct = 22,

	/// <summary>
	/// An event completion item.
	/// </summary>
	Event = 23,

	/// <summary>
	/// An operator completion item.
	/// </summary>
	Operator = 24,

	/// <summary>
	/// A type parameter completion item.
	/// </summary>
	TypeParameter = 25
}
