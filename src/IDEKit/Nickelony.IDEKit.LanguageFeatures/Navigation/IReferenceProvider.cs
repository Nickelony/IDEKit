namespace Nickelony.IDEKit.LanguageFeatures.Navigation;

/// <summary>Resolves the reference locations of a symbol in the current document context.</summary>
/// <remarks>
/// Implementations must be safe to call from any thread and must not touch UI state. Implementations reject a <see langword="null"/>
/// request with <see cref="ArgumentNullException"/>, and the call observes the supplied cancellation token.
/// </remarks>
public interface IReferenceProvider
{
	/// <summary>Returns the reference locations for the supplied request.</summary>
	/// <param name="request">The current document and symbol-position request.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>
	/// The reference locations in provider order, or an empty list when none are available. The result is never <see langword="null"/>.
	/// <see cref="ReferenceRequest.IncludeDeclaration"/> decides whether the symbol declaration may be part of it.
	/// </returns>
	Task<IReadOnlyList<ReferenceLocation>> GetReferencesAsync(ReferenceRequest request, CancellationToken cancellationToken = default);
}
