using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents a single signature entry within a signature-help response.</summary>
/// <remarks>
/// Malformed parameter elements are replaced with empty placeholders instead of being dropped, so the <c>activeParameter</c> index stays aligned
/// with the server's parameter array.
/// </remarks>
[JsonConverter(typeof(SignatureInformationPayloadJsonConverter))]
public sealed record SignatureInformationPayload
{
	/// <summary>The display label for the signature.</summary>
	[JsonPropertyName("label")]
	public string? Label { get; init; }

	/// <summary>The optional documentation of the signature.</summary>
	/// <remarks>
	/// A bare string reads as plain text, and the payload writes back as <c>MarkupContent</c>; see <see cref="MarkupContentPayload"/>.
	/// </remarks>
	[JsonPropertyName("documentation")]
	public MarkupContentPayload? Documentation { get; init; }

	/// <summary>The raw <c>activeParameter</c> payload value.</summary>
	/// <remarks>
	/// <see cref="JsonValueKind.Undefined"/> means the property was absent, <see cref="JsonValueKind.Null"/> means the server reported
	/// no active parameter, and a number carries the supplied index.
	/// </remarks>
	[JsonPropertyName("activeParameter")]
	public JsonElement ActiveParameter { get; init; }

	/// <summary>The parameter entries defined by the signature.</summary>
	[JsonPropertyName("parameters")]
	public IReadOnlyList<ParameterInformationPayload>? Parameters { get; init; }
}
