namespace Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;

/// <summary>Provides the symbols of a whole workspace that match a query.</summary>
/// <remarks>
/// Implementations must be safe to call from any thread and must not touch UI state. The provider owns
/// the matching rule, so a host that ranks the answer ranks symbols the provider already chose. Implementations reject a <see langword="null"/>
/// request with <see cref="ArgumentNullException"/>, and the call observes the supplied cancellation token.
/// </remarks>
public interface IWorkspaceSymbolProvider
{
	/// <summary>Returns the workspace symbols for the supplied request.</summary>
	/// <param name="request">The query to match symbols against.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The matching symbols in provider order, or an empty list when none apply; never <see langword="null"/>.</returns>
	Task<IReadOnlyList<WorkspaceSymbol>> GetWorkspaceSymbolsAsync(WorkspaceSymbolRequest request, CancellationToken cancellationToken = default);
}
