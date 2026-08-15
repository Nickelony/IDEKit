namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Represents an in-progress directory rename or recursive delete that callers can wait on while it
/// moves or removes every path under its source directory.
/// </summary>
/// <remarks>
/// While the reservation is active, an open for a path inside the source subtree waits for the
/// directory operation instead of loading a file whose location is about to change, and a file
/// rename or save-as into the subtree is rejected as busy. The reservation is removed before its
/// completion task is released, so a resumed waiter re-evaluates the post-operation state under the
/// store state lock.
/// </remarks>
internal sealed class DirectoryOperationReservation(string directoryId, string sourcePrefix)
{
	/// <summary>
	/// Gets the normalized id of the operation's source directory, with any trailing separator
	/// trimmed, so the directory path itself matches the subtree coverage as well as its descendants.
	/// </summary>
	public string DirectoryId { get; } = directoryId;

	/// <summary>
	/// Gets the source directory id with a guaranteed trailing separator, used for the prefix test
	/// against descendant document ids.
	/// </summary>
	public string SourcePrefix { get; } = sourcePrefix;

	public TaskCompletionSource Completion { get; } =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
}
