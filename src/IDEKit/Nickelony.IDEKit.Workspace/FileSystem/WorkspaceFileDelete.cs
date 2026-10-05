namespace Nickelony.IDEKit.Workspace.FileSystem;

/// <summary>Describes the outcome of deleting a file or directory.</summary>
public enum WorkspaceFileDeleteOutcome
{
	/// <summary>No outcome was recorded; a default-constructed result does not describe a completed operation.</summary>
	Unknown = 0,

	/// <summary>The file or directory is absent after the delete operation.</summary>
	Deleted,

	/// <summary>The expected stamp did not match for a delete operation that validates a stamp.</summary>
	ExternalFileConflict,

	/// <summary>The delete did not complete because of an unexpected error.</summary>
	DeleteFailed,

	Canceled,
}

/// <summary>Contains the outcome of deleting a file or directory.</summary>
/// <param name="Outcome">The delete outcome.</param>
/// <param name="ObservedOnDiskStamp">The stamp observed when a conflict or failure was detected, when available.</param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceFileDeleteResult(
	WorkspaceFileDeleteOutcome Outcome,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null
);
