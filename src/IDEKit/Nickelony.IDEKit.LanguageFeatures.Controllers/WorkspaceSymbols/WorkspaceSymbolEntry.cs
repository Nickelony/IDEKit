using Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.WorkspaceSymbols;

/// <summary>A listed workspace symbol with the provider that listed it, which resolves the range of the symbol when the user chooses it.</summary>
/// <param name="Provider">The provider that listed the symbol.</param>
/// <param name="Symbol">The symbol as the provider listed it.</param>
internal sealed record WorkspaceSymbolEntry(IWorkspaceSymbolProvider Provider, WorkspaceSymbol Symbol);
