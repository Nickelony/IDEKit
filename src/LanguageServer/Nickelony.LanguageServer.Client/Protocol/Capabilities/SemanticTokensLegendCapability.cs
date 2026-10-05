using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents the semantic tokens legend advertised by the server.</summary>
/// <remarks>
/// The token type and modifier arrays are not copied, so treat them as read-only.
/// </remarks>
internal readonly record struct SemanticTokensLegendCapability
{
	[JsonPropertyName("tokenTypes")]
	public string[]? TokenTypes { get; init; }

	[JsonPropertyName("tokenModifiers")]
	public string[]? TokenModifiers { get; init; }
}
