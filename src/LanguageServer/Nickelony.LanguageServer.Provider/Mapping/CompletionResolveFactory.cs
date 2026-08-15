using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Builds the lazy-resolve callback for a parsed completion item whose detail or documentation is
/// incomplete. The parser decides whether an item needs resolution before invoking the factory.
/// </summary>
/// <param name="item">The parsed completion item that may need resolution.</param>
/// <param name="payload">The originating completion-item payload.</param>
/// <param name="priorityRank">The zero-based protocol ordering rank assigned to the item, reused so a resolved item keeps the same priority hint.</param>
/// <returns>The resolve callback for the item.</returns>
internal delegate Func<CancellationToken, Task<TextCompletionItem>> CompletionResolveFactory(
	TextCompletionItem item,
	CompletionItemPayload payload,
	int priorityRank);
