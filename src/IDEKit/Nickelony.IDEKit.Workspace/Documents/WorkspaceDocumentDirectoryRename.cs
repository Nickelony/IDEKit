namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Requests a directory move and the document identity updates it implies.</summary>
/// <param name="SourceDirectoryPath">The directory path that is moved.</param>
/// <param name="DestinationDirectoryPath">The path that receives the moved directory.</param>
/// <remarks>
/// Every tracked descendant of the source directory is part of the operation: the directory move applies to the complete directory on disk,
/// so each descendant's identity path is rebased onto the destination and each descendant's file stamp is validated against the stamp
/// the store currently tracks before the move starts. A descendant whose file changed since the store last observed it stops the operation
/// with <see cref="WorkspaceDocumentDirectoryRenameOutcome.ExternalFileConflict"/>.
/// </remarks>
public sealed record WorkspaceDocumentDirectoryRenameRequest(string SourceDirectoryPath, string DestinationDirectoryPath);

/// <summary>Describes the outcome of renaming a directory and its tracked documents.</summary>
public enum WorkspaceDocumentDirectoryRenameOutcome
{
	/// <summary>The directory rename landed and the tracked descendants were rebased onto the destination.</summary>
	Renamed,

	/// <summary>The destination path is identical to the source path after normalization; a case-only difference is a real rename.</summary>
	NoChange,

	InvalidPath,

	OperationInProgress,

	/// <summary>A tracked descendant's file stamp changed before the directory move.</summary>
	ExternalFileConflict,

	DestinationExists,

	/// <summary>A tracked document already uses a destination identity.</summary>
	DestinationInUse,

	/// <summary>
	/// Another operation reserves one of the destination identity paths, or an in-flight directory rename or recursive delete covers the destination.
	/// </summary>
	DestinationBusy,

	/// <summary>The move did not complete because of an unexpected error.</summary>
	MoveFailed,

	/// <summary>The move failed and its final state could not be established; the tracked descendants keep their previous identities.</summary>
	MoveStateUnknown,

	Canceled,
}

/// <summary>Contains the outcome of renaming a directory and its tracked documents.</summary>
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
	WorkspaceOperationFailure? Failure = null
);
