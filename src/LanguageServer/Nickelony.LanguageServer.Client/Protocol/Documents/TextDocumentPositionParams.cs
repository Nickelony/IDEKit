using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>The parameters of a request that targets one position in a text document.</summary>
public readonly record struct TextDocumentPositionParams(
	[property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
	[property: JsonPropertyName("position")] PositionPayload Position
);
