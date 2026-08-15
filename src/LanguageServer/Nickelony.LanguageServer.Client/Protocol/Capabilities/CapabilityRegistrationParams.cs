using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a dynamic capability registration request from the language server.
/// </summary>
/// <param name="Registrations">The requested capability registrations. Null entries are skipped during binding.</param>
/// <remarks>
/// This payload is a subset of the protocol shape (<c>registerOptions</c> is not modeled), and the registrations
/// array is caller-owned DTO storage that should be treated as read-only.
/// </remarks>
internal readonly record struct CapabilityRegistrationParams(
	[property: JsonPropertyName("registrations")] CapabilityRegistrationPayload[]? Registrations);

/// <summary>
/// Represents one dynamic capability registration entry requested by the language server.
/// </summary>
/// <param name="Id">The server-defined registration identifier.</param>
/// <param name="Method">The capability method being registered.</param>
internal readonly record struct CapabilityRegistrationPayload(
	[property: JsonPropertyName("id")] string? Id,
	[property: JsonPropertyName("method")] string? Method);
