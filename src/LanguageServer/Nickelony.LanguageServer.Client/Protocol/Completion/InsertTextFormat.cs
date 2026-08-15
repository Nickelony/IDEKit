namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Describes the format of a completion item's insertion text, using the LSP <c>InsertTextFormat</c> mapping.
/// </summary>
/// <remarks>
/// The numeric values match the protocol values and are serialized as numbers. A value outside the
/// defined range stays representable as an unnamed enum value; hosts that cannot act on an
/// unknown format treat it like <see cref="PlainText"/>.
/// </remarks>
public enum InsertTextFormat
{
	/// <summary>
	/// The insertion text is plain text.
	/// </summary>
	PlainText = 1,

	/// <summary>
	/// The insertion text is a snippet with placeholders, tabstops, and escapes.
	/// </summary>
	Snippet = 2
}
