using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents the typed top-level hover payload returned by a language server.
/// </summary>
/// <remarks>
/// A JSON <see langword="null"/> response deserializes to a <see langword="null"/> response; a payload without a <c>contents</c>
/// property leaves <see cref="Contents"/> as <see langword="null"/>, so the absent state is representable and a
/// present element can always be written back safely. A malformed optional <see cref="Range"/> is dropped instead of
/// failing the response; see <see cref="HoverResponseJsonConverter"/>.
/// </remarks>
[JsonConverter(typeof(HoverResponseJsonConverter))]
public sealed class HoverResponse
{
	/// <summary>
	/// Gets the hover contents payload returned by the language server, or <see langword="null"/> when the payload
	/// carried no contents member.
	/// </summary>
	[JsonPropertyName("contents")]
	public JsonElement? Contents { get; init; }

	/// <summary>
	/// Gets the optional protocol range the hover applies to, or <see langword="null"/> when the
	/// language server returned no range.
	/// </summary>
	[JsonPropertyName("range")]
	public ProtocolRangePayload? Range { get; init; }
}
