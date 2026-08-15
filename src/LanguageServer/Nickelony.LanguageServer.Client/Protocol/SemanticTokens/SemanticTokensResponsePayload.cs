using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents the typed top-level response of a full semantic token request.
/// </summary>
/// <param name="Data">The raw semantic token integer stream.</param>
public readonly record struct SemanticTokensResponsePayload(
	[property: JsonPropertyName("data")] int[]? Data);
