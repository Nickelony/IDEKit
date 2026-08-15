namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Resolves symbol reference locations from the current document context.
/// </summary>
public interface ILanguageServerReferencesProvider
{
	/// <summary>
	/// Gets a value indicating whether reference lookup is supported by the current ready session.
	/// </summary>
	/// <remarks>
	/// This flag is <see langword="false"/> until a language-server session is ready and supports references;
	/// the lazy-startup and capability-gating contract is on <see cref="ILanguageServerIntelliSenseProvider"/>.
	/// </remarks>
	bool SupportsReferences { get; }

	/// <summary>
	/// Resolves reference locations for the supplied request.
	/// </summary>
	/// <remarks>
	/// Reference lookup is capability-gated; see <see cref="SupportsReferences"/>. The request's
	/// <see cref="LanguageServerReferenceRequest.IncludeDeclaration"/> flag is forwarded to the language server and decides
	/// whether the symbol declaration may be part of the result.
	/// </remarks>
	/// <param name="request">The current document and position request.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The resolved reference locations, or an empty list when references are unsupported or none are available.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	Task<IReadOnlyList<TextReferenceLocation>> GetReferencesAsync(
		LanguageServerReferenceRequest request, CancellationToken cancellationToken = default);
}
