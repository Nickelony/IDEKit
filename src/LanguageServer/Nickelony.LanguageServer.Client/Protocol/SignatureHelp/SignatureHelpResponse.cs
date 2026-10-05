using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents the typed top-level signature-help payload returned by a language server.</summary>
/// <remarks>
/// A JSON <see langword="null"/> response deserializes to a <see langword="null"/> response, and a payload without a <c>signatures</c> property
/// leaves <see cref="Signatures"/> as <see langword="null"/>. Malformed signature and parameter elements become empty placeholders instead of failing
/// the whole response. That keeps the <c>activeSignature</c> and <c>activeParameter</c> indexes aligned with the entries the server sent.
/// A signature element that is not an object, and a parameter element that is neither an object nor a JSON <see langword="null"/>, log a warning.
/// </remarks>
[JsonConverter(typeof(SignatureHelpResponseJsonConverter))]
public sealed class SignatureHelpResponse
{
	/// <summary>The index of the active signature.</summary>
	[JsonPropertyName("activeSignature")]
	public int? ActiveSignature { get; init; }

	/// <summary>The raw <c>activeParameter</c> payload value.</summary>
	/// <remarks>
	/// <see cref="JsonValueKind.Undefined"/> means the property was absent, <see cref="JsonValueKind.Null"/> means the server reported
	/// no active parameter, and a number carries the supplied index.
	/// </remarks>
	[JsonPropertyName("activeParameter")]
	public JsonElement ActiveParameter { get; init; }

	[JsonPropertyName("signatures")]
	public IReadOnlyList<SignatureInformationPayload>? Signatures { get; init; }
}
