using Nickelony.IDEKit.LanguageFeatures.Navigation;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Answers definition requests with the language server's <c>textDocument/definition</c> response.</summary>
/// <param name="pipeline">The pipeline that sends the document-scoped request.</param>
/// <param name="options">The provider tunables, whose path comparison decides whether a location is in the requested document.</param>
internal sealed class DefinitionProvider(IDocumentRequestPipeline pipeline, LanguageServerFeatureProviderOptions options) : IDefinitionProvider
{
	/// <inheritdoc/>
	public Task<DefinitionLocation?> GetDefinitionAsync(DefinitionRequest request, CancellationToken cancellationToken = default)
	{
		if (request.SymbolOffset is not { } symbolOffset)
			return Task.FromResult<DefinitionLocation?>(null);

		return pipeline.SendAsync(
			filePath: request.DocumentId,
			documentText: request.DocumentText,

			request: new DocumentRequestDescriptor<DefinitionResponse?, DefinitionLocation?>(
				method: LspMethodNames.Definition,
				buildParameters: static (textDocument, position) => new TextDocumentPositionParams(textDocument, position),

				parseResponse: (response, document) =>
					RequestedDocumentNormalizer.Normalize(
						DefinitionResponseParser.Parse(response),
						document.NormalizedFilePath,
						options.PathComparison
					),

				fallbackValue: null,
				offset: symbolOffset
			),

			cancellationToken
		);
	}
}
