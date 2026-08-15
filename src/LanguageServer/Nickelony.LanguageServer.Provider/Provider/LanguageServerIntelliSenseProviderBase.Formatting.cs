namespace Nickelony.LanguageServer.Provider;

public abstract partial class LanguageServerIntelliSenseProviderBase
{
	/// <inheritdoc/>
	public virtual async Task<TextWorkspaceEdit?> FormatDocumentAsync(LanguageServerFormattingRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		// The parse closure needs the normalized document identity for the resulting document edit; an
		// unusable path fails normalization inside the request pipeline and returns the fallback.
		string documentFilePath = ResolveDocumentFilePath(request.FilePath);

		return await SendDocumentRequestAsync<TextEditPayload[]?, TextWorkspaceEdit?>(
			request.FilePath, request.DocumentText, LspMethodNames.Formatting,
			supportsRequest: static client => client.SupportsFormatting,
			buildParameters: textDocument => new DocumentFormattingParams(textDocument,
				FormattingOptionsConversion.ToPayload(request.Options)),
			parseResponse: response =>
			{
				IReadOnlyList<TextEdit> textEdits = ResponseParser.ParseDocumentFormattingEdits(response);

				return textEdits.Count == 0
					? null
					: new TextWorkspaceEdit([
						new TextDocumentEdit(documentFilePath, textEdits)
					]);
			},
			fallbackValue: null,
			cancellationToken).ConfigureAwait(false);
	}
}
