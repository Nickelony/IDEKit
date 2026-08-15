using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents an LSP location: a document URI together with a range inside it.
/// </summary>
/// <param name="Uri">The target document URI.</param>
/// <param name="Range">The target range.</param>
/// <remarks>
/// Both members stay nullable although LSP marks them required, so a malformed entry cannot fail deserialization
/// of an entire response; hosts treat a null member as an unusable location. A malformed range object still
/// fails deserialization through <see cref="ProtocolRangePayloadJsonConverter"/>.
/// </remarks>
public readonly record struct ProtocolLocationPayload(
	[property: JsonPropertyName("uri")] string? Uri,
	[property: JsonPropertyName("range")] ProtocolRangePayload? Range);
