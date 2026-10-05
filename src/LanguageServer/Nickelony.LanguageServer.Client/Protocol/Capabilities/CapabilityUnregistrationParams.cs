using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents a dynamic capability unregistration request from the language server.</summary>
/// <param name="Unregistrations">The requested capability removals. Null entries are skipped during binding.</param>
/// <remarks>
/// The reader accepts both <c>unregistrations</c> and the misspelled <c>unregisterations</c> that the protocol uses.
/// </remarks>
[JsonConverter(typeof(CapabilityUnregistrationParamsJsonConverter))]
internal readonly record struct CapabilityUnregistrationParams(CapabilityUnregistrationPayload[]? Unregistrations);

internal readonly record struct CapabilityUnregistrationPayload(
	[property: JsonPropertyName("id")] string? Id,
	[property: JsonPropertyName("method")] string? Method
);
