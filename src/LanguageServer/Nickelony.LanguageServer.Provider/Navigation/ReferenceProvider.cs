using Nickelony.IDEKit.LanguageFeatures.Navigation;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Answers reference requests with the language server's <c>textDocument/references</c> response.</summary>
/// <param name="pipeline">The pipeline that sends the document-scoped request.</param>
/// <param name="options">The provider tunables, whose path comparison decides whether a location is in the requested document.</param>
internal sealed class ReferenceProvider(IDocumentRequestPipeline pipeline, LanguageServerFeatureProviderOptions options) : IReferenceProvider
{
	/// <inheritdoc/>
	public async Task<IReadOnlyList<ReferenceLocation>> GetReferencesAsync(ReferenceRequest request, CancellationToken cancellationToken = default)
	{
		return await pipeline
			.SendAsync(
				filePath: request.DocumentId,
				documentText: request.DocumentText,

				request: new DocumentRequestDescriptor<LocationPayload[]?, IReadOnlyList<ReferenceLocation>>(
					method: LspMethodNames.References,

					buildParameters: (textDocument, position) =>
						new ReferenceParams(textDocument, position, new ReferenceContextPayload(IncludeDeclaration: request.IncludeDeclaration)),

					parseResponse: (response, document) =>
						RequestedDocumentNormalizer.Normalize(
							locations: ReferenceResponseParser.Parse(response),
							requestDocumentId: request.DocumentId,
							requestFilePath: document.NormalizedFilePath,
							pathComparison: options.PathComparison
						),

					fallbackValue: [],
					supportsRequest: static client => client.NegotiatedCapabilities.SupportsReferences,
					offset: request.SymbolOffset
				),

				cancellationToken
			)
			.ConfigureAwait(false);
	}
}
