using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.IDEKit.LanguageFeatures.Rename;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Answers rename requests with the language server's <c>textDocument/rename</c> response.</summary>
/// <param name="pipeline">The pipeline that sends the document-scoped request.</param>
/// <param name="options">The provider tunables, whose path comparison decides whether two edited paths are one document.</param>
/// <param name="logger">The logger that records a workspace edit the parser had to drop.</param>
internal sealed class RenameProvider(IDocumentRequestPipeline pipeline, LanguageServerFeatureProviderOptions options, ILogger logger)
	: IRenameProvider
{
	/// <inheritdoc/>
	public async Task<WorkspaceEdit?> GetRenameEditsAsync(RenameRequest request, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(request.NewName))
			return null;

		return await pipeline
			.SendAsync(
				filePath: request.DocumentId,
				documentText: request.DocumentText,

				request: new DocumentRequestDescriptor<WorkspaceEditResponse?, WorkspaceEdit?>(
					method: LspMethodNames.Rename,
					buildParameters: (textDocument, position) => new RenameParams(textDocument, position, request.NewName),

					parseResponse: (response, document) =>
						RequestedDocumentNormalizer.Normalize(
							edit: WorkspaceEditParser.Parse(response, options.PathComparison, logger),
							requestDocumentId: request.DocumentId,
							requestFilePath: document.NormalizedFilePath,
							pathComparison: options.PathComparison
						),

					fallbackValue: null,
					supportsRequest: static client => client.NegotiatedCapabilities.SupportsRename,
					offset: request.SymbolOffset
				),

				cancellationToken
			)
			.ConfigureAwait(false);
	}
}
