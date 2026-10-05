using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents a single typed completion item returned by a language server.</summary>
public sealed record CompletionItemPayload
{
	/// <summary>The display label shown for the completion item.</summary>
	[JsonPropertyName("label")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Label { get; init; }

	/// <summary>The typed protocol completion-item kind, or <see langword="null"/> when the server sent none.</summary>
	/// <remarks>
	/// An unknown protocol value stays representable as an unnamed <see cref="CompletionItemKind"/> value.
	/// </remarks>
	[JsonPropertyName("kind")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public CompletionItemKind? Kind { get; init; }

	/// <summary>The optional detail text shown beside the label.</summary>
	[JsonPropertyName("detail")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Detail { get; init; }

	/// <summary>The optional documentation of the completion item.</summary>
	/// <remarks>
	/// A bare string reads as plain text, and the payload writes back as <c>MarkupContent</c>; see <see cref="MarkupContentPayload"/>.
	/// </remarks>
	[JsonPropertyName("documentation")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public MarkupContentPayload? Documentation { get; init; }

	/// <summary>The insert text the server sent, or <see langword="null"/> when it sent none.</summary>
	[JsonPropertyName("insertText")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? InsertText { get; init; }

	/// <summary>The typed protocol insert-text format, or <see langword="null"/> when the server sent none.</summary>
	/// <remarks>
	/// An unknown protocol value stays representable as an unnamed <see cref="InsertTextFormat"/> value.
	/// </remarks>
	[JsonPropertyName("insertTextFormat")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public InsertTextFormat? InsertTextFormat { get; init; }

	/// <summary>The optional filter text used for completion matching.</summary>
	[JsonPropertyName("filterText")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? FilterText { get; init; }

	/// <summary>The protocol sort text that orders the item relative to other items.</summary>
	[JsonPropertyName("sortText")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? SortText { get; init; }

	[JsonPropertyName("preselect")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? Preselect { get; init; }

	/// <summary>The additional annotations the server attached to the item, or <see langword="null"/> when the server sent none.</summary>
	/// <remarks>
	/// Unknown protocol values stay representable as unnamed <see cref="CompletionItemTag"/> values; a host that does not recognize a tag ignores it.
	/// </remarks>
	[JsonPropertyName("tags")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<CompletionItemTag>? Tags { get; init; }

	/// <summary>The characters that, when typed, accept the item, or <see langword="null"/> when the server sent none.</summary>
	/// <remarks>
	/// Entries are compared verbatim by a commit path; protocol-conforming servers send single-character entries and non-conforming entries
	/// are preserved as supplied.
	/// </remarks>
	[JsonPropertyName("commitCharacters")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string>? CommitCharacters { get; init; }

	/// <summary>The text edit applied when the completion item is committed.</summary>
	[JsonPropertyName("textEdit")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public CompletionTextEditPayload? TextEdit { get; init; }

	/// <summary>The secondary edits applied together with the item's commit, or <see langword="null"/> when the server sent none.</summary>
	/// <remarks>
	/// Mirrors the protocol's <c>additionalTextEdits</c> array - for example an import insertion belonging to an auto-import completion.
	/// Every entry carries its own replacement text; a commit path applies the entries together with the primary edit.
	/// </remarks>
	[JsonPropertyName("additionalTextEdits")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<TextEditPayload>? AdditionalTextEdits { get; init; }

	/// <summary>Protocol fields this type does not model explicitly.</summary>
	/// <remarks>
	/// They round-trip so completion-item resolve can preserve opaque server state such as <c>data</c>.
	/// </remarks>
	[JsonExtensionData]
	public IDictionary<string, JsonElement>? ExtensionData { get; init; }

	/// <remarks>
	/// The item is deprecated when the <c>tags</c> array carries <see cref="CompletionItemTag.Deprecated"/>, or when an unmodeled <c>deprecated</c>
	/// member is <see langword="true"/>.
	/// </remarks>
	[JsonIgnore]
	public bool IsDeprecated => Tags?.Contains(CompletionItemTag.Deprecated) == true || UnmodeledDeprecated == true;

	private bool? UnmodeledDeprecated =>
		ExtensionData is { } data
		&& data.TryGetValue("deprecated", out JsonElement value)
		&& value.ValueKind is JsonValueKind.True or JsonValueKind.False
			? value.GetBoolean()
			: null;
}
