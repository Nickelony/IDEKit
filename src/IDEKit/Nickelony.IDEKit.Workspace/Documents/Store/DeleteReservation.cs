namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Represents the delete in flight for one tracked document, which an open for the same path can wait on.</summary>
/// <remarks>
/// While the reservation is active, a replacement and every gated operation on the document report that an operation is in progress, and an open
/// for the path waits for the delete instead of returning an instance that is about to be removed. The reservation is removed before its completion
/// task is released, so a resumed waiter re-evaluates the state after the delete.
/// </remarks>
internal sealed class DeleteReservation
{
	internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
