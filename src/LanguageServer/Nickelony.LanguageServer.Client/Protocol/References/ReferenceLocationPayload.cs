using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a single typed reference location returned by a language server.
/// </summary>
/// <remarks>
/// Both members stay nullable so a malformed entry is kept with null members (a host decides whether to skip
/// it) instead of failing the whole references response.
/// </remarks>
public sealed record ReferenceLocationPayload
{
	/// <summary>
	/// Gets the referenced document URI.
	/// </summary>
	[JsonPropertyName("uri")]
	public string? Uri { get; init; }

	/// <summary>
	/// Gets the referenced document range.
	/// </summary>
	[JsonPropertyName("range")]
	public ProtocolRangePayload? Range { get; init; }
}
