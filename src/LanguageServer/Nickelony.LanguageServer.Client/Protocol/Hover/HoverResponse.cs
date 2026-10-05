using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents the typed top-level hover payload returned by a language server.</summary>
/// <remarks>
/// A JSON <see langword="null"/> response deserializes to a <see langword="null"/> response; a payload without a <c>contents</c> property
/// leaves <see cref="Contents"/> empty. A malformed optional <see cref="Range"/> is dropped with a warning instead of failing the response.
/// </remarks>
[JsonConverter(typeof(HoverResponseJsonConverter))]
public sealed class HoverResponse
{
	/// <summary>The hover contents, one entry per fragment the server sent, or an empty list when it sent none.</summary>
	/// <remarks>
	/// <c>contents</c> is <c>MarkupContent | MarkedString | MarkedString[]</c> on the wire. A single value yields one entry,
	/// and an array yields one entry per usable element with its surrounding CR and LF characters removed. A bare string reads as Markdown,
	/// and a <c>MarkedString</c> code block reads as a fenced Markdown block. The list writes back as one <c>MarkupContent</c> object, or as an array
	/// of them for several entries.
	/// </remarks>
	[JsonPropertyName("contents")]
	public IReadOnlyList<MarkupContentPayload> Contents { get; init; } = [];

	/// <summary>The optional protocol range the hover applies to.</summary>
	[JsonPropertyName("range")]
	public RangePayload? Range { get; init; }
}
