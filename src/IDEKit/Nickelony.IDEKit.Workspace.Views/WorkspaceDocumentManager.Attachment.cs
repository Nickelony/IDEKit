using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

public sealed partial class WorkspaceDocumentManager
{
	// Determines whether the view may be attached, with the failure to report when the view could
	// not report its id or state. The view-id read and the state probe both run through the dispatch
	// delegate; duplicate detection uses the view ids captured at registration, so no view member is
	// read while the state lock is held. The state lock is never held across an await: taking it
	// inside dispatched code is safe only because no member awaits a dispatcher action while holding
	// it. State-based unavailability (an existing document, pending edits, or a conflict) carries no
	// failure: the host can inspect that state on the view itself.
	private async Task<(ViewAttachmentOutcome Decision, WorkspaceOperationFailure? Failure)> ValidateViewAsync(IWorkspaceDocumentView view)
	{
		ViewAttachmentOutcome validation = ViewAttachmentOutcome.Proceed;
		WorkspaceOperationFailure? failure = null;
		await _dispatchViewAction(() =>
		{
			string viewId;
			try
			{
				viewId = view.ViewId;
			}
			catch (Exception exception)
			{
				validation = ViewAttachmentOutcome.Unavailable;
				failure = new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.OpenFailed, exception.Message, exception);
				return;
			}

			if (string.IsNullOrWhiteSpace(viewId))
			{
				validation = ViewAttachmentOutcome.Unavailable;
				failure = new WorkspaceOperationFailure(
					WorkspaceViewOperationFailureCodes.OpenFailed,
					ViewIdUnreportedMessage);
				return;
			}

			lock (_stateLock)
			{
				if (_registrations.ContainsKey(view))
				{
					validation = ViewAttachmentOutcome.AlreadyRegistered;
					return;
				}

				if (_viewsByViewId.ContainsKey(viewId))
				{
					validation = ViewAttachmentOutcome.DuplicateViewId;
					return;
				}
			}

			try
			{
				if (view.DocumentKey is not null
					|| view.HasPendingEdits
					|| view.HasConflict)
				{
					validation = ViewAttachmentOutcome.Unavailable;
				}
			}
			catch (Exception exception)
			{
				validation = ViewAttachmentOutcome.Unavailable;
				failure = new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.OpenFailed, exception.Message, exception);
			}
		}).ConfigureAwait(false);

		return (validation, failure);
	}
}
