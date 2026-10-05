using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents the subset of server capabilities consumed by this client.</summary>
internal sealed record ServerCapabilities
{
	[JsonPropertyName("textDocumentSync")]
	public TextDocumentSyncCapability? TextDocumentSync { get; init; }

	[JsonPropertyName("completionProvider")]
	public CompletionProviderCapability? CompletionProvider { get; init; }

	[JsonPropertyName("hoverProvider")]
	public SupportedCapability? HoverProvider { get; init; }

	[JsonPropertyName("definitionProvider")]
	public SupportedCapability? DefinitionProvider { get; init; }

	[JsonPropertyName("documentSymbolProvider")]
	public SupportedCapability? DocumentSymbolProvider { get; init; }

	/// <summary>The workspace-symbol provider capability, including whether the server supports <c>workspaceSymbol/resolve</c>.</summary>
	[JsonPropertyName("workspaceSymbolProvider")]
	public WorkspaceSymbolProviderCapability? WorkspaceSymbolProvider { get; init; }

	/// <summary>The code-action provider capability, including whether the server supports <c>codeAction/resolve</c>.</summary>
	[JsonPropertyName("codeActionProvider")]
	public CodeActionProviderCapability? CodeActionProvider { get; init; }

	/// <summary>The commands the server executes through <c>workspace/executeCommand</c>.</summary>
	[JsonPropertyName("executeCommandProvider")]
	public ExecuteCommandProviderCapability? ExecuteCommandProvider { get; init; }

	[JsonPropertyName("referencesProvider")]
	public SupportedCapability? ReferencesProvider { get; init; }

	[JsonPropertyName("renameProvider")]
	public SupportedCapability? RenameProvider { get; init; }

	[JsonPropertyName("documentFormattingProvider")]
	public SupportedCapability? DocumentFormattingProvider { get; init; }

	[JsonPropertyName("signatureHelpProvider")]
	public SignatureHelpProviderCapability? SignatureHelpProvider { get; init; }

	[JsonPropertyName("semanticTokensProvider")]
	public SemanticTokensProviderCapability? SemanticTokensProvider { get; init; }

	/// <summary>The pull-diagnostics provider capability.</summary>
	/// <remarks>
	/// Only the presence is modeled; the object's inner fields are not.
	/// </remarks>
	[JsonPropertyName("diagnosticProvider")]
	public SupportedCapability? DiagnosticProvider { get; init; }

	/// <summary>The position encoding the server selected for this session, when it advertised one.</summary>
	[JsonPropertyName("positionEncoding")]
	public string? PositionEncoding { get; init; }
}
