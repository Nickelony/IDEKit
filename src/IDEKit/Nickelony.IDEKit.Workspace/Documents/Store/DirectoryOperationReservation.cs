namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Represents an in-progress directory rename or recursive delete that callers can wait on while it moves or removes every path
/// under its source directory.
/// </summary>
/// <remarks>
/// While the reservation is active, an open for a path inside the source subtree waits for the directory operation instead of loading a file
/// whose location is about to change. A file rename or save-as into the subtree is rejected as busy. The reservation is removed before its completion
/// task is released, so a resumed waiter re-evaluates the post-operation state under the store state lock.
/// </remarks>
internal sealed class DirectoryOperationReservation(string directoryId)
{
	/// <summary>
	/// The normalized id of the operation's source directory with any trailing separator trimmed, so the id matches the directory path
	/// and its descendants.
	/// </summary>
	internal string DirectoryId { get; } = directoryId;
	internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
