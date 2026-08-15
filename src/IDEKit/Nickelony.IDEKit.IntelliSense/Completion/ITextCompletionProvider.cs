namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Provides shared completion items for a document snapshot and caret position.
/// </summary>
/// <remarks>
/// Implementations must be safe to call from any thread: the request carries a materialized
/// document text snapshot and no UI state may be touched. This permits the host to run
/// catalog-driven completion on the thread pool when the work is CPU-bound. The contract is
/// synchronous and carries no cancellation token, so the provider call itself cannot be canceled;
/// callers that need supersession run the call on a task-scheduling boundary and discard stale
/// results. Implementations must reject a <see langword="null"/> request with
/// <see cref="ArgumentNullException"/>.
/// </remarks>
public interface ITextCompletionProvider
{
	/// <summary>
	/// Gets completion items for the supplied request.
	/// </summary>
	/// <param name="request">The completion request.</param>
	/// <returns>
	/// The candidate completion items; an empty list when none apply. The return value is never
	/// <see langword="null"/>; returning <see langword="null"/> violates the contract and causes
	/// <see cref="TextCompletionSessionKernel"/> to throw <see cref="InvalidOperationException"/>.
	/// The returned list must not be mutated after the call because a decision can retain it.
	/// </returns>
	IReadOnlyList<TextCompletionItem> GetCompletionItems(TextCompletionRequest request);
}
