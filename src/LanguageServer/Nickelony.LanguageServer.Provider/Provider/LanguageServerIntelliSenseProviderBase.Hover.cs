using Nickelony.IDEKit.IntelliSense.Hover;

namespace Nickelony.LanguageServer.Provider;

public abstract partial class LanguageServerIntelliSenseProviderBase
{
	/// <inheritdoc/>
	public virtual Task<TextHoverInfo?> GetHoverAsync(LanguageServerHoverRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		return SendDocumentPositionRequestAsync<HoverResponse?, TextHoverInfo?>(
			request.FilePath, request.DocumentText, request.Position, LspMethodNames.Hover,
			static (textDocument, position) => new TextDocumentPositionParams(textDocument, position),
			response => ResponseParser.ParseHoverInfo(response, request.DocumentText),
			fallbackValue: null,
			cancellationToken);
	}
}
