using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Describes the signature-help trigger context for a signature-help request.
/// </summary>
/// <param name="TriggerKind">The typed signature-help trigger kind.</param>
/// <param name="IsRetrigger">
/// <see langword="true"/> when signature help was already showing when it was triggered.
/// </param>
/// <param name="TriggerCharacter">The trigger character when signature help was character-triggered.</param>
/// <param name="ActiveSignatureHelp">
/// The signature help that is currently shown, when available; a server can use it to keep the
/// selected overload stable across retriggers.
/// </param>
public readonly record struct SignatureHelpContextPayload(
	[property: JsonPropertyName("triggerKind")] SignatureHelpTriggerKind TriggerKind,
	[property: JsonPropertyName("isRetrigger")] bool IsRetrigger,
	[property: JsonPropertyName("triggerCharacter")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? TriggerCharacter = null,
	[property: JsonPropertyName("activeSignatureHelp")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	SignatureHelpResponse? ActiveSignatureHelp = null);

/// <summary>
/// Represents a signature-help request payload.
/// </summary>
/// <param name="TextDocument">The targeted document.</param>
/// <param name="Position">The caret position for the signature-help request.</param>
/// <param name="Context">The trigger context, or <see langword="null"/> for a position-only request.</param>
public readonly record struct SignatureHelpParams(
	[property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
	[property: JsonPropertyName("position")] ProtocolPosition Position,
	[property: JsonPropertyName("context")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	SignatureHelpContextPayload? Context = null);
