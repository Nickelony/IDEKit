using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents the subset of server capabilities consumed by this client.
/// </summary>
internal sealed record ServerCapabilities
{
	/// <summary>
	/// Gets the text-document synchronization capability.
	/// </summary>
	[JsonPropertyName("textDocumentSync")]
	public TextDocumentSyncCapability? TextDocumentSync { get; init; }

	/// <summary>
	/// Gets the completion provider capability.
	/// </summary>
	[JsonPropertyName("completionProvider")]
	public CompletionProviderCapability? CompletionProvider { get; init; }

	/// <summary>
	/// Gets the hover provider capability.
	/// </summary>
	[JsonPropertyName("hoverProvider")]
	public SupportedCapability? HoverProvider { get; init; }

	/// <summary>
	/// Gets the definition provider capability.
	/// </summary>
	[JsonPropertyName("definitionProvider")]
	public SupportedCapability? DefinitionProvider { get; init; }

	/// <summary>
	/// Gets the document-symbol provider capability.
	/// </summary>
	[JsonPropertyName("documentSymbolProvider")]
	public SupportedCapability? DocumentSymbolProvider { get; init; }

	/// <summary>
	/// Gets the code-action provider capability, including whether the server supports <c>codeAction/resolve</c>.
	/// </summary>
	[JsonPropertyName("codeActionProvider")]
	public CodeActionProviderCapability? CodeActionProvider { get; init; }

	/// <summary>
	/// Gets the references provider capability.
	/// </summary>
	[JsonPropertyName("referencesProvider")]
	public SupportedCapability? ReferencesProvider { get; init; }

	/// <summary>
	/// Gets the rename provider capability.
	/// </summary>
	[JsonPropertyName("renameProvider")]
	public SupportedCapability? RenameProvider { get; init; }

	/// <summary>
	/// Gets the document-formatting provider capability.
	/// </summary>
	[JsonPropertyName("documentFormattingProvider")]
	public SupportedCapability? DocumentFormattingProvider { get; init; }

	/// <summary>
	/// Gets the signature-help provider capability.
	/// </summary>
	[JsonPropertyName("signatureHelpProvider")]
	public SupportedCapability? SignatureHelpProvider { get; init; }

	/// <summary>
	/// Gets the semantic tokens provider capability.
	/// </summary>
	[JsonPropertyName("semanticTokensProvider")]
	public SemanticTokensProviderCapability? SemanticTokensProvider { get; init; }

	/// <summary>
	/// Gets the pull-diagnostics provider capability.
	/// </summary>
	/// <remarks>
	/// The server advertises this as an object when it implements <c>textDocument/diagnostic</c>; the object's
	/// inner fields are not modeled because the client only needs to know whether pull diagnostics is available.
	/// </remarks>
	[JsonPropertyName("diagnosticProvider")]
	public SupportedCapability? DiagnosticProvider { get; init; }

	/// <summary>
	/// Gets the position encoding the server selected for this session, when it advertised one.
	/// </summary>
	[JsonPropertyName("positionEncoding")]
	public string? PositionEncoding { get; init; }
}
