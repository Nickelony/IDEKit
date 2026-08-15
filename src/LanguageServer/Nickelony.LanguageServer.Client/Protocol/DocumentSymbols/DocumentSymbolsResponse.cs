using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents the typed top-level document-symbol payload returned by a language server.
/// </summary>
/// <remarks>
/// A JSON <see langword="null"/> response deserializes to a <see langword="null"/> response, and a payload that is not an array
/// leaves <see cref="Symbols"/> empty. Malformed symbol entries are skipped with a warning instead of failing the
/// whole response, and the surviving entries keep their response order. Hierarchical and flat entries share the
/// <see cref="DocumentSymbolPayload"/> shape and can be mixed in one response.
/// </remarks>
[JsonConverter(typeof(DocumentSymbolsResponseJsonConverter))]
public sealed class DocumentSymbolsResponse
{
	/// <summary>
	/// Initializes a new instance of the <see cref="DocumentSymbolsResponse"/> class.
	/// </summary>
	public DocumentSymbolsResponse()
		: this(null)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="DocumentSymbolsResponse"/> class.
	/// </summary>
	/// <param name="symbols">The usable symbol entries returned by the server.</param>
	public DocumentSymbolsResponse(IReadOnlyList<DocumentSymbolPayload>? symbols)
		=> Symbols = symbols is null ? [] : Array.AsReadOnly([.. symbols]);

	/// <summary>
	/// Gets the usable symbol entries returned by the server, in response order.
	/// </summary>
	public IReadOnlyList<DocumentSymbolPayload> Symbols { get; }
}
