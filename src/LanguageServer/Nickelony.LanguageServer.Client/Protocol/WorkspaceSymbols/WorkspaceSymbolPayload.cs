using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents one workspace-symbol entry returned by a language server.</summary>
/// <remarks>
/// <para>
/// The payload covers both result shapes of <c>workspace/symbol</c> with one type: the flat <c>SymbolInformation</c>
/// entry and the <c>WorkspaceSymbol</c> entry whose location may name a document without a range. Each member keeps what the server sent, so an entry
/// that goes back in a <c>workspaceSymbol/resolve</c> request is the one the server produced.
/// </para>
/// <para>
/// <see cref="Data"/> is an opaque detached clone that stays valid after its source document is disposed.
/// </para>
/// </remarks>
public sealed record WorkspaceSymbolPayload
{
	/// <summary>The display name of the symbol.</summary>
	[JsonPropertyName("name")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Name { get; init; }

	/// <summary>The protocol symbol kind.</summary>
	[JsonPropertyName("kind")]
	public SymbolKind Kind { get; init; }

	/// <summary>The optional symbol tags such as deprecated.</summary>
	[JsonPropertyName("tags")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[JsonConverter(typeof(TolerantCollectionJsonConverter<SymbolTag>))]
	public IReadOnlyList<SymbolTag>? Tags { get; init; }

	/// <summary>The name of the symbol that contains this one, or <see langword="null"/> when the server named none.</summary>
	[JsonPropertyName("containerName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ContainerName { get; init; }

	/// <summary>The location of the symbol, or <see langword="null"/> when the server sent none.</summary>
	[JsonPropertyName("location")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public WorkspaceSymbolLocationPayload? Location { get; init; }

	/// <summary>
	/// The opaque server state that <c>workspaceSymbol/resolve</c> sends back, or <see langword="null"/> when the server sent none.
	/// </summary>
	[JsonPropertyName("data")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[JsonConverter(typeof(JsonElementJsonConverter))]
	public JsonElement? Data { get; init; }
}
