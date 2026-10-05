using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Requests deletion of a directory and the tracked documents it contains.</summary>
/// <remarks>
/// The directory operation itself can remove untracked files as part of the recursive delete. Every tracked descendant of the directory
/// is part of the operation: each descendant's file stamp is validated against the stamp the store currently tracks, and a descendant whose file
/// changed since the store last observed it stops the operation with <see cref="WorkspaceDocumentDirectoryDeleteOutcome.ExternalFileConflict"/>.
/// Deletion follows the policy on <see cref="IWorkspaceFileSystem.DeleteAsync"/>.
/// </remarks>
public sealed record WorkspaceDocumentDirectoryDeleteRequest(string DirectoryPath);

/// <summary>Describes the outcome of deleting a directory and its tracked documents.</summary>
public enum WorkspaceDocumentDirectoryDeleteOutcome
{
	/// <summary>The directory was recursively deleted and the tracked descendants were removed.</summary>
	Deleted,

	InvalidPath,

	OperationInProgress,

	/// <summary>A tracked descendant's file stamp changed before the directory deletion.</summary>
	ExternalFileConflict,

	/// <summary>The delete did not complete because of an unexpected error.</summary>
	DeleteFailed,

	Canceled,
}

/// <summary>Contains the outcome of deleting a directory and its tracked documents.</summary>
/// <param name="Outcome">The directory delete outcome.</param>
/// <param name="DirectoryPath">The directory path supplied with the request.</param>
/// <param name="Snapshots">
/// The current snapshots of the affected tracked descendants; for a successful delete, built from the state immediately before removal from tracking;
/// empty when the path is invalid.
/// </param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceDocumentDirectoryDeleteResult(
	WorkspaceDocumentDirectoryDeleteOutcome Outcome,
	string DirectoryPath,
	IReadOnlyList<WorkspaceDocumentSnapshot> Snapshots,
	WorkspaceOperationFailure? Failure = null
);
