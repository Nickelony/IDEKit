namespace Nickelony.IDEKit.IntelliSense.CodeActions;

/// <summary>
/// Provides the code actions available for a document range.
/// </summary>
/// <remarks>
/// Implementations must be safe to call from any thread: the request is an immutable snapshot and
/// no UI state may be touched. The contract is synchronous and carries no cancellation token, so
/// callers that need supersession schedule the call themselves and discard stale results.
/// Implementations must reject a <see langword="null"/> request with
/// <see cref="ArgumentNullException"/>.
/// </remarks>
public interface ITextCodeActionProvider
{
	/// <summary>
	/// Gets the code actions available for the supplied request.
	/// </summary>
	/// <param name="request">The document and range request.</param>
	/// <returns>
	/// The available code actions; an empty list when none apply. The return value is never
	/// <see langword="null"/>. The returned list must not be mutated after the call because the
	/// caller can retain it.
	/// </returns>
	IReadOnlyList<TextCodeActionItem> GetCodeActions(TextCodeActionRequest request);
}
