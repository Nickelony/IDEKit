using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Builds the lazy-resolve callback for a parsed completion item whose detail or documentation is incomplete.</summary>
/// <param name="item">The parsed completion item that may need resolution.</param>
/// <param name="payload">The originating completion-item payload.</param>
/// <param name="priorityRank">
/// The zero-based protocol ordering rank assigned to the item, reused so a resolved item keeps the same priority hint.
/// </param>
/// <returns>The resolve callback for the item.</returns>
/// <remarks>
/// The parser decides whether an item needs resolution before invoking the factory.
/// </remarks>
internal delegate Func<CancellationToken, Task<CompletionItem>> CompletionResolveFactory(
	CompletionItem item,
	CompletionItemPayload payload,
	int priorityRank
);
