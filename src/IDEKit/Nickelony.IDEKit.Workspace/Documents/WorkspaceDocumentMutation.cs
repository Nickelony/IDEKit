using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Requests replacement of the logical content and file format of a document.</summary>
/// <param name="Identity">The document instance, id, and version expected by the caller.</param>
/// <param name="Content">The new logical content.</param>
/// <param name="FileFormat">The format associated with the new content.</param>
/// <remarks>
/// The request does not write to disk;
/// persistence is performed by <see cref="IWorkspaceDocumentStore.CommitAsync(WorkspaceDocumentCommitRequest, CancellationToken)"/>.
/// The supplied format is stored as-is: <see cref="TextFileFormat.LineTerminatorStyle"/> is never recomputed to match the new content.
/// </remarks>
public sealed record WorkspaceDocumentReplaceRequest(WorkspaceDocumentRequestIdentity Identity, string Content, TextFileFormat FileFormat);

/// <summary>Requests that a document discard its unsaved logical changes.</summary>
/// <param name="Identity">The document instance, id, and version expected by the caller.</param>
/// <remarks>
/// The document is restored to its last persisted content and format if it is dirty.
/// </remarks>
public sealed record WorkspaceDocumentDiscardRequest(WorkspaceDocumentRequestIdentity Identity);

/// <summary>Describes the outcome of a synchronous logical document mutation.</summary>
/// <remarks>
/// These outcomes describe logical mutations; none of them writes to disk.
/// </remarks>
public enum WorkspaceDocumentMutationOutcome
{
	Changed,

	NoChange,

	StaleDocument,

	/// <summary>The tracked document instance does not match the request's document key.</summary>
	StaleDocumentInstance,

	DocumentNotFound,

	OperationInProgress,
}

/// <summary>Contains the outcome of a synchronous logical document mutation.</summary>
/// <param name="Outcome">The mutation outcome.</param>
/// <param name="RequestedIdentity">The document instance, id, and version supplied with the request.</param>
/// <param name="Snapshot">The current tracked state; <see langword="null"/> when the document was not found.</param>
public sealed record WorkspaceDocumentMutationResult(
	WorkspaceDocumentMutationOutcome Outcome,
	WorkspaceDocumentRequestIdentity RequestedIdentity,
	WorkspaceDocumentSnapshot? Snapshot
)
{
	/// <summary>
	/// Indicates whether the document holds the requested state, because the mutation changed
	/// it (<see cref="WorkspaceDocumentMutationOutcome.Changed"/>) or it already held it (<see cref="WorkspaceDocumentMutationOutcome.NoChange"/>).
	/// </summary>
	public bool Applied => Outcome is WorkspaceDocumentMutationOutcome.Changed or WorkspaceDocumentMutationOutcome.NoChange;
}
