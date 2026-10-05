using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents a single parameter entry within a signature-help response.</summary>
/// <remarks>
/// A placeholder produced for a malformed parameter element carries the empty label and serializes as an empty parameter object, so a parsed response
/// always round-trips.
/// </remarks>
public sealed record ParameterInformationPayload
{
	/// <summary>The label of the parameter, as its text or as its offsets inside the signature label.</summary>
	[JsonPropertyName("label")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public SignatureHelpParameterLabelPayload Label { get; init; }

	/// <summary>The optional documentation of the parameter.</summary>
	/// <remarks>
	/// A bare string reads as plain text, and the payload writes back as <c>MarkupContent</c>; see <see cref="MarkupContentPayload"/>.
	/// </remarks>
	[JsonPropertyName("documentation")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public MarkupContentPayload? Documentation { get; init; }
}
