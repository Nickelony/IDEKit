using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents the typed workspace edit returned by rename and carried by code actions.</summary>
public readonly record struct WorkspaceEditResponse
{
	/// <param name="changes">The simple URI-to-edit map returned by the server.</param>
	/// <param name="documentChanges">The structured document-change payload returned by the server.</param>
	/// <param name="changeAnnotations">The change annotations referenced by the returned edits.</param>
	[JsonConstructor]
	public WorkspaceEditResponse(
		IReadOnlyDictionary<string, IReadOnlyList<TextEditPayload>?>? changes,
		IReadOnlyList<WorkspaceDocumentChangePayload>? documentChanges,
		IReadOnlyDictionary<string, WorkspaceEditChangeAnnotationPayload>? changeAnnotations = null
	)
	{
		Changes = WorkspaceEditPayloadCloner.CloneChangeMap(changes);
		DocumentChanges = WorkspaceEditPayloadCloner.CloneDocumentChanges(documentChanges);
		ChangeAnnotations = WorkspaceEditPayloadCloner.CloneChangeAnnotations(changeAnnotations);
	}

	/// <summary>The simple URI-to-edit map returned by the server.</summary>
	/// <remarks>
	/// The returned dictionary and nested edit lists are defensive read-only snapshots.
	/// </remarks>
	[JsonPropertyName("changes")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, IReadOnlyList<TextEditPayload>?>? Changes { get; }

	/// <summary>The structured document-change payload returned by the server.</summary>
	/// <remarks>
	/// The returned list is a defensive read-only snapshot.
	/// </remarks>
	[JsonPropertyName("documentChanges")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<WorkspaceDocumentChangePayload>? DocumentChanges { get; }

	/// <summary>The change annotations referenced by the returned edits, or <see langword="null"/> when the server sent none.</summary>
	/// <remarks>
	/// The returned dictionary is a defensive read-only snapshot.
	/// </remarks>
	[JsonPropertyName("changeAnnotations")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, WorkspaceEditChangeAnnotationPayload>? ChangeAnnotations { get; }
}
