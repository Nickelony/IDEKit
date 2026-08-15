using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents completion-specific capabilities advertised by the server.
/// </summary>
internal sealed record CompletionProviderCapability
{
	/// <summary>
	/// Gets a value indicating whether completion-item resolve is supported.
	/// </summary>
	[JsonPropertyName("resolveProvider")]
	public bool? ResolveProvider { get; init; }
}
