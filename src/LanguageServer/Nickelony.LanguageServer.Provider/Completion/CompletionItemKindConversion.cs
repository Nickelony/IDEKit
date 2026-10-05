using Nickelony.IDEKit.LanguageFeatures.Completion;
using Lsp = Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Maps the protocol completion kind onto the shared <see cref="CompletionItemKind"/> vocabulary.</summary>
/// <remarks>
/// It lives with the protocol boundary because the mapping pins protocol members, while the shared vocabulary itself stays protocol-free.
/// Every defined protocol member maps one-to-one onto a well-known member whose identifier matches the protocol name.
/// </remarks>
internal static class CompletionItemKindConversion
{
	/// <summary>Resolves a protocol completion kind to the corresponding well-known member.</summary>
	/// <param name="kind">The protocol completion kind, or <see langword="null"/> when the item omitted it.</param>
	/// <returns>The matching well-known member, or <see cref="CompletionItemKind.Generic"/> for an omitted or unrecognized kind.</returns>
	/// <remarks>
	/// An omitted or unrecognized value maps to <see cref="CompletionItemKind.Generic"/>, the presentation fallback, because the protocol defines
	/// no default kind.
	/// </remarks>
	internal static CompletionItemKind FromProtocol(Lsp.CompletionItemKind? kind)
	{
		return kind switch
		{
			Lsp.CompletionItemKind.Text => CompletionItemKind.Text,
			Lsp.CompletionItemKind.Method => CompletionItemKind.Method,
			Lsp.CompletionItemKind.Function => CompletionItemKind.Function,
			Lsp.CompletionItemKind.Constructor => CompletionItemKind.Constructor,
			Lsp.CompletionItemKind.Field => CompletionItemKind.Field,
			Lsp.CompletionItemKind.Variable => CompletionItemKind.Variable,
			Lsp.CompletionItemKind.Class => CompletionItemKind.Class,
			Lsp.CompletionItemKind.Interface => CompletionItemKind.Interface,
			Lsp.CompletionItemKind.Module => CompletionItemKind.Module,
			Lsp.CompletionItemKind.Property => CompletionItemKind.Property,
			Lsp.CompletionItemKind.Unit => CompletionItemKind.Unit,
			Lsp.CompletionItemKind.Value => CompletionItemKind.Value,
			Lsp.CompletionItemKind.Enum => CompletionItemKind.Enum,
			Lsp.CompletionItemKind.Keyword => CompletionItemKind.Keyword,
			Lsp.CompletionItemKind.Snippet => CompletionItemKind.Snippet,
			Lsp.CompletionItemKind.Color => CompletionItemKind.Color,
			Lsp.CompletionItemKind.File => CompletionItemKind.File,
			Lsp.CompletionItemKind.Reference => CompletionItemKind.Reference,
			Lsp.CompletionItemKind.Folder => CompletionItemKind.Folder,
			Lsp.CompletionItemKind.EnumMember => CompletionItemKind.EnumMember,
			Lsp.CompletionItemKind.Constant => CompletionItemKind.Constant,
			Lsp.CompletionItemKind.Struct => CompletionItemKind.Struct,
			Lsp.CompletionItemKind.Event => CompletionItemKind.Event,
			Lsp.CompletionItemKind.Operator => CompletionItemKind.Operator,
			Lsp.CompletionItemKind.TypeParameter => CompletionItemKind.TypeParameter,
			_ => CompletionItemKind.Generic,
		};
	}
}
