namespace Nickelony.IDEKit.Workspace.FileSystem;

/// <summary>Describes the outcome of conditionally replacing a destination file.</summary>
public enum WorkspaceFileReplacementOutcome
{
	/// <summary>No outcome was recorded; a default-constructed result does not describe a completed operation.</summary>
	Unknown = 0,

	/// <summary>The destination was replaced after its stamp matched the expected stamp.</summary>
	Replaced,

	/// <summary>The destination stamp did not match the expected stamp.</summary>
	ExternalFileConflict,

	/// <summary>
	/// A directory occupies the destination path where the replacement expected no file, so the temporary file cannot be moved onto it.
	/// </summary>
	DestinationExists,

	/// <summary>The replacement may have occurred, but its final state could not be determined.</summary>
	ReplacementStateUnknown,

	/// <summary>The replacement did not complete because of an unexpected error.</summary>
	Failed,

	/// <summary>The replacement was canceled before the destination was touched.</summary>
	Canceled,
}

/// <summary>Contains the outcome of replacing a destination file.</summary>
/// <param name="Outcome">The replacement outcome.</param>
/// <param name="ObservedOnDiskStamp">The stamp observed during conflict detection or after replacement, when available.</param>
/// <param name="Failure">Explains a failed or indeterminate operation, when one occurred.</param>
public sealed record WorkspaceFileReplacementResult(
	WorkspaceFileReplacementOutcome Outcome,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null
);
