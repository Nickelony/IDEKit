using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents the typed result of the LSP initialize request.
/// </summary>
internal sealed record InitializeResponse
{
	/// <summary>
	/// Gets the server capabilities advertised during initialization.
	/// </summary>
	[JsonPropertyName("capabilities")]
	public ServerCapabilities? Capabilities { get; init; }
}
