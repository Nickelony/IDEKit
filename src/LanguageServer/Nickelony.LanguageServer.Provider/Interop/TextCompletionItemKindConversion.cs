using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Maps protocol completion kinds onto the shared <see cref="TextCompletionItemKind"/> vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// The protocol transmits completion kinds as numbers; this bridge resolves them without a
/// provider-side conversion table. It lives with the protocol boundary because the mapping pins
/// protocol numerics, while the shared vocabulary itself stays protocol-free.
/// </para>
/// <para>
/// Every defined protocol value (1-25) maps one-to-one onto a well-known member whose identifier
/// matches the protocol name; see <see cref="FromLspKind"/> for the explicit mapping.
/// </para>
/// </remarks>
public static class TextCompletionItemKindConversion
{
	/// <summary>
	/// Resolves a protocol <c>CompletionItemKind</c> numeric value to the corresponding well-known member.
	/// </summary>
	/// <remarks>
	/// An unrecognized value maps to <see cref="TextCompletionItemKind.Generic"/>, the presentation
	/// fallback, because the protocol defines no default kind; a payload that omits the value maps
	/// through the caller's convention, which the consuming provider resolves to
	/// <see cref="TextCompletionItemKind.Generic"/> as well.
	/// </remarks>
	/// <param name="lspKind">The numeric protocol completion kind.</param>
	/// <returns>
	/// The matching well-known member, or <see cref="TextCompletionItemKind.Generic"/> for a value
	/// outside the protocol range.
	/// </returns>
	public static TextCompletionItemKind FromLspKind(int lspKind) => lspKind switch
	{
		1 => TextCompletionItemKind.Text,
		2 => TextCompletionItemKind.Method,
		3 => TextCompletionItemKind.Function,
		4 => TextCompletionItemKind.Constructor,
		5 => TextCompletionItemKind.Field,
		6 => TextCompletionItemKind.Variable,
		7 => TextCompletionItemKind.Class,
		8 => TextCompletionItemKind.Interface,
		9 => TextCompletionItemKind.Module,
		10 => TextCompletionItemKind.Property,
		11 => TextCompletionItemKind.Unit,
		12 => TextCompletionItemKind.Value,
		13 => TextCompletionItemKind.Enum,
		14 => TextCompletionItemKind.Keyword,
		15 => TextCompletionItemKind.Snippet,
		16 => TextCompletionItemKind.Color,
		17 => TextCompletionItemKind.File,
		18 => TextCompletionItemKind.Reference,
		19 => TextCompletionItemKind.Folder,
		20 => TextCompletionItemKind.EnumMember,
		21 => TextCompletionItemKind.Constant,
		22 => TextCompletionItemKind.Struct,
		23 => TextCompletionItemKind.Event,
		24 => TextCompletionItemKind.Operator,
		25 => TextCompletionItemKind.TypeParameter,
		_ => TextCompletionItemKind.Generic
	};
}
