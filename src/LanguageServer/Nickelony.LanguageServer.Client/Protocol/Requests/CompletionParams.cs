using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Describes the completion trigger context for a completion request.
/// </summary>
/// <param name="TriggerKind">The typed completion trigger kind.</param>
/// <param name="TriggerCharacter">The trigger character when completion was character-triggered.</param>
public readonly record struct CompletionContextPayload(
	[property: JsonPropertyName("triggerKind")] CompletionTriggerKind TriggerKind,
	[property: JsonPropertyName("triggerCharacter")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? TriggerCharacter = null);

/// <summary>
/// Represents a completion request payload.
/// </summary>
/// <param name="TextDocument">The targeted document.</param>
/// <param name="Position">The caret position for the completion request.</param>
/// <param name="Context">The completion trigger context, or <see langword="null"/> when the request carries none.</param>
public readonly record struct CompletionParams(
	[property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
	[property: JsonPropertyName("position")] ProtocolPosition Position,
	[property: JsonPropertyName("context")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	CompletionContextPayload? Context = null);
