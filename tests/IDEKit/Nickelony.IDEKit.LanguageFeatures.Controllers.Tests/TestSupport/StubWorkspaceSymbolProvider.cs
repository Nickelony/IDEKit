using Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Answers every query with one list of workspace symbols and cannot resolve a symbol.</summary>
/// <param name="symbols">The symbols to answer every query with.</param>
internal sealed class StubWorkspaceSymbolProvider(params WorkspaceSymbol[] symbols) : IWorkspaceSymbolProvider
{
	public Task<IReadOnlyList<WorkspaceSymbol>> GetWorkspaceSymbolsAsync(
		WorkspaceSymbolRequest request,
		CancellationToken cancellationToken = default
	) => Task.FromResult<IReadOnlyList<WorkspaceSymbol>>(symbols);
}
