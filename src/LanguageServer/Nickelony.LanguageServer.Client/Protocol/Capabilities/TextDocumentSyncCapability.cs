using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents the text-document synchronization capability advertised by the server.
/// </summary>
/// <param name="Kind">The negotiated synchronization kind.</param>
[JsonConverter(typeof(TextDocumentSyncCapabilityJsonConverter))]
internal readonly record struct TextDocumentSyncCapability(TextDocumentSyncKind Kind);
