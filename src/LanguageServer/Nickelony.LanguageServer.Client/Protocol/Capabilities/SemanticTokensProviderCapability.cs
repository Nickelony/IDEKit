using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents semantic tokens capabilities advertised by the server.
/// </summary>
internal sealed record SemanticTokensProviderCapability
{
	/// <summary>
	/// Gets the semantic tokens full-refresh capability.
	/// </summary>
	[JsonPropertyName("full")]
	public SemanticTokensFullCapability? Full { get; init; }

	/// <summary>
	/// Gets the semantic tokens legend advertised by the server.
	/// </summary>
	[JsonPropertyName("legend")]
	public SemanticTokensLegendCapability? Legend { get; init; }
}
