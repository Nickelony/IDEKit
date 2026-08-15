using Nickelony.IDEKit.IntelliSense.Navigation;

namespace Nickelony.LanguageServer.Provider;

public abstract partial class LanguageServerIntelliSenseProviderBase
{
	/// <inheritdoc/>
	public virtual Task<TextDefinitionLocation?> GetDefinitionAsync(LanguageServerDefinitionRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		return SendDocumentPositionRequestAsync<DefinitionResponse?, TextDefinitionLocation?>(
			request.FilePath, request.DocumentText, request.Position, LspMethodNames.Definition,
			static (textDocument, position) => new TextDocumentPositionParams(textDocument, position),
			ResponseParser.ParseDefinitionLocation,
			fallbackValue: null,
			cancellationToken);
	}
}
