namespace Nickelony.IDEKit.LanguageFeatures.Completion;

/// <summary>Provides shared completion items for a document snapshot and caret position.</summary>
/// <remarks>
/// Implementations must be safe to call from any thread, must reject a <see langword="null"/> request with <see cref="ArgumentNullException"/>,
/// and may not touch UI state: the request carries a materialized document text snapshot. The call observes the supplied cancellation token,
/// and a caller that needs supersession cancels the token and discards stale results.
/// </remarks>
public interface ICompletionProvider
{
	/// <summary>Returns the completion items for the supplied request.</summary>
	/// <param name="request">The completion request.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>
	/// The candidate completion items; an empty list when none apply. The result is never <see langword="null"/>; a task that produces
	/// <see langword="null"/> violates the contract, and the code that consumes the result throws <see cref="InvalidOperationException"/>.
	/// The returned list must not be mutated after the call because the consumer can retain it.
	/// </returns>
	Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(CompletionRequest request, CancellationToken cancellationToken = default);
}
