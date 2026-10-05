using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Requests that a document move to a new path.</summary>
/// <param name="Identity">The document instance, id, and version expected by the caller.</param>
/// <param name="ExpectedOnDiskStamp">The source stamp that must still match before the move.</param>
/// <param name="DestinationPath">The path that receives the moved file.</param>
/// <remarks>
/// The expected source stamp protects the move from an external source-file change. A request whose expected stamp is missing retargets the document
/// identity without a file-system move and keeps the missing stamp, so a later commit creates the file at the new path. The source is re-checked
/// before the retarget, reporting <see cref="WorkspaceDocumentRenameOutcome.ExternalFileConflict"/> for a file or directory that appeared
/// and <see cref="WorkspaceDocumentRenameOutcome.DestinationExists"/> for an existing destination.
/// </remarks>
public sealed record WorkspaceDocumentRenameRequest(WorkspaceDocumentRequestIdentity Identity, FileStamp ExpectedOnDiskStamp, string DestinationPath);

/// <summary>Describes the outcome of renaming a document.</summary>
public enum WorkspaceDocumentRenameOutcome
{
	/// <summary>
	/// The document rename landed; a rename whose source file was missing retargets the identity to the destination without a file-system move.
	/// </summary>
	Renamed,

	/// <summary>The destination path is identical to the document's path after normalization; a case-only difference is a real rename.</summary>
	NoChange,

	InvalidPath,

	StaleDocument,

	/// <summary>The tracked document instance does not match the request's document key.</summary>
	StaleDocumentInstance,

	DocumentNotFound,

	OperationInProgress,

	/// <summary>The source file stamp did not match the expected stamp.</summary>
	ExternalFileConflict,

	DestinationExists,

	/// <summary>A tracked document already occupies the destination path.</summary>
	DestinationInUse,

	/// <summary>
	/// The destination path is reserved by another operation: a file move or save-as destination reservation, or a directory whose rename
	/// or recursive delete is in flight.
	/// </summary>
	DestinationBusy,

	/// <summary>The move did not complete because of an unexpected error.</summary>
	MoveFailed,

	/// <summary>
	/// The move failed and its final state could not be established; for example, a case-only move whose rollback move also failed.
	/// </summary>
	MoveStateUnknown,

	Canceled,
}

/// <summary>Contains the outcome of renaming a document.</summary>
/// <param name="Outcome">The rename outcome.</param>
/// <param name="RequestedIdentity">
/// The document instance, id, and version supplied with the request: the identity before the rename. The renamed identity
/// is in <see cref="Snapshot"/>.
/// </param>
/// <param name="Snapshot">The current document state; <see langword="null"/> when the document was not found or the request path was invalid.</param>
/// <param name="ObservedOnDiskStamp">
/// The stamp that closes the rename. After a successful move it is the stamp of the moved file (captured at the destination
/// when the file system reports none, and the request's expected stamp when that capture fails; <see cref="FileStamp.Missing"/> when a missing
/// source was retargeted). When a conflict or failure was detected it is the stamp observed then. It is <see langword="null"/>
/// when none was observed.
/// </param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceDocumentRenameResult(
	WorkspaceDocumentRenameOutcome Outcome,
	WorkspaceDocumentRequestIdentity RequestedIdentity,
	WorkspaceDocumentSnapshot? Snapshot,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null
);
