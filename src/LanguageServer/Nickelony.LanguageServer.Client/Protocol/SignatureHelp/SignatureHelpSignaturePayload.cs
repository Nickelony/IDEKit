using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a single signature entry within a signature-help response.
/// </summary>
/// <remarks>
/// Malformed parameter elements are replaced with empty placeholders instead of being dropped, so the
/// <c>activeParameter</c> index stays aligned with the server's parameter array.
/// </remarks>
[JsonConverter(typeof(SignatureHelpSignaturePayloadJsonConverter))]
public sealed record SignatureHelpSignaturePayload
{
	/// <summary>
	/// Gets the display label for the signature.
	/// </summary>
	[JsonPropertyName("label")]
	public string? Label { get; init; }

	/// <summary>
	/// Gets the documentation payload for the signature.
	/// </summary>
	[JsonPropertyName("documentation")]
	public JsonElement? Documentation { get; init; }

	/// <summary>
	/// Gets the raw <c>activeParameter</c> payload value. <see cref="JsonValueKind.Undefined"/> means
	/// the property was absent and the payload-level index applies, <see cref="JsonValueKind.Null"/>
	/// is the LSP 3.18 "no active parameter" state, and a number carries the supplied index.
	/// </summary>
	[JsonPropertyName("activeParameter")]
	public JsonElement ActiveParameter { get; init; }

	/// <summary>
	/// Gets the parameter entries defined by the signature.
	/// </summary>
	[JsonPropertyName("parameters")]
	public IReadOnlyList<SignatureHelpParameterPayload>? Parameters { get; init; }
}
