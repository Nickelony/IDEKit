using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents an LSP location: a document URI together with a range inside it.</summary>
/// <param name="Uri">The target document URI.</param>
/// <param name="Range">The target range.</param>
/// <remarks>
/// The members stay nullable although LSP marks them required, so a missing member does not fail deserialization of an entire response;
/// a present but malformed <c>range</c> still throws <see cref="System.Text.Json.JsonException"/>. Hosts treat a <see langword="null"/> member
/// as an unusable location.
/// </remarks>
public readonly record struct LocationPayload(
	[property: JsonPropertyName("uri")] string? Uri,
	[property: JsonPropertyName("range")] RangePayload? Range
);
