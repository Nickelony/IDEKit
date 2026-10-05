using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Answers signature-help requests with the language server's <c>textDocument/signatureHelp</c> response.</summary>
/// <param name="pipeline">The pipeline that sends the document-scoped request.</param>
internal sealed class SignatureHelpProvider(IDocumentRequestPipeline pipeline) : ISignatureHelpProvider
{
	/// <inheritdoc/>
	public Task<SignatureHelpInformation?> GetSignatureHelpAsync(SignatureHelpRequest request, CancellationToken cancellationToken = default)
	{
		return pipeline.SendAsync(
			filePath: request.DocumentId,
			documentText: request.DocumentText,

			request: new DocumentRequestDescriptor<SignatureHelpResponse?, SignatureHelpInformation?>(
				method: LspMethodNames.SignatureHelp,

				buildParameters: (textDocument, position) =>
					new SignatureHelpParams(textDocument, position, SignatureHelpContextConversion.ToProtocol(request.Context)),

				parseResponse: static (response, _) => SignatureHelpResponseParser.Parse(response),
				fallbackValue: null,
				offset: request.CaretOffset
			),

			cancellationToken
		);
	}
}
