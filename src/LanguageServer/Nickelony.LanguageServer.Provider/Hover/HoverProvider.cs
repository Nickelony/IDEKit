using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Answers hover requests with the language server's <c>textDocument/hover</c> response.</summary>
/// <param name="pipeline">The pipeline that sends the document-scoped request.</param>
internal sealed class HoverProvider(IDocumentRequestPipeline pipeline) : IHoverProvider
{
	/// <inheritdoc/>
	public Task<HoverInformation?> GetHoverAsync(HoverRequest request, CancellationToken cancellationToken = default)
	{
		return pipeline.SendAsync(
			filePath: request.DocumentId,
			documentText: request.DocumentText,

			request: new DocumentRequestDescriptor<HoverResponse?, HoverInformation?>(
				method: LspMethodNames.Hover,
				buildParameters: static (textDocument, position) => new TextDocumentPositionParams(textDocument, position),
				parseResponse: static (response, document) => HoverResponseParser.Parse(response, document.LineMap),
				fallbackValue: null,
				offset: request.HoveredOffset
			),

			cancellationToken
		);
	}
}
