using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.IDEKit.LanguageFeatures.Formatting;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Answers formatting requests with the language server's <c>textDocument/formatting</c> response.</summary>
/// <param name="pipeline">The pipeline that sends the document-scoped request.</param>
/// <remarks>
/// The edit names the document by the request's identifier, as the host named it, so a planner that matches it ordinally finds it.
/// </remarks>
internal sealed class FormattingProvider(IDocumentRequestPipeline pipeline) : IFormattingProvider
{
	/// <inheritdoc/>
	public async Task<WorkspaceEdit?> GetFormattingEditsAsync(FormattingRequest request, CancellationToken cancellationToken = default)
	{
		return await pipeline
			.SendAsync(
				filePath: request.DocumentId,
				documentText: request.DocumentText,

				request: new DocumentRequestDescriptor<TextEditPayload[]?, WorkspaceEdit?>(
					method: LspMethodNames.Formatting,

					buildParameters: (textDocument, _) =>
						new DocumentFormattingParams(
							textDocument,
							new FormattingOptionsPayload(request.Options.TabSize, request.Options.InsertSpaces)
						),

					parseResponse: (response, _) =>
					{
						IReadOnlyList<TextEdit> textEdits = WorkspaceEditParser.ParseTextEdits(response);
						return textEdits.Count == 0 ? null : new WorkspaceEdit([new TextDocumentEdit(request.DocumentId, textEdits)]);
					},

					fallbackValue: null,
					supportsRequest: static client => client.NegotiatedCapabilities.SupportsFormatting
				),

				cancellationToken
			)
			.ConfigureAwait(false);
	}
}
