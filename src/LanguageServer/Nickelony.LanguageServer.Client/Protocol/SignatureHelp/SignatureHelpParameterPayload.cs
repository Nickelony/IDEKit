using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a single parameter entry within a signature-help response.
/// </summary>
/// <remarks>
/// A placeholder produced for a malformed parameter element carries an undefined label and serializes as an empty
/// parameter object, so a parsed response always round-trips.
/// </remarks>
[JsonConverter(typeof(SignatureHelpParameterPayloadJsonConverter))]
public sealed record SignatureHelpParameterPayload
{
	/// <summary>
	/// Gets the label payload identifying the parameter span or text.
	/// </summary>
	[JsonPropertyName("label")]
	public JsonElement Label { get; init; }

	/// <summary>
	/// Gets the documentation payload for the parameter.
	/// </summary>
	[JsonPropertyName("documentation")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonElement? Documentation { get; init; }
}
