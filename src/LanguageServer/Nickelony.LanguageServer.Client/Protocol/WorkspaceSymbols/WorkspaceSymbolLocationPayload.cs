using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>
/// Represents the location of a workspace symbol: a document URI and, when the server has computed it, the range inside the document.
/// </summary>
/// <param name="Uri">The document URI.</param>
/// <param name="Range">
/// The range of the symbol in the document, or <see langword="null"/> when the server answers it only through <c>workspaceSymbol/resolve</c>.
/// </param>
/// <remarks>
/// The protocol allows a <c>WorkspaceSymbol</c> to carry a bare <c>{ uri }</c> location, so the range is optional.
/// The members stay nullable so a missing member does not fail the deserialization of an entire response; a present but malformed
/// range still throws <see cref="System.Text.Json.JsonException"/>. A range that is <see langword="null"/> is not written, so a symbol that goes
/// back to the server keeps the shape it arrived in.
/// </remarks>
public readonly record struct WorkspaceSymbolLocationPayload(
	[property: JsonPropertyName("uri")] string? Uri,
	[property: JsonPropertyName("range")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RangePayload? Range
);
