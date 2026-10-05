using Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

namespace Nickelony.Samples;

/// <summary>
/// Answers a document-symbol request with the declared functions, each with its parameters as children, and the declared variables.
/// </summary>
internal sealed class DeclarationDocumentSymbolProvider : IDocumentSymbolProvider
{
	private static readonly DocumentSymbolOutlineHooks<FunctionDeclaration> s_functionHooks = new()
	{
		NameSelector = function => function.Name,
		KindSelector = _ => DocumentSymbolKind.Function,
		RangeSelector = function => function.HeaderRange,
		SelectionRangeSelector = function => function.NameRange,
		DetailSelector = function => $"({string.Join(", ", function.Parameters.Select(parameter => parameter.Name))})",
	};

	private static readonly DocumentSymbolOutlineHooks<VariableDeclaration> s_variableHooks = new()
	{
		NameSelector = variable => variable.Name,
		KindSelector = _ => DocumentSymbolKind.Variable,
		RangeSelector = variable => variable.NameRange,
		SelectionRangeSelector = variable => variable.NameRange,
	};

	public Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(DocumentSymbolRequest request, CancellationToken cancellationToken = default)
	{
		// The builder turns the scanner's records into symbols: a root per function with its parameters as children, and a flat symbol per variable.
		IReadOnlyList<DocumentSymbol> functions = DocumentSymbolOutlineBuilder.Build(
			groups: DeclarationScanner.FindFunctions(request.DocumentText),
			groupHooks: s_functionHooks,
			groupItemsSelector: function => function.Parameters,
			itemHooks: s_variableHooks
		);

		IReadOnlyList<DocumentSymbol> variables = DocumentSymbolOutlineBuilder.Build(
			DeclarationScanner.FindVariables(request.DocumentText),
			s_variableHooks
		);

		List<DocumentSymbol> symbols = [.. functions.Concat(variables).OrderBy(symbol => symbol.Range?.Offset ?? 0)];

		return Task.FromResult(DocumentSymbolFilter.FilterByName(symbols, request.FilterText));
	}
}
