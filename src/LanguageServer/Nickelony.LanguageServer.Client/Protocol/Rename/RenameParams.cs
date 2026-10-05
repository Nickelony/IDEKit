using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <param name="TextDocument">The targeted document.</param>
/// <param name="Position">The symbol position to rename.</param>
/// <param name="NewName">The replacement symbol name.</param>
public readonly record struct RenameParams(
	[property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
	[property: JsonPropertyName("position")] PositionPayload Position,
	[property: JsonPropertyName("newName")] string NewName
);
