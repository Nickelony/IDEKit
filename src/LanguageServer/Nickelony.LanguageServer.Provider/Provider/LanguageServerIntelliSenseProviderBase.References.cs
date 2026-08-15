namespace Nickelony.LanguageServer.Provider;

public abstract partial class LanguageServerIntelliSenseProviderBase
{
	/// <inheritdoc/>
	public virtual async Task<IReadOnlyList<TextReferenceLocation>> GetReferencesAsync(LanguageServerReferenceRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		return await SendDocumentRequestAsync<ReferenceLocationPayload[]?, IReadOnlyList<TextReferenceLocation>>(
			request.FilePath, request.DocumentText, LspMethodNames.References,
			supportsRequest: static client => client.SupportsReferences,
			buildParameters: textDocument => new ReferenceParams(textDocument,
				ToProtocolPosition(request.Position),
				new ReferenceContextPayload(IncludeDeclaration: request.IncludeDeclaration)),
			parseResponse: ResponseParser.ParseReferenceLocations,
			fallbackValue: [],
			cancellationToken).ConfigureAwait(false);
	}
}
