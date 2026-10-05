using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>Converts the initialize response into the negotiated server capabilities and rejects a server the client cannot work with.</summary>
internal static class NegotiatedServerCapabilitiesConversion
{
	private static readonly IReadOnlyList<string> s_emptyCapabilityList = Array.AsReadOnly<string>([]);

	/// <summary>Converts one initialize response into the negotiated server capabilities.</summary>
	/// <param name="initializeResponse">The initialize response received from the server.</param>
	/// <param name="requireTextDocumentSynchronization">
	/// Whether the conversion fails when the server does not advertise full or incremental text synchronization.
	/// </param>
	/// <returns>The negotiated capabilities.</returns>
	/// <exception cref="NotSupportedException">
	/// The initialize response has no server capabilities payload; the server selected a position encoding other than UTF-16; or the server
	/// did not advertise the text synchronization the client is configured to require.
	/// </exception>
	/// <remarks>
	/// A plain provider flag is <see langword="false"/> when the server
	/// did not advertise it; the object-derived flags (<c>completionItem/resolve</c>, <c>codeAction/resolve</c>, <c>textDocument/semanticTokens/full</c>)
	/// are <see langword="false"/> when their object form is absent.
	/// </remarks>
	internal static NegotiatedServerCapabilities FromInitializeResponse(
		InitializeResponse? initializeResponse,
		bool requireTextDocumentSynchronization
	)
	{
		ServerCapabilities capabilities =
			initializeResponse?.Capabilities
			?? throw new NotSupportedException("The initialize response did not contain a server capabilities payload.");

		if (
			capabilities.PositionEncoding is { Length: > 0 } positionEncoding
			&& !string.Equals(positionEncoding, "utf-16", StringComparison.OrdinalIgnoreCase)
		)
		{
			throw new NotSupportedException(
				$"The server selected position encoding '{positionEncoding}', which this client does not support; the client only implements UTF-16 positions."
			);
		}

		NegotiatedServerCapabilities negotiated = Read(capabilities);

		if (negotiated.TextDocumentSyncKind == TextDocumentSyncKind.None && requireTextDocumentSynchronization)
		{
			throw new NotSupportedException(
				"The server did not advertise full or incremental text synchronization, which the client is configured to require."
			);
		}

		return negotiated;
	}

	private static NegotiatedServerCapabilities Read(ServerCapabilities capabilities)
	{
		IReadOnlyList<string> semanticTokenTypes = s_emptyCapabilityList;
		IReadOnlyList<string> semanticTokenModifiers = s_emptyCapabilityList;

		bool supportsSemanticTokensFull = false;

		if (capabilities.SemanticTokensProvider is { } semanticTokensProvider)
		{
			supportsSemanticTokensFull = semanticTokensProvider.Full?.IsSupported == true;

			if (semanticTokensProvider.Legend is { } legend)
			{
				semanticTokenTypes = Array.AsReadOnly(legend.TokenTypes ?? []);
				semanticTokenModifiers = Array.AsReadOnly(legend.TokenModifiers ?? []);
			}
		}

		return new NegotiatedServerCapabilities
		{
			TextDocumentSyncKind = capabilities.TextDocumentSync?.Kind ?? TextDocumentSyncKind.None,
			SemanticTokenTypes = semanticTokenTypes,
			SemanticTokenModifiers = semanticTokenModifiers,
			SupportsCompletionResolve = capabilities.CompletionProvider?.ResolveProvider == true,
			CompletionTriggerCharacters = Array.AsReadOnly(capabilities.CompletionProvider?.TriggerCharacters?.ToArray() ?? []),
			SupportsDocumentSymbols = capabilities.DocumentSymbolProvider?.IsSupported == true,
			SupportsWorkspaceSymbols = capabilities.WorkspaceSymbolProvider?.IsSupported == true,
			SupportsWorkspaceSymbolResolve = capabilities.WorkspaceSymbolProvider?.ResolveProvider == true,
			SupportsCodeActions = capabilities.CodeActionProvider?.IsSupported == true,
			SupportsCodeActionResolve = capabilities.CodeActionProvider?.ResolveProvider == true,
			ExecuteCommands = Array.AsReadOnly(capabilities.ExecuteCommandProvider?.Commands?.ToArray() ?? []),
			SupportsReferences = capabilities.ReferencesProvider?.IsSupported == true,
			SupportsRename = capabilities.RenameProvider?.IsSupported == true,
			SupportsFormatting = capabilities.DocumentFormattingProvider?.IsSupported == true,
			SupportsHover = capabilities.HoverProvider?.IsSupported == true,
			SupportsDefinition = capabilities.DefinitionProvider?.IsSupported == true,
			SupportsSignatureHelp = capabilities.SignatureHelpProvider?.IsSupported == true,
			SignatureHelpTriggerCharacters = Array.AsReadOnly(capabilities.SignatureHelpProvider?.TriggerCharacters.ToArray() ?? []),
			SignatureHelpRetriggerCharacters = Array.AsReadOnly(capabilities.SignatureHelpProvider?.RetriggerCharacters.ToArray() ?? []),
			SupportsSemanticTokensFull = supportsSemanticTokensFull,
			SupportsPullDiagnostics = capabilities.DiagnosticProvider?.IsSupported == true,
		};
	}
}
