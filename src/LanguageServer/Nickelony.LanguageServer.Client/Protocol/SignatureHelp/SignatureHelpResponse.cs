using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents the typed top-level signature-help payload returned by a language server.
/// </summary>
/// <remarks>
/// A JSON <see langword="null"/> response deserializes to a <see langword="null"/> response, and a payload without a
/// <c>signatures</c> property leaves <see cref="Signatures"/> as <see langword="null"/>. Malformed signature and
/// parameter elements are replaced with empty placeholders instead of failing the whole response, so
/// <c>activeSignature</c> and <c>activeParameter</c> indexes stay aligned with the entries the server sent;
/// an element that is neither an object nor a JSON null logs a warning.
/// </remarks>
[JsonConverter(typeof(SignatureHelpResponseJsonConverter))]
public sealed class SignatureHelpResponse
{
	/// <summary>
	/// Gets the index of the active signature.
	/// </summary>
	[JsonPropertyName("activeSignature")]
	public int? ActiveSignature { get; init; }

	/// <summary>
	/// Gets the raw <c>activeParameter</c> payload value.
	/// </summary>
	/// <remarks>
	/// <see cref="JsonValueKind.Undefined"/> means the property was absent and the protocol's default index rules
	/// apply, <see cref="JsonValueKind.Null"/> is the LSP 3.18 "no active parameter" state, and a number carries the
	/// supplied index.
	/// </remarks>
	[JsonPropertyName("activeParameter")]
	public JsonElement ActiveParameter { get; init; }

	/// <summary>
	/// Gets the available signature entries.
	/// </summary>
	[JsonPropertyName("signatures")]
	public IReadOnlyList<SignatureHelpSignaturePayload>? Signatures { get; init; }
}
