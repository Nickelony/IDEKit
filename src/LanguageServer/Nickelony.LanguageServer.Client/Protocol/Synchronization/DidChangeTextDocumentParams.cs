using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents the typed payload for a <c>textDocument/didChange</c> notification.</summary>
/// <param name="TextDocument">The changed versioned document identifier.</param>
/// <param name="ContentChanges">The content changes to apply.</param>
public readonly record struct DidChangeTextDocumentParams(
	[property: JsonPropertyName("textDocument")] VersionedTextDocumentIdentifier TextDocument,
	[property: JsonPropertyName("contentChanges")] IReadOnlyList<TextDocumentContentChangeEventPayload> ContentChanges
);

/// <summary>Represents a single text content change within a change notification.</summary>
/// <param name="Text">The replacement text.</param>
/// <param name="Range">The replaced range, or <see langword="null"/> for full-document replacement.</param>
public readonly record struct TextDocumentContentChangeEventPayload(
	[property: JsonPropertyName("text")] string Text,
	[property: JsonPropertyName("range")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RangePayload? Range = null
);
