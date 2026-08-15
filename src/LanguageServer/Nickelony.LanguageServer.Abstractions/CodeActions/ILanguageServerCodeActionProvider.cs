namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Produces code actions (quick fixes and refactorings) for a document range.
/// </summary>
public interface ILanguageServerCodeActionProvider
{
	/// <summary>
	/// Gets a value indicating whether code-action requests are supported by the current ready session.
	/// </summary>
	/// <remarks>
	/// This flag is <see langword="false"/> until a language-server session is ready and supports code actions;
	/// the lazy-startup and capability-gating contract is on <see cref="ILanguageServerIntelliSenseProvider"/>.
	/// </remarks>
	bool SupportsCodeActions { get; }

	/// <summary>
	/// Requests the code actions (quick fixes and refactorings) available for a document range.
	/// </summary>
	/// <remarks>
	/// Code actions are capability-gated: the request is skipped with an empty result when the current session
	/// does not support them (see <see cref="SupportsCodeActions"/>). The returned list is an owned snapshot with
	/// the provider's entry order preserved. Actions the server expresses as a command without an edit cannot be
	/// represented by the shared edit model and are omitted by providers.
	/// </remarks>
	/// <param name="request">The document and the range to get code actions for.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The available code actions, or an empty list when unavailable.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	Task<IReadOnlyList<TextCodeAction>> GetCodeActionsAsync(LanguageServerCodeActionRequest request,
		CancellationToken cancellationToken = default);
}
