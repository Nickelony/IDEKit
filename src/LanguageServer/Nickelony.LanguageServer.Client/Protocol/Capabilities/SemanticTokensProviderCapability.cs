using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents semantic tokens capabilities advertised by the server.</summary>
internal readonly record struct SemanticTokensProviderCapability
{
	[JsonPropertyName("full")]
	public SemanticTokensFullCapability? Full { get; init; }

	[JsonPropertyName("legend")]
	public SemanticTokensLegendCapability? Legend { get; init; }
}
