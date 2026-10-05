using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Answers document-symbol requests with the language server's <c>textDocument/documentSymbol</c> response.</summary>
/// <param name="pipeline">The pipeline that sends the document-scoped request.</param>
/// <param name="options">The provider tunables, whose path comparison decides whether a flat symbol entry targets the requested document.</param>
/// <param name="logger">The logger that records a symbol the parser had to drop.</param>
internal sealed class DocumentSymbolProvider(IDocumentRequestPipeline pipeline, LanguageServerFeatureProviderOptions options, ILogger logger)
	: IDocumentSymbolProvider
{
	/// <inheritdoc/>
	public Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(DocumentSymbolRequest request, CancellationToken cancellationToken = default)
	{
		return pipeline.SendAsync(
			filePath: request.DocumentId,
			documentText: request.DocumentText,

			request: new DocumentRequestDescriptor<DocumentSymbolResponse?, IReadOnlyList<DocumentSymbol>>(
				method: LspMethodNames.DocumentSymbol,
				buildParameters: static (textDocument, _) => new DocumentSymbolParams(textDocument),

				parseResponse: (response, document) =>
					DocumentSymbolFilter.FilterByName(
						DocumentSymbolResponseParser.Parse(
							response,
							lineMap: document.LineMap,
							normalizedFilePath: document.NormalizedFilePath,
							pathComparison: options.PathComparison,
							logger
						),

						request.FilterText
					),

				fallbackValue: [],
				supportsRequest: static client => client.NegotiatedCapabilities.SupportsDocumentSymbols
			),

			cancellationToken
		);
	}
}
