using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <param name="Items">
/// The requested configuration sections in request order; a <see langword="null"/> or malformed entry is preserved
/// as a <see cref="WorkspaceConfigurationItemPayload.IsMalformed"/> placeholder so the response stays positionally aligned with the request.
/// </param>
internal readonly record struct WorkspaceConfigurationParams([property: JsonPropertyName("items")] WorkspaceConfigurationItemPayload[]? Items);

/// <summary>Identifies a single configuration section requested from the host.</summary>
/// <param name="Section">The dotted configuration section name; <see langword="null"/> requests the whole settings root.</param>
/// <param name="IsMalformed">
/// Whether the request entry was <see langword="null"/> or malformed. The host answers a malformed entry
/// with a <see langword="null"/> configuration value, because there is no section to resolve for it.
/// </param>
/// <remarks>
/// The item models <c>section</c> only; <c>scopeUri</c> is not honored because every item is answered from the single global settings snapshot.
/// </remarks>
internal readonly record struct WorkspaceConfigurationItemPayload([property: JsonPropertyName("section")] string? Section, bool IsMalformed = false);
