using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a single typed completion item returned by a language server.
/// </summary>
public sealed record CompletionItemPayload
{
	/// <summary>
	/// Gets the display label shown for the completion item.
	/// </summary>
	[JsonPropertyName("label")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Label { get; init; }

	/// <summary>
	/// Gets the typed protocol completion-item kind, or <see langword="null"/> when the server sent none.
	/// An unknown protocol value stays representable as an unnamed <see cref="CompletionItemKind"/> value.
	/// </summary>
	[JsonPropertyName("kind")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public CompletionItemKind? Kind { get; init; }

	/// <summary>
	/// Gets the optional detail text shown beside the label.
	/// </summary>
	[JsonPropertyName("detail")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Detail { get; init; }

	/// <summary>
	/// Gets the optional documentation payload for the completion item.
	/// </summary>
	[JsonPropertyName("documentation")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonElement? Documentation { get; init; }

	/// <summary>
	/// Gets the explicit insert text when it differs from the label.
	/// </summary>
	[JsonPropertyName("insertText")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? InsertText { get; init; }

	/// <summary>
	/// Gets the typed protocol insert-text format, or <see langword="null"/> when the server sent none.
	/// An unknown protocol value stays representable as an unnamed <see cref="InsertTextFormat"/> value.
	/// </summary>
	[JsonPropertyName("insertTextFormat")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public InsertTextFormat? InsertTextFormat { get; init; }

	/// <summary>
	/// Gets the optional filter text used for completion matching.
	/// </summary>
	[JsonPropertyName("filterText")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? FilterText { get; init; }

	/// <summary>
	/// Gets the protocol sort text used to order the item relative to other items.
	/// </summary>
	[JsonPropertyName("sortText")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? SortText { get; init; }

	/// <summary>
	/// Gets a value indicating whether the item should be preselected by the client.
	/// </summary>
	[JsonPropertyName("preselect")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? Preselect { get; init; }

	/// <summary>
	/// Gets the additional annotations the server attached to the item, or <see langword="null"/>
	/// when the server sent none.
	/// </summary>
	/// <remarks>
	/// Unknown protocol values stay representable as unnamed <see cref="CompletionItemTag"/> values;
	/// a host that does not recognize a tag ignores it.
	/// </remarks>
	[JsonPropertyName("tags")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<CompletionItemTag>? Tags { get; init; }

	/// <summary>
	/// Gets the characters that, when typed, accept the item, or <see langword="null"/> when the
	/// server sent none.
	/// </summary>
	/// <remarks>
	/// Entries are compared verbatim by a commit path; protocol-conforming servers send
	/// single-character entries and non-conforming entries are preserved as supplied.
	/// </remarks>
	[JsonPropertyName("commitCharacters")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string>? CommitCharacters { get; init; }

	/// <summary>
	/// Gets the text edit applied when the completion item is committed.
	/// </summary>
	[JsonPropertyName("textEdit")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public CompletionTextEditPayload? TextEdit { get; init; }

	/// <summary>
	/// Gets the secondary edits applied together with the item's commit, or <see langword="null"/>
	/// when the server sent none.
	/// </summary>
	/// <remarks>
	/// Mirrors the protocol's <c>additionalTextEdits</c> array - for example an import insertion
	/// belonging to an auto-import completion. Every entry carries its own replacement text; a
	/// commit path applies the entries together with the primary edit.
	/// </remarks>
	[JsonPropertyName("additionalTextEdits")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<TextEditPayload>? AdditionalTextEdits { get; init; }

	/// <summary>
	/// Gets protocol fields not modeled explicitly by this lean wrapper.
	/// These values round-trip so completion-item resolve can preserve opaque server state such as <c>data</c>.
	/// </summary>
	[JsonExtensionData]
	public IDictionary<string, JsonElement>? ExtensionData { get; init; }

	/// <summary>
	/// Gets a value indicating whether the item is deprecated.
	/// </summary>
	/// <remarks>
	/// The item is deprecated when the modern <c>tags</c> array carries
	/// <see cref="CompletionItemTag.Deprecated"/>, or - for servers that emit only the legacy boolean that
	/// predates <c>tags</c>, such as LuaLS - when an unmodeled <c>deprecated</c> member is <see langword="true"/>.
	/// </remarks>
	[JsonIgnore]
	public bool IsDeprecated => Tags?.Contains(CompletionItemTag.Deprecated) == true || LegacyDeprecated == true;

	private bool? LegacyDeprecated => ExtensionData is { } data
		&& data.TryGetValue("deprecated", out JsonElement value)
		&& value.ValueKind is JsonValueKind.True or JsonValueKind.False
			? value.GetBoolean()
			: null;
}
