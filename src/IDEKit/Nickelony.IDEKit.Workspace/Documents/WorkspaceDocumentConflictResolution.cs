using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Selects the source of truth when logical content conflicts with disk content.</summary>
public enum WorkspaceDocumentConflictResolutionMode
{
	/// <summary>Resolve with the content currently on disk.</summary>
	ResolveWithDisk,

	/// <summary>Resolve by keeping the logical document content and writing it to disk.</summary>
	ResolveWithLogical,
}

/// <summary>Requests resolution of an external workspace document conflict.</summary>
/// <param name="Identity">The document instance, id, and version expected by the caller.</param>
/// <param name="ObservedOnDiskStamp">The disk stamp that caused the conflict; it is revalidated before the resolution is applied.</param>
/// <param name="Mode">The source of truth to use when resolving the conflict.</param>
public sealed record WorkspaceDocumentConflictResolutionRequest(
	WorkspaceDocumentRequestIdentity Identity,
	FileStamp ObservedOnDiskStamp,
	WorkspaceDocumentConflictResolutionMode Mode
);

/// <summary>Describes the outcome of resolving an external workspace document conflict.</summary>
public enum WorkspaceDocumentConflictResolutionOutcome
{
	/// <summary>The conflict was resolved with disk content.</summary>
	ResolvedWithDisk,

	/// <summary>The conflict was resolved with logical content.</summary>
	ResolvedWithLogical,

	StaleDocument,

	/// <summary>The tracked document instance does not match the request's document key.</summary>
	StaleDocumentInstance,

	DocumentNotFound,

	OperationInProgress,

	/// <summary>The disk state changed again while the conflict was being resolved.</summary>
	ExternalFileConflict,

	/// <summary>The read did not complete because of an unexpected error.</summary>
	ReadFailed,

	/// <summary>The write did not complete because of an unexpected error.</summary>
	WriteFailed,

	ReplacementStateUnknown,

	Canceled,
}

/// <summary>Contains the outcome of resolving an external workspace document conflict.</summary>
/// <param name="Outcome">The resolution outcome.</param>
/// <param name="Mode">The source of truth carried by the request.</param>
/// <param name="RequestedIdentity">The document instance, id, and version supplied with the request.</param>
/// <param name="Snapshot">The current tracked state; <see langword="null"/> when the document was not found.</param>
/// <param name="ObservedOnDiskStamp">
/// The stamp that closes the resolution: the adopted disk
/// stamp after <see cref="WorkspaceDocumentConflictResolutionMode.ResolveWithDisk"/> (<see cref="FileStamp.Missing"/> for a missing file),
/// the resulting stamp after <see cref="WorkspaceDocumentConflictResolutionMode.ResolveWithLogical"/>, or the stamp observed when a conflict
/// or failure was detected.
/// </param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
/// <remarks>
/// <see cref="WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical"/> commits the logical snapshot;
/// <see cref="WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk"/> replaces it with freshly read disk content. A later logical edit
/// can leave the returned snapshot dirty even when the resolution succeeded.
/// </remarks>
public sealed record WorkspaceDocumentConflictResolutionResult(
	WorkspaceDocumentConflictResolutionOutcome Outcome,
	WorkspaceDocumentConflictResolutionMode Mode,
	WorkspaceDocumentRequestIdentity RequestedIdentity,
	WorkspaceDocumentSnapshot? Snapshot,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null
);
