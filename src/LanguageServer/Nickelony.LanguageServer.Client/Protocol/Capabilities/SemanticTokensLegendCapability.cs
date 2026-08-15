using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents the semantic tokens legend advertised by the server.
/// </summary>
/// <remarks>
/// The token type and modifier arrays are caller-owned DTO storage that should be treated as read-only.
/// </remarks>
internal sealed record SemanticTokensLegendCapability
{
	/// <summary>
	/// Gets the token type names advertised by the server.
	/// </summary>
	[JsonPropertyName("tokenTypes")]
	public string[]? TokenTypes { get; init; }

	/// <summary>
	/// Gets the token modifier names advertised by the server.
	/// </summary>
	[JsonPropertyName("tokenModifiers")]
	public string[]? TokenModifiers { get; init; }
}
