using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents one document-symbol entry returned by a language server.
/// </summary>
/// <remarks>
/// <para>
/// The payload covers both protocol response shapes with one tolerant type: hierarchical
/// <c>DocumentSymbol</c> entries carry <see cref="Range"/>, <see cref="SelectionRange"/>, and optionally
/// <see cref="Children"/>, while flat <c>SymbolInformation</c> entries carry <see cref="Location"/> and optionally
/// <see cref="ContainerName"/>. The shape is decided per element by the presence of a usable
/// <see cref="Location"/>, so a mixed response stays readable even though the protocol expects a single shape per
/// response.
/// </para>
/// <para>
/// <see cref="DocumentSymbolsResponseJsonConverter"/> enforces the skip rules, so a payload that reached a
/// host is the usable subset of the response's entries: <see cref="Name"/> and <see cref="Kind"/> are
/// present, and at least one of <see cref="Range"/>, <see cref="SelectionRange"/>, or <see cref="Location"/> is
/// present. Range values are stored as supplied; range validation is left to the consuming conversion.
/// </para>
/// </remarks>
public sealed class DocumentSymbolPayload
{
	/// <summary>
	/// Gets the display name of the symbol.
	/// </summary>
	[JsonPropertyName("name")]
	public string? Name { get; init; }

	/// <summary>
	/// Gets the optional short detail line shown beside the name.
	/// </summary>
	[JsonPropertyName("detail")]
	public string? Detail { get; init; }

	/// <summary>
	/// Gets the protocol symbol kind.
	/// </summary>
	[JsonPropertyName("kind")]
	public SymbolKind Kind { get; init; }

	/// <summary>
	/// Gets the optional symbol tags such as deprecated.
	/// </summary>
	[JsonPropertyName("tags")]
	public IReadOnlyList<SymbolTag>? Tags { get; init; }

	/// <summary>
	/// Gets a value indicating whether the symbol is deprecated, when the server sent the flag.
	/// </summary>
	[JsonPropertyName("deprecated")]
	public bool? Deprecated { get; init; }

	/// <summary>
	/// Gets the full range of the symbol; present for hierarchical entries.
	/// </summary>
	[JsonPropertyName("range")]
	public ProtocolRangePayload? Range { get; init; }

	/// <summary>
	/// Gets the range of the symbol name; present for hierarchical entries.
	/// </summary>
	[JsonPropertyName("selectionRange")]
	public ProtocolRangePayload? SelectionRange { get; init; }

	/// <summary>
	/// Gets the location of a flat symbol entry; present for flat entries.
	/// </summary>
	[JsonPropertyName("location")]
	public SymbolLocationPayload? Location { get; init; }

	/// <summary>
	/// Gets the optional container name of a flat symbol entry.
	/// </summary>
	[JsonPropertyName("containerName")]
	public string? ContainerName { get; init; }

	/// <summary>
	/// Gets the nested symbols of a hierarchical entry, in response order; flat entries carry none.
	/// </summary>
	[JsonPropertyName("children")]
	public IReadOnlyList<DocumentSymbolPayload>? Children { get; init; }
}
