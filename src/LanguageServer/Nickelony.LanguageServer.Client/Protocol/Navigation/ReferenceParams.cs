using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Describes how reference requests should treat declarations.</summary>
/// <param name="IncludeDeclaration">Whether declaration locations should be included in the response.</param>
public readonly record struct ReferenceContextPayload([property: JsonPropertyName("includeDeclaration")] bool IncludeDeclaration);

/// <param name="TextDocument">The targeted document.</param>
/// <param name="Position">The symbol position to query.</param>
/// <param name="Context">The request context controlling declaration inclusion.</param>
public readonly record struct ReferenceParams(
	[property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
	[property: JsonPropertyName("position")] PositionPayload Position,
	[property: JsonPropertyName("context")] ReferenceContextPayload Context
);
