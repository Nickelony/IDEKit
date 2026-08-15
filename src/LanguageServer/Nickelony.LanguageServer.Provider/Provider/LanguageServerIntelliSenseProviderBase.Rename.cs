namespace Nickelony.LanguageServer.Provider;

public abstract partial class LanguageServerIntelliSenseProviderBase
{
	/// <inheritdoc/>
	public virtual async Task<TextWorkspaceEdit?> RenameSymbolAsync(LanguageServerRenameRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (string.IsNullOrWhiteSpace(request.NewName))
			return null;

		return await SendDocumentRequestAsync<WorkspaceEditResponse?, TextWorkspaceEdit?>(
			request.FilePath, request.DocumentText, LspMethodNames.Rename,
			supportsRequest: static client => client.SupportsRename,
			buildParameters: textDocument => new RenameParams(textDocument,
				ToProtocolPosition(request.Position), request.NewName),
			parseResponse: response => ResponseParser.ParseWorkspaceEdit(response, Logger),
			fallbackValue: null,
			cancellationToken).ConfigureAwait(false);
	}
}
