using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents LSP <c>MarkupContent</c>: text together with the markup kind that says how to interpret it.</summary>
/// <remarks>
/// A member typed as <c>string | MarkupContent</c> on the wire, such as a completion item's <c>documentation</c>,
/// reads a bare string as <see cref="MarkupKind.PlainText"/>, as the protocol defines it; a JSON value that is neither a string nor a markup object
/// reads as <see langword="null"/>. The payload always writes back as <c>MarkupContent</c>, so a bare string is sent in its object form.
/// </remarks>
[JsonConverter(typeof(MarkupContentPayloadJsonConverter))]
public sealed record MarkupContentPayload
{
	/// <summary>How <see cref="Value"/> is interpreted.</summary>
	[JsonPropertyName("kind")]
	public MarkupKind Kind { get; init; }

	/// <summary>The markup text, empty when the server sent none.</summary>
	[JsonPropertyName("value")]
	public string Value { get; init; } = string.Empty;
}
