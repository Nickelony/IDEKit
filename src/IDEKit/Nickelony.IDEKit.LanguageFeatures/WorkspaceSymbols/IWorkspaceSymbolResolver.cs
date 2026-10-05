namespace Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;

/// <summary>Resolves the range of a workspace symbol that a provider listed without one.</summary>
/// <remarks>
/// A provider implements this contract beside <see cref="IWorkspaceSymbolProvider"/> when it lists symbols cheaply and computes
/// their ranges on demand, as a language server does for <c>workspaceSymbol/resolve</c>. The host that opens a symbol calls it for the one symbol
/// the user chose, never for every listed symbol, and only when <see cref="WorkspaceSymbolLocation.Range"/> is <see langword="null"/>.
/// </remarks>
public interface IWorkspaceSymbolResolver
{
	/// <summary>Resolves the supplied symbol into one that carries the range the provider computes for it.</summary>
	/// <param name="symbol">A symbol that this provider listed.</param>
	/// <param name="cancellationToken">A token that can cancel the resolution.</param>
	/// <returns>
	/// The resolved symbol, or <paramref name="symbol"/> unchanged when the provider cannot resolve it: the symbol already has a range,
	/// is not one of its own, or the resolution failed. The result is never <see langword="null"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="symbol"/> is <see langword="null"/>.</exception>
	Task<WorkspaceSymbol> ResolveAsync(WorkspaceSymbol symbol, CancellationToken cancellationToken = default);
}
