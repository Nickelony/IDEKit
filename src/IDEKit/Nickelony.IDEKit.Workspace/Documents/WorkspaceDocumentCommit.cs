using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Requests persistence of the current logical document content.</summary>
/// <param name="Identity">The document instance, id, and version expected by the caller.</param>
/// <param name="ExpectedOnDiskStamp">The on-disk stamp that must still match before writing.</param>
public sealed record WorkspaceDocumentCommitRequest(WorkspaceDocumentRequestIdentity Identity, FileStamp ExpectedOnDiskStamp);

/// <summary>Describes the outcome of persisting a workspace document.</summary>
public enum WorkspaceDocumentCommitOutcome
{
	/// <summary>
	/// The captured logical content and format were installed as the persisted baseline; a clean existing document commits as a no-op.
	/// </summary>
	Committed,

	StaleDocument,

	/// <summary>The tracked document instance does not match the request's document key.</summary>
	StaleDocumentInstance,

	DocumentNotFound,

	OperationInProgress,

	/// <summary>
	/// The stamp expected by the commit disagrees with the store's state: it is validated against a fresh capture when the commit writes,
	/// or against the tracked stamp for a clean no-op.
	/// </summary>
	ExternalFileConflict,

	/// <summary>The write did not complete because of an unexpected error.</summary>
	WriteFailed,

	ReplacementStateUnknown,

	Canceled,
}

/// <summary>Contains the outcome of persisting a workspace document.</summary>
/// <param name="Outcome">The commit outcome.</param>
/// <param name="RequestedIdentity">The document instance, id, and version supplied with the request.</param>
/// <param name="Snapshot">The current tracked state; <see langword="null"/> when the document was not found.</param>
/// <param name="ObservedOnDiskStamp">
/// The stamp that closes the operation. After a successful write it is the resulting on-disk stamp,
/// reported by the replacement or re-captured when the file system does not report one; a completed replacement whose stamp
/// cannot be established reports <see cref="WorkspaceDocumentCommitOutcome.ReplacementStateUnknown"/>. Otherwise, it is the stamp observed
/// when a conflict or failure was detected, and <see langword="null"/> when none was observed.
/// </param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceDocumentCommitResult(
	WorkspaceDocumentCommitOutcome Outcome,
	WorkspaceDocumentRequestIdentity RequestedIdentity,
	WorkspaceDocumentSnapshot? Snapshot,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null
);
