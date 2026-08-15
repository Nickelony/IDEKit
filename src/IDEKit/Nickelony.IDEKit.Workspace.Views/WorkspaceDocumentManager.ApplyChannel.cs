using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

public sealed partial class WorkspaceDocumentManager
{
	// The subscription entry point: a raise starts the apply pipeline without blocking the raising
	// view; the outcome reaches the publishing view through AcknowledgeApply.
	private void OnApplyRequested(
		object? sender,
		WorkspaceDocumentViewApplyRequestedEventArgs eventArgs)
	{
		if (sender is not IWorkspaceDocumentView sourceView)
			return;

		_ = HandleApplyRequestedAsync(sourceView, eventArgs);
	}

	// An event-driven apply has no synchronous completion channel back to the raising code: the
	// publishing view learns the outcome through AcknowledgeApply (a replacement the store rejects is
	// acknowledged with the current state), while a failure thrown by the apply path itself is
	// recorded as unsynchronized view state with its detail and blocks the operations that destroy
	// the document until a successful refresh clears it. Callers that need the mutation result call
	// ReplaceAsync directly. The replacement registers a manager operation, so a stop still waits for
	// the fire-and-forget call.
	private async Task HandleApplyRequestedAsync(
		IWorkspaceDocumentView sourceView,
		WorkspaceDocumentViewApplyRequestedEventArgs eventArgs)
	{
		try
		{
			if (!IsRegistered(sourceView))
				return;

			await ReplaceCoreAsync(eventArgs.Request, sourceView).ConfigureAwait(false);
		}
		catch (ObjectDisposedException) when (!IsRegistered(sourceView))
		{
			// A stop that began after the registration check rejected the apply. The stop owns the
			// view teardown, so the apply is not recorded as a view failure; a disposed store while
			// the manager is still active keeps the registration and falls through to the recording
			// path below.
		}
		catch (Exception exception)
		{
			RecordViewFailure(sourceView, new WorkspaceOperationFailure(
				WorkspaceViewOperationFailureCodes.ViewApplyFailed,
				exception.Message,
				exception));
		}
	}
}
