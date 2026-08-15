using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Requests a directory move and the document identity updates it implies.
/// </summary>
/// <remarks>
/// Every tracked descendant of the source directory is part of the operation: the directory move
/// applies to the complete directory on disk, so each descendant's identity path is rebased onto the
/// destination and each descendant's file stamp is validated against the stamp the store currently
/// tracks before the move starts. A descendant whose file changed since the store last observed it
/// stops the operation with <see cref="WorkspaceDocumentDirectoryRenameOutcome.ExternalFileConflict"/>.
/// </remarks>
/// <param name="SourceDirectoryPath">The directory path that is moved.</param>
/// <param name="DestinationDirectoryPath">The path that receives the moved directory.</param>
public sealed record WorkspaceDocumentDirectoryRenameRequest(
	string SourceDirectoryPath,
	string DestinationDirectoryPath);

/// <summary>
/// Describes the outcome of renaming a directory and its tracked documents.
/// </summary>
public enum WorkspaceDocumentDirectoryRenameOutcome
{
	/// <summary>The directory was renamed and the tracked descendants were rebased onto the destination.</summary>
	Renamed,

	/// <summary>The destination path is identical to the source path after normalization; a case-only difference is a real rename.</summary>
	NoChange,

	/// <summary>The path is invalid.</summary>
	InvalidPath,

	/// <summary>Another operation is in progress.</summary>
	OperationInProgress,

	/// <summary>A tracked descendant's file stamp changed before the directory move.</summary>
	ExternalFileConflict,

	/// <summary>The destination already exists.</summary>
	DestinationExists,

	/// <summary>A tracked document already uses a destination identity.</summary>
	DestinationInUse,

	/// <summary>Another operation is currently reserving one of the destination identity paths.</summary>
	DestinationBusy,

	/// <summary>The move did not complete because of an unexpected error.</summary>
	MoveFailed,

	/// <summary>The move failed and its final state could not be established; the tracked descendants keep their previous identities.</summary>
	MoveStateUnknown,

	/// <summary>The operation was canceled.</summary>
	Canceled
}

/// <summary>
/// Contains the outcome of renaming a directory and its tracked documents.
/// </summary>
/// <param name="Outcome">The directory rename outcome.</param>
/// <param name="SourceDirectoryPath">The source directory path supplied with the request.</param>
/// <param name="DestinationDirectoryPath">The destination directory path supplied with the request.</param>
/// <param name="Snapshots">The current snapshots of the affected tracked descendants; empty when the path is invalid.</param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceDocumentDirectoryRenameResult(
	WorkspaceDocumentDirectoryRenameOutcome Outcome,
	string SourceDirectoryPath,
	string DestinationDirectoryPath,
	IReadOnlyList<WorkspaceDocumentSnapshot> Snapshots,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Requests deletion of a directory and the tracked documents it contains.
/// </summary>
/// <remarks>
/// The directory operation itself can remove untracked files as part of the recursive delete. Every
/// tracked descendant of the directory is part of the operation: each descendant's file stamp is
/// validated against the stamp the store currently tracks, and a descendant whose file changed since
/// the store last observed it stops the operation with
/// <see cref="WorkspaceDocumentDirectoryDeleteOutcome.ExternalFileConflict"/>.
/// Deletion is permanent by default; see <see cref="IWorkspaceFileSystem"/> remarks for host-specific
/// deletion behavior such as a shell recycle bin.
/// </remarks>
/// <param name="DirectoryPath">The directory path to delete.</param>
public sealed record WorkspaceDocumentDirectoryDeleteRequest(
	string DirectoryPath);

/// <summary>
/// Describes the outcome of deleting a directory and its tracked documents.
/// </summary>
public enum WorkspaceDocumentDirectoryDeleteOutcome
{
	/// <summary>The directory was recursively deleted and the tracked descendants were removed.</summary>
	Deleted,

	/// <summary>The path is invalid.</summary>
	InvalidPath,

	/// <summary>Another operation is in progress.</summary>
	OperationInProgress,

	/// <summary>A tracked descendant's file stamp changed before the directory deletion.</summary>
	ExternalFileConflict,

	/// <summary>The delete did not complete because of an unexpected error.</summary>
	DeleteFailed,

	/// <summary>The operation was canceled.</summary>
	Canceled
}

/// <summary>
/// Contains the outcome of deleting a directory and its tracked documents.
/// </summary>
/// <param name="Outcome">The directory delete outcome.</param>
/// <param name="DirectoryPath">The directory path supplied with the request.</param>
/// <param name="Snapshots">The current snapshots of the affected tracked descendants; for a successful delete, built from the state just before removal from tracking; empty when the path is invalid.</param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceDocumentDirectoryDeleteResult(
	WorkspaceDocumentDirectoryDeleteOutcome Outcome,
	string DirectoryPath,
	IReadOnlyList<WorkspaceDocumentSnapshot> Snapshots,
	WorkspaceOperationFailure? Failure = null);
