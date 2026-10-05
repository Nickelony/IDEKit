using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Identifies a text document by its URI in the protocol.</summary>
public readonly record struct TextDocumentIdentifier([property: JsonPropertyName("uri")] string Uri);
