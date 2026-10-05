using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents a dynamic capability registration request from the language server.</summary>
/// <param name="Registrations">The requested capability registrations. Null entries are skipped during binding.</param>
/// <remarks>
/// This payload is a subset of the protocol shape (<c>registerOptions</c> is not modeled). The array is not copied, so treat it as read-only.
/// </remarks>
internal readonly record struct CapabilityRegistrationParams(
	[property: JsonPropertyName("registrations")] CapabilityRegistrationPayload[]? Registrations
);

internal readonly record struct CapabilityRegistrationPayload(
	[property: JsonPropertyName("id")] string? Id,
	[property: JsonPropertyName("method")] string? Method
);
