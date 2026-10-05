using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents one document-symbol entry returned by a language server.</summary>
/// <remarks>
/// The payload covers the protocol's response shapes with one type: hierarchical <c>DocumentSymbol</c> entries
/// and flat <c>SymbolInformation</c> entries. Each element keeps whichever of <c>range</c>, <c>selectionRange</c>, and <c>location</c> it carries,
/// so a mixed response stays readable even though the protocol expects a single shape per response.
/// </remarks>
public sealed record DocumentSymbolPayload
{
	/// <summary>The display name of the symbol.</summary>
	[JsonPropertyName("name")]
	public string? Name { get; init; }

	/// <summary>The optional short detail line shown beside the name.</summary>
	[JsonPropertyName("detail")]
	public string? Detail { get; init; }

	/// <summary>The protocol symbol kind.</summary>
	[JsonPropertyName("kind")]
	public SymbolKind Kind { get; init; }

	/// <summary>The optional symbol tags such as deprecated.</summary>
	[JsonPropertyName("tags")]
	public IReadOnlyList<SymbolTag>? Tags { get; init; }

	/// <summary>Whether the symbol is deprecated, when the server sent the flag.</summary>
	[JsonPropertyName("deprecated")]
	public bool? Deprecated { get; init; }

	/// <summary>The full range of the symbol; present for hierarchical entries.</summary>
	[JsonPropertyName("range")]
	public RangePayload? Range { get; init; }

	/// <summary>The range of the symbol name; present for hierarchical entries.</summary>
	[JsonPropertyName("selectionRange")]
	public RangePayload? SelectionRange { get; init; }

	/// <summary>The location of a flat symbol entry; present for flat entries.</summary>
	[JsonPropertyName("location")]
	public SymbolLocationPayload? Location { get; init; }

	/// <summary>The optional container name of a flat symbol entry.</summary>
	[JsonPropertyName("containerName")]
	public string? ContainerName { get; init; }

	/// <summary>The nested symbols of a hierarchical entry, in response order; flat entries carry none.</summary>
	[JsonPropertyName("children")]
	public IReadOnlyList<DocumentSymbolPayload>? Children { get; init; }
}
