using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Identifies a versioned text document in protocol notifications.
/// </summary>
/// <param name="Uri">The document URI.</param>
/// <param name="Version">The current document version.</param>
/// <remarks>
/// The notification shape requires the version, unlike the workspace-edit shape that uses
/// <see cref="OptionalVersionedTextDocumentIdentifier"/> with an optional version.
/// </remarks>
public readonly record struct VersionedTextDocumentIdentifier(
	[property: JsonPropertyName("uri")] string Uri,
	[property: JsonPropertyName("version")] int Version);
