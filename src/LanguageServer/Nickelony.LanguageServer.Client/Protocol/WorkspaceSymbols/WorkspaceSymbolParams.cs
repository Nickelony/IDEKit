using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>The parameters of a <c>workspace/symbol</c> request.</summary>
/// <remarks>
/// The request carries no partial-result token, so a server sends its whole answer in the response. The server filters by the query, and the matching
/// rule is the server's own.
/// </remarks>
public readonly record struct WorkspaceSymbolParams([property: JsonPropertyName("query")] string Query);
