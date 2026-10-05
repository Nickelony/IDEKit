using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents the typed top-level document-symbol payload returned by a language server.</summary>
/// <remarks>
/// Contains the usable symbol entries in response order. An entry without a usable name, kind, and range or location is skipped with a warning,
/// and a payload that is not an array yields an empty response.
/// </remarks>
[JsonConverter(typeof(DocumentSymbolResponseJsonConverter))]
public sealed class DocumentSymbolResponse
{
	public DocumentSymbolResponse()
		: this(null) { }

	/// <param name="symbols">The usable symbol entries returned by the server.</param>
	public DocumentSymbolResponse(IReadOnlyList<DocumentSymbolPayload>? symbols) => Symbols = symbols is null ? [] : Array.AsReadOnly([.. symbols]);

	/// <summary>The usable symbol entries returned by the server, in response order.</summary>
	public IReadOnlyList<DocumentSymbolPayload> Symbols { get; }
}
