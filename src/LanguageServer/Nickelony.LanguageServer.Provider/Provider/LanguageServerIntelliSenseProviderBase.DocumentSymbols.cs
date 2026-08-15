using Nickelony.IDEKit.IntelliSense.DocumentSymbols;

namespace Nickelony.LanguageServer.Provider;

public abstract partial class LanguageServerIntelliSenseProviderBase
{
	/// <inheritdoc/>
	public virtual Task<IReadOnlyList<TextDocumentSymbol>> GetDocumentSymbolsAsync(
		LanguageServerDocumentSymbolRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		// The parse closure needs the normalized document identity for the flat same-document filter.
		// An unusable path fails normalization inside the request pipeline and returns the fallback.
		string documentFilePath = ResolveDocumentFilePath(request.FilePath);

		return SendDocumentRequestAsync<DocumentSymbolsResponse?, IReadOnlyList<TextDocumentSymbol>>(
			request.FilePath, request.DocumentText, LspMethodNames.DocumentSymbol,
			supportsRequest: static client => client.SupportsDocumentSymbols,
			buildParameters: static textDocument => new DocumentSymbolParams(textDocument),
			parseResponse: response => ResponseParser.ParseDocumentSymbols(response, request.DocumentText, documentFilePath, Logger),
			fallbackValue: [],
			cancellationToken);
	}
}
