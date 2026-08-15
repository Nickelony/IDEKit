using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Requests replacement of the logical content and file format of a document.
/// </summary>
/// <remarks>The request does not write to disk; persistence is performed by <see cref="IWorkspaceDocumentStore.CommitAsync(WorkspaceDocumentCommitRequest, CancellationToken)"/>. The supplied format is stored as-is: <see cref="TextFileFormat.NewlineStyle"/> is never recomputed to match the new content.</remarks>
/// <param name="Identity">The document instance, id, and version expected by the caller.</param>
/// <param name="Content">The new logical content.</param>
/// <param name="FileFormat">The format associated with the new content.</param>
public sealed record WorkspaceDocumentReplaceRequest(
	WorkspaceDocumentRequestIdentity Identity,
	string Content,
	TextFileFormat FileFormat);

/// <summary>
/// Requests that a document discard its unsaved logical changes.
/// </summary>
/// <remarks>The document is restored to its last persisted content and format if it is dirty.</remarks>
/// <param name="Identity">The document instance, id, and version expected by the caller.</param>
public sealed record WorkspaceDocumentDiscardRequest(
	WorkspaceDocumentRequestIdentity Identity);

/// <summary>
/// Requests that a document move to a new path.
/// </summary>
/// <remarks>
/// The expected source stamp protects the move from an external source-file change. A request whose
/// expected stamp is missing retargets the document identity without a file-system move - there are
/// no bytes to move - and the destination keeps the missing stamp, so a later commit creates the
/// file at the new path. The source is re-checked before the retarget: a file - or a directory -
/// that appeared since the caller's snapshot reports
/// <see cref="WorkspaceDocumentRenameOutcome.ExternalFileConflict"/>, and an existing destination reports
/// <see cref="WorkspaceDocumentRenameOutcome.DestinationExists"/>.
/// </remarks>
/// <param name="Identity">The document instance, id, and version expected by the caller.</param>
/// <param name="ExpectedOnDiskStamp">The source stamp that must still match before the move.</param>
/// <param name="DestinationPath">The path that receives the moved file.</param>
public sealed record WorkspaceDocumentRenameRequest(
	WorkspaceDocumentRequestIdentity Identity,
	FileStamp ExpectedOnDiskStamp,
	string DestinationPath);

/// <summary>
/// Describes the outcome of renaming a document.
/// </summary>
public enum WorkspaceDocumentRenameOutcome
{
	/// <summary>The document was renamed; a rename whose source file was missing retargets the identity to the destination without a file-system move.</summary>
	Renamed,

	/// <summary>The destination path is identical to the document's path after normalization; a case-only difference is a real rename.</summary>
	NoChange,

	/// <summary>The path is invalid.</summary>
	InvalidPath,

	/// <summary>The document version was stale.</summary>
	StaleDocument,

	/// <summary>The tracked document instance does not match the request's document key.</summary>
	StaleDocumentInstance,

	/// <summary>The document was not found.</summary>
	DocumentNotFound,

	/// <summary>Another operation is in progress.</summary>
	OperationInProgress,

	/// <summary>The source file stamp did not match the expected stamp.</summary>
	ExternalFileConflict,

	/// <summary>The destination already exists.</summary>
	DestinationExists,

	/// <summary>A tracked document already occupies the destination path.</summary>
	DestinationInUse,

	/// <summary>The destination path is reserved by another operation: a file move or save-as destination reservation, or a directory whose rename or recursive delete is in flight.</summary>
	DestinationBusy,

	/// <summary>The move did not complete because of an unexpected error.</summary>
	MoveFailed,

	/// <summary>The move failed and its final state could not be established; for example, a case-only rename whose rollback move also failed.</summary>
	MoveStateUnknown,

	/// <summary>The operation was canceled.</summary>
	Canceled
}

/// <summary>
/// Contains the outcome of renaming a document.
/// </summary>
/// <remarks>
/// <see cref="Snapshot"/> contains the current document state when it is still tracked.
/// </remarks>
/// <param name="Outcome">The rename outcome.</param>
/// <param name="RequestedIdentity">The document instance, id, and version supplied with the request: the identity before the rename. The renamed identity is in <see cref="Snapshot"/>.</param>
/// <param name="Snapshot">The current document state; <see langword="null"/> when the document was not found or the request path was invalid.</param>
/// <param name="ObservedOnDiskStamp">The stamp that closes the rename: the resulting source stamp after a successful move (which may echo the request's expected stamp when the file system does not report one), or the stamp observed when a conflict or failure was detected; <see langword="null"/> when none was observed.</param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceDocumentRenameResult(
	WorkspaceDocumentRenameOutcome Outcome,
	WorkspaceDocumentRequestIdentity RequestedIdentity,
	WorkspaceDocumentSnapshot? Snapshot,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Requests that a document be persisted at a destination path.
/// </summary>
/// <remarks>The source file is retained; the tracked document is retargeted to the destination on success.</remarks>
/// <param name="Identity">The document instance, id, and version expected by the caller.</param>
/// <param name="ExpectedOnDiskStamp">The source stamp that must still match before the write.</param>
/// <param name="DestinationPath">The path that receives the written copy.</param>
public sealed record WorkspaceDocumentSaveAsRequest(
	WorkspaceDocumentRequestIdentity Identity,
	FileStamp ExpectedOnDiskStamp,
	string DestinationPath);

/// <summary>
/// Describes the outcome of saving a document at a destination path.
/// </summary>
public enum WorkspaceDocumentSaveAsOutcome
{
	/// <summary>The document was saved at the destination path; a destination that resolves to the document's own file is saved in place.</summary>
	SavedAs,

	/// <summary>The path is invalid.</summary>
	InvalidPath,

	/// <summary>The document version was stale.</summary>
	StaleDocument,

	/// <summary>The tracked document instance does not match the request's document key.</summary>
	StaleDocumentInstance,

	/// <summary>The document was not found.</summary>
	DocumentNotFound,

	/// <summary>Another operation is in progress.</summary>
	OperationInProgress,

	/// <summary>The destination already exists.</summary>
	DestinationExists,

	/// <summary>
	/// A tracked document already occupies the destination path. The document issuing the save is
	/// excluded: saving onto its own file is a save in place and reports
	/// <see cref="WorkspaceDocumentSaveAsOutcome.SavedAs"/>.
	/// </summary>
	DestinationInUse,

	/// <summary>The destination path is reserved by another operation: a file move or save-as destination reservation, or a directory whose rename or recursive delete is in flight.</summary>
	DestinationBusy,

	/// <summary>The source file stamp did not match the expected stamp.</summary>
	ExternalFileConflict,

	/// <summary>The write did not complete because of an unexpected error.</summary>
	WriteFailed,

	/// <summary>The replacement state is unknown.</summary>
	ReplacementStateUnknown,

	/// <summary>The operation was canceled.</summary>
	Canceled
}

/// <summary>
/// Contains the outcome of saving a document at a destination path.
/// </summary>
/// <remarks>
/// A successful result points to the destination path and preserves the document key. The source
/// file is not removed.
/// </remarks>
/// <param name="Outcome">The save-as outcome.</param>
/// <param name="RequestedIdentity">The document instance, id, and version supplied with the request: the identity before the save-as. The retargeted identity is in <see cref="Snapshot"/>.</param>
/// <param name="Snapshot">
/// The current document state; <see langword="null"/> when the document was not found or the request
/// path was invalid.
/// </param>
/// <param name="ObservedOnDiskStamp">The stamp that closes the write: the resulting destination stamp after a successful save-as, or the stamp observed when a conflict or destination collision was detected; <see langword="null"/> when none was observed.</param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceDocumentSaveAsResult(
	WorkspaceDocumentSaveAsOutcome Outcome,
	WorkspaceDocumentRequestIdentity RequestedIdentity,
	WorkspaceDocumentSnapshot? Snapshot,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Requests deletion of a workspace document.
/// </summary>
/// <remarks>Deletion uses the file system supplied to the store and is permanent by default; see <see cref="IWorkspaceFileSystem"/> remarks for host-specific deletion behavior.</remarks>
/// <param name="Identity">The document instance, id, and version expected by the caller.</param>
/// <param name="ExpectedOnDiskStamp">The on-disk stamp that must still match before deletion.</param>
public sealed record WorkspaceDocumentDeleteRequest(
	WorkspaceDocumentRequestIdentity Identity,
	FileStamp ExpectedOnDiskStamp);

/// <summary>
/// Describes the outcome of deleting a document.
/// </summary>
public enum WorkspaceDocumentDeleteOutcome
{
	/// <summary>The document was deleted.</summary>
	Deleted,

	/// <summary>The document version was stale.</summary>
	StaleDocument,

	/// <summary>The tracked document instance does not match the request's document key.</summary>
	StaleDocumentInstance,

	/// <summary>The document was not found.</summary>
	DocumentNotFound,

	/// <summary>Another operation is in progress.</summary>
	OperationInProgress,

	/// <summary>The on-disk stamp did not match the expected stamp.</summary>
	ExternalFileConflict,

	/// <summary>The delete did not complete because of an unexpected error.</summary>
	DeleteFailed,

	/// <summary>The operation was canceled.</summary>
	Canceled
}

/// <summary>
/// Contains the outcome of deleting a document.
/// </summary>
/// <remarks>
/// <see cref="Snapshot"/> describes the document immediately before it was removed from tracking in
/// a successful result; a failed result carries the current tracked state instead. Only
/// <see cref="WorkspaceDocumentDeleteOutcome.DocumentNotFound"/> returns no snapshot.
/// </remarks>
/// <param name="Outcome">The delete outcome.</param>
/// <param name="RequestedIdentity">The document instance, id, and version supplied with the request.</param>
/// <param name="Snapshot">The current tracked state; for a successful delete, the state immediately before removal. <see langword="null"/> when the document was not found.</param>
/// <param name="ObservedOnDiskStamp">The stamp that closes the delete: the stamp reported by the file system after a successful delete (the default file system reports none), or the stamp observed when a conflict or failure was detected; <see langword="null"/> when none was observed.</param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceDocumentDeleteResult(
	WorkspaceDocumentDeleteOutcome Outcome,
	WorkspaceDocumentRequestIdentity RequestedIdentity,
	WorkspaceDocumentSnapshot? Snapshot,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Describes the outcome of a synchronous logical document mutation.
/// </summary>
/// <remarks>These outcomes describe logical mutations; none of them writes to disk.</remarks>
public enum WorkspaceDocumentMutationOutcome
{
	/// <summary>The tracked logical state changed.</summary>
	Changed,

	/// <summary>The request made no logical change.</summary>
	NoChange,

	/// <summary>The document version was stale.</summary>
	StaleDocument,

	/// <summary>The tracked document instance does not match the request's document key.</summary>
	StaleDocumentInstance,

	/// <summary>The document was not found.</summary>
	DocumentNotFound,

	/// <summary>Another operation is in progress.</summary>
	OperationInProgress
}

/// <summary>
/// Contains the outcome of a synchronous logical document mutation.
/// </summary>
/// <remarks>
/// <see cref="Snapshot"/> contains the current tracked state for every outcome except
/// <see cref="WorkspaceDocumentMutationOutcome.DocumentNotFound"/>.
/// </remarks>
/// <param name="Outcome">The mutation outcome.</param>
/// <param name="RequestedIdentity">The document instance, id, and version supplied with the request.</param>
/// <param name="Snapshot">The current tracked state; <see langword="null"/> when the document was not found.</param>
public sealed record WorkspaceDocumentMutationResult(
	WorkspaceDocumentMutationOutcome Outcome,
	WorkspaceDocumentRequestIdentity RequestedIdentity,
	WorkspaceDocumentSnapshot? Snapshot);
