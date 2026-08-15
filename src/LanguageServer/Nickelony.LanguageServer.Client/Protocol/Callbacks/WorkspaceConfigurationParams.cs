using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a workspace configuration request payload.
/// </summary>
/// <param name="Items">
/// The requested configuration sections in request order; a null or malformed entry is preserved as a
/// <see cref="WorkspaceConfigurationItem.IsMalformed"/> placeholder so the response stays positionally aligned
/// with the request.
/// </param>
internal readonly record struct WorkspaceConfigurationParams(
	[property: JsonPropertyName("items")] WorkspaceConfigurationItem[]? Items);

/// <summary>
/// Identifies a single configuration section requested from the host.
/// </summary>
/// <param name="Section">The dotted configuration section name; null requests the whole settings root.</param>
/// <param name="IsMalformed">
/// Whether the request entry was null or malformed. The host answers a malformed entry with a null
/// configuration value, because there is no section to resolve for it.
/// </param>
/// <remarks>
/// LSP allows a request item to carry a <c>scopeUri</c> so a server can ask for per-resource configuration. This
/// item models the section only, and the client answers every item from its single global settings snapshot, so
/// per-resource scopes are not honored. The response array matches the request entry count one-for-one in
/// request order, which LSP requires.
/// </remarks>
internal readonly record struct WorkspaceConfigurationItem(
	[property: JsonPropertyName("section")] string? Section,
	bool IsMalformed = false);
