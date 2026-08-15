using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a structured document-change entry within a workspace edit response.
/// </summary>
public sealed record WorkspaceDocumentChangePayload
{
	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceDocumentChangePayload"/> struct.
	/// </summary>
	/// <param name="textDocument">The target text document descriptor, including its optional version.</param>
	/// <param name="edits">The edits to apply to the target document.</param>
	/// <param name="kind">The resource-operation kind when the change is not a text-document edit.</param>
	/// <param name="uri">The target URI for create or delete operations.</param>
	/// <param name="oldUri">The source URI for rename operations.</param>
	/// <param name="newUri">The destination URI for rename operations.</param>
	/// <param name="options">The resource-operation options (overwrite and ignore flags) for create, rename, or delete operations.</param>
	[JsonConstructor]
	public WorkspaceDocumentChangePayload(
		OptionalVersionedTextDocumentIdentifier? textDocument,
		IReadOnlyList<TextEditPayload>? edits,
		string? kind,
		string? uri,
		string? oldUri,
		string? newUri,
		WorkspaceResourceOperationOptionsPayload? options = null)
	{
		TextDocument = textDocument;
		Edits = WorkspaceEditPayloadCloner.CloneEditList(edits);
		Kind = kind;
		Uri = uri;
		OldUri = oldUri;
		NewUri = newUri;
		Options = options;
	}

	/// <summary>
	/// Gets the target text document descriptor, including the optional version the server sent for the edit.
	/// The member is omitted when the change is a resource operation; the protocol does not allow a null value.
	/// </summary>
	[JsonPropertyName("textDocument")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public OptionalVersionedTextDocumentIdentifier? TextDocument { get; }

	/// <summary>
	/// Gets the edits to apply to the target document.
	/// The returned list is a defensive read-only snapshot. The member is omitted when the change is a resource
	/// operation; the protocol does not allow a null value.
	/// </summary>
	[JsonPropertyName("edits")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<TextEditPayload>? Edits { get; }

	/// <summary>
	/// Gets the resource-operation kind when the change is not a text-document edit.
	/// </summary>
	[JsonPropertyName("kind")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Kind { get; }

	/// <summary>
	/// Gets the target URI for create or delete operations.
	/// </summary>
	[JsonPropertyName("uri")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Uri { get; }

	/// <summary>
	/// Gets the source URI for rename operations.
	/// </summary>
	[JsonPropertyName("oldUri")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? OldUri { get; }

	/// <summary>
	/// Gets the destination URI for rename operations.
	/// </summary>
	[JsonPropertyName("newUri")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? NewUri { get; }

	/// <summary>
	/// Gets the resource-operation options the server attached to a create, rename, or delete change, or
	/// <see langword="null"/> when the server sent none (or the change carries text edits).
	/// </summary>
	[JsonPropertyName("options")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public WorkspaceResourceOperationOptionsPayload? Options { get; }

	/// <summary>
	/// Gets a value indicating whether the payload describes a resource operation (<c>create</c>, <c>rename</c>, or
	/// <c>delete</c>) rather than text edits.
	/// </summary>
	/// <remarks>
	/// An unrecognized <see cref="Kind"/> is reported as <see langword="false"/> so unknown operations cannot be
	/// mistaken for supported resource operations. The property is computed state, not a wire member, and is never
	/// serialized.
	/// </remarks>
	[JsonIgnore]
	public bool IsResourceOperation => Kind is "create" or "rename" or "delete";
}

/// <summary>
/// Represents the resource-operation options of a structured document change (create, rename, or delete).
/// </summary>
/// <param name="Overwrite">Whether an existing target should be overwritten, or <see langword="null"/> when the server omitted the flag.</param>
/// <param name="IgnoreIfExists">Whether an existing target should be left untouched instead of overwritten, or <see langword="null"/> when the server omitted the flag.</param>
/// <param name="IgnoreIfNotExists">Whether a missing target should be ignored for delete operations, or <see langword="null"/> when the server omitted the flag.</param>
/// <param name="Recursive">Whether a delete operation should recurse into directories, or <see langword="null"/> when the server omitted the flag.</param>
public readonly record struct WorkspaceResourceOperationOptionsPayload(
	[property: JsonPropertyName("overwrite")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	bool? Overwrite = null,
	[property: JsonPropertyName("ignoreIfExists")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	bool? IgnoreIfExists = null,
	[property: JsonPropertyName("ignoreIfNotExists")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	bool? IgnoreIfNotExists = null,
	[property: JsonPropertyName("recursive")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	bool? Recursive = null);
