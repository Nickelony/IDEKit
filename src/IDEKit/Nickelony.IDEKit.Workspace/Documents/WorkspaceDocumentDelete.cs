using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Requests deletion of a workspace document.</summary>
/// <param name="Identity">The document instance, id, and version expected by the caller.</param>
/// <param name="ExpectedOnDiskStamp">The on-disk stamp that must still match before deletion.</param>
/// <remarks>
/// Deletion uses the file system supplied to the store, so it follows the policy on <see cref="IWorkspaceFileSystem.DeleteAsync"/>.
/// </remarks>
public sealed record WorkspaceDocumentDeleteRequest(WorkspaceDocumentRequestIdentity Identity, FileStamp ExpectedOnDiskStamp);

/// <summary>Describes the outcome of deleting a document.</summary>
public enum WorkspaceDocumentDeleteOutcome
{
	Deleted,

	StaleDocument,

	/// <summary>The tracked document instance does not match the request's document key.</summary>
	StaleDocumentInstance,

	DocumentNotFound,

	OperationInProgress,

	/// <summary>The on-disk stamp did not match the expected stamp.</summary>
	ExternalFileConflict,

	/// <summary>The delete did not complete because of an unexpected error.</summary>
	DeleteFailed,

	Canceled,
}

/// <summary>Contains the outcome of deleting a document.</summary>
/// <param name="Outcome">The delete outcome.</param>
/// <param name="RequestedIdentity">The document instance, id, and version supplied with the request.</param>
/// <param name="Snapshot">
/// The current tracked state; for a successful delete, the state immediately before removal. <see langword="null"/> when the document was not found.
/// </param>
/// <param name="ObservedOnDiskStamp">
/// The stamp that closes the delete: the stamp reported by the file system after a successful delete (the default file
/// system reports none), or the stamp observed when a conflict or failure was detected; <see langword="null"/> when none was observed.
/// </param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceDocumentDeleteResult(
	WorkspaceDocumentDeleteOutcome Outcome,
	WorkspaceDocumentRequestIdentity RequestedIdentity,
	WorkspaceDocumentSnapshot? Snapshot,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null
);
