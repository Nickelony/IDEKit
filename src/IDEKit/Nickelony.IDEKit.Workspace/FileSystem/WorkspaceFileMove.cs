namespace Nickelony.IDEKit.Workspace.FileSystem;

/// <summary>Describes the outcome of moving a file or directory after validating the source when applicable.</summary>
public enum WorkspaceFileMoveOutcome
{
	/// <summary>No outcome was recorded; a default-constructed result does not describe a completed operation.</summary>
	Unknown = 0,

	Moved,

	DestinationExists,

	/// <summary>The source stamp did not match the expected stamp.</summary>
	ExternalFileConflict,

	/// <summary>The move did not complete because of an unexpected error.</summary>
	MoveFailed,

	/// <summary>
	/// The move failed and its final state could not be established; for example, a case-only move whose rollback move also failed.
	/// </summary>
	MoveStateUnknown,

	Canceled,
}

/// <summary>Contains the outcome of moving a file or directory.</summary>
/// <param name="Outcome">The move outcome.</param>
/// <param name="ObservedOnDiskStamp">
/// The source stamp observed when a conflict was detected, or the resulting destination stamp after a successful move, when available.
/// </param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceFileMoveResult(
	WorkspaceFileMoveOutcome Outcome,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null
);
