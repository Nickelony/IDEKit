namespace Nickelony.IDEKit.LanguageFeatures.CodeActions;

/// <summary>Provides the code actions available for a document range.</summary>
/// <remarks>
/// Implementations must be safe to call from any thread: the request is an immutable snapshot and no UI state may be touched.
/// The call observes the supplied cancellation token, and a caller that needs supersession cancels the token and discards stale results.
/// Implementations must reject a <see langword="null"/> request with <see cref="ArgumentNullException"/>. A provider that offers
/// an action without its edit also implements <see cref="ICodeActionResolver"/>, and one whose actions carry a <see cref="CodeActionCommand"/>
/// implements <see cref="ICodeActionCommandRunner"/>.
/// </remarks>
public interface ICodeActionProvider
{
	/// <summary>Returns the code actions available for the supplied request.</summary>
	/// <param name="request">The document and range request.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>
	/// The available code actions in provider order; an empty list when none apply. The result is never <see langword="null"/>. The returned list
	/// must not be mutated after the call because the caller can retain it.
	/// </returns>
	Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(CodeActionRequest request, CancellationToken cancellationToken = default);
}
