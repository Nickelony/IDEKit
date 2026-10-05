namespace Nickelony.IDEKit.LanguageFeatures.Navigation;

/// <summary>Resolves a navigable definition location for a symbol in the current document context.</summary>
/// <remarks>
/// Implementations must be safe to call from any thread and must not touch UI state.
/// Implementations reject a <see langword="null"/> request with <see cref="ArgumentNullException"/>. A provider that resolves
/// by name returns <see langword="null"/> for a blank <see cref="DefinitionRequest.SymbolName"/> instead of failing, and a provider that resolves
/// by position reads <see cref="DefinitionRequest.SymbolOffset"/>. The call observes the supplied cancellation token.
/// </remarks>
public interface IDefinitionProvider
{
	/// <summary>Returns the definition location for the supplied request.</summary>
	/// <param name="request">The current document and symbol lookup request.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The resolved definition location, or <see langword="null"/> when no definition can be resolved.</returns>
	Task<DefinitionLocation?> GetDefinitionAsync(DefinitionRequest request, CancellationToken cancellationToken = default);
}
