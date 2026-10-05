using Nickelony.IDEKit.LanguageFeatures.Completion;
using Lsp = Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Maps the deprecation of a protocol completion item onto the shared <see cref="CompletionItemTag"/> vocabulary.</summary>
internal static class CompletionItemTagConversion
{
	/// <summary>Resolves the tags of a protocol completion item.</summary>
	/// <param name="itemPayload">The protocol completion item.</param>
	/// <returns><see cref="CompletionItemTag.Deprecated"/> when the item is deprecated, otherwise, an empty list.</returns>
	/// <remarks>
	/// The <c>tags</c> array and the <c>deprecated</c> boolean are honored, because a server may emit only the boolean; relying on <c>tags</c> alone
	/// would leave deprecated functions unmarked in the completion list.
	/// </remarks>
	internal static List<CompletionItemTag> FromProtocol(Lsp.CompletionItemPayload itemPayload) =>
		itemPayload.IsDeprecated ? [CompletionItemTag.Deprecated] : [];
}
