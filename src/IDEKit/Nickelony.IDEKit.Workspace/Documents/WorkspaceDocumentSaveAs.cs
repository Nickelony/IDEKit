using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Requests that a document be persisted at a destination path.</summary>
/// <param name="Identity">The document instance, id, and version expected by the caller.</param>
/// <param name="ExpectedOnDiskStamp">The source stamp that must still match before the write.</param>
/// <param name="DestinationPath">The path that receives the written copy.</param>
/// <remarks>
/// The source file is retained; the tracked document is retargeted to the destination on success.
/// </remarks>
public sealed record WorkspaceDocumentSaveAsRequest(WorkspaceDocumentRequestIdentity Identity, FileStamp ExpectedOnDiskStamp, string DestinationPath);

/// <summary>Describes the outcome of saving a document at a destination path.</summary>
public enum WorkspaceDocumentSaveAsOutcome
{
	/// <summary>The document was saved at the destination path; a destination that resolves to the document's own file is saved in place.</summary>
	SavedAs,

	InvalidPath,

	StaleDocument,

	/// <summary>The tracked document instance does not match the request's document key.</summary>
	StaleDocumentInstance,

	DocumentNotFound,

	OperationInProgress,

	DestinationExists,

	/// <summary>
	/// A tracked document already occupies the destination path. The document issuing the save is excluded: saving onto its own file is a save
	/// in place and reports <see cref="WorkspaceDocumentSaveAsOutcome.SavedAs"/>.
	/// </summary>
	DestinationInUse,

	/// <summary>
	/// The destination path is reserved by another operation: a file move or save-as destination reservation, or a directory whose rename
	/// or recursive delete is in flight.
	/// </summary>
	DestinationBusy,

	/// <summary>The source file stamp did not match the expected stamp.</summary>
	ExternalFileConflict,

	/// <summary>The write did not complete because of an unexpected error.</summary>
	WriteFailed,

	ReplacementStateUnknown,

	Canceled,
}

/// <summary>Contains the outcome of saving a document at a destination path.</summary>
/// <param name="Outcome">The save-as outcome.</param>
/// <param name="RequestedIdentity">
/// The document instance, id, and version supplied with the request: the identity before the save-as. The retargeted identity
/// is in <see cref="Snapshot"/>.
/// </param>
/// <param name="Snapshot">The current document state; <see langword="null"/> when the document was not found or the request path was invalid.</param>
/// <param name="ObservedOnDiskStamp">
/// The stamp that closes the write: the resulting destination stamp after a successful save-as, or the stamp observed when a conflict or destination
/// collision was detected; <see langword="null"/> when none was observed.
/// </param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceDocumentSaveAsResult(
	WorkspaceDocumentSaveAsOutcome Outcome,
	WorkspaceDocumentRequestIdentity RequestedIdentity,
	WorkspaceDocumentSnapshot? Snapshot,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null
);
