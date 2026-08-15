namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Represents an in-progress document open that callers can wait on.
/// </summary>
internal sealed class OpenReservation
{
	public TaskCompletionSource<WorkspaceDocumentOpenResult> Completion { get; } =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
}
