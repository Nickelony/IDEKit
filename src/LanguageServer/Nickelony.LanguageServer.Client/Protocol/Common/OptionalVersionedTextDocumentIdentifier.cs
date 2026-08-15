using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Identifies a text document together with the optional document version a payload refers to.
/// </summary>
/// <param name="Uri">The document URI.</param>
/// <param name="Version">
/// The document version the payload applies to, or <see langword="null"/> when the payload applies to whatever
/// version the document has when it arrives (or when the server omitted the version).
/// </param>
/// <remarks>
/// LSP models this shape as <c>OptionalVersionedTextDocumentIdentifier</c>: a <see langword="null"/> version
/// explicitly means "apply to the current version", while a numeric version lets a host reject edits computed
/// against a stale document. Workspace-edit payloads use this shape instead of the plain <see cref="TextDocumentIdentifier"/> so a
/// server-sent version round-trips.
/// </remarks>
public readonly record struct OptionalVersionedTextDocumentIdentifier(
	[property: JsonPropertyName("uri")] string Uri,
	[property: JsonPropertyName("version")] int? Version);
