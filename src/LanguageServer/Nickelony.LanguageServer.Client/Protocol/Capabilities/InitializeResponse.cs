using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents the typed result of the LSP initialize request.</summary>
internal sealed record InitializeResponse
{
	[JsonPropertyName("capabilities")]
	public ServerCapabilities? Capabilities { get; init; }
}
