using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Workspace.Documents;
using static Nickelony.IDEKit.Workspace.Views.Coordination.ViewSynchronizer;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>
/// Runs the publication of the views: the apply of an edit that a view raises, and the wait that a host asks for until the store applied
/// what the views published.
/// </summary>
/// <remarks>
/// <para>
/// A view that publishes its own edits raises <see cref="IWorkspaceDocumentView.ApplyRequested"/>, and the runner applies the edit to the store
/// without blocking the raising view.
/// </para>
/// <para>
/// The runner tracks the applies in flight, so a publication that a host asks for completes only after them. It reports a view that cannot publish,
/// that throws or that holds a conflict as <c>Blocked</c>.
/// </para>
/// </remarks>
internal sealed class PublicationRunner
{
	private readonly IWorkspaceDocumentStore _store;
	private readonly ViewTracker _views;
	private readonly ViewSynchronizer _synchronizer;
	private readonly Func<Action, Task> _dispatchViewAction;
	private readonly CoordinatorOperationRunner _operations;
	private readonly InFlightApplyTracker _applies = new();

	internal PublicationRunner(
		IWorkspaceDocumentStore store,
		ViewTracker views,
		ViewSynchronizer synchronizer,
		Func<Action, Task> dispatchViewAction,
		CoordinatorOperationRunner operations
	)
	{
		_store = store;
		_views = views;
		_synchronizer = synchronizer;
		_dispatchViewAction = dispatchViewAction;
		_operations = operations;
	}

	internal Task<WorkspaceViewCoordinatorPublicationResult> PublishPendingEditsAsync(string filePath, CancellationToken cancellationToken)
	{
		return _operations.RunAsync(async () =>
		{
			// The views are registered under the normalized id of the document, so another spelling of the path reaches them too.
			string documentId = LocalPath.TryNormalize(filePath, out string normalized) ? normalized : filePath;
			IWorkspaceDocumentView[] views = _views.GetPeers(documentId, sourceView: null);

			// A view raises ApplyRequested inside the dispatched action, so its apply is tracked by the time the action returns.
			await _dispatchViewAction(() => PublishPendingEdits(views)).ConfigureAwait(false);

			// A view that held a publication back raises it inside the acknowledgment of the earlier one, so the wait repeats until no apply of
			// the views is in flight.
			for (Task applied = _applies.WhenAppliedAsync(views); !applied.IsCompleted; applied = _applies.WhenAppliedAsync(views))
				await applied.WaitAsync(cancellationToken).ConfigureAwait(false);

			// The snapshot is read in the action of the final check, so a publication that typing scheduled on the views' thread cannot land
			// between them.
			List<WorkspaceDocumentViewSynchronizationIssue> blocking = [];
			WorkspaceDocumentSnapshot? published = null;

			await _dispatchViewAction(() =>
				{
					blocking = _synchronizer.GetBlockingViewIssues(views, includeUnsynchronizedState: true);

					if (blocking.Count == 0 && _store.TryGetSnapshot(documentId, out WorkspaceDocumentSnapshot? snapshot))
						published = snapshot;
				})
				.ConfigureAwait(false);

			return blocking.Count == 0
				? new WorkspaceViewCoordinatorPublicationResult(WorkspaceDocumentViewSynchronizationResult.Synchronized, published)
				: new WorkspaceViewCoordinatorPublicationResult(WorkspaceDocumentViewSynchronizationResult.Blocked(blocking));
		});
	}

	// The subscription entry point: a raise starts the apply pipeline without blocking the raising
	// view; the outcome reaches the publishing view through AcknowledgeApply.
	internal void HandleApplyRequested(object? sender, ApplyRequestedEventArgs eventArgs)
	{
		if (sender is not IWorkspaceDocumentView sourceView)
			return;

		_applies.Track(sourceView, HandleApplyRequestedAsync(sourceView, eventArgs));
	}

	// Runs inside a dispatched action. A view that throws is recorded as unsynchronized, so the blocking report that follows names it.
	private void PublishPendingEdits(IWorkspaceDocumentView[] views)
	{
		foreach (IWorkspaceDocumentView view in views)
		{
			if (view is not IWorkspaceDocumentPublishingView publishingView)
				continue;

			try
			{
				publishingView.PublishPendingEdits();
			}
			catch (Exception exception)
			{
				_views.RecordFailure(
					view,
					new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.ViewPublishFailed, exception.Message, exception)
				);
			}
		}
	}

	// An event-driven apply has no synchronous completion channel back to the raising code. The publishing
	// view learns the outcome through AcknowledgeApply (a replacement the store rejects is acknowledged with
	// the current state). A failure thrown by the apply path itself is recorded as unsynchronized view
	// state and blocks a delete and a directory delete until a successful refresh clears it. Callers
	// that need the mutation result call ReplaceAsync directly. The replacement registers a coordinator
	// operation, so a stop still waits for the fire-and-forget call.
	private async Task HandleApplyRequestedAsync(IWorkspaceDocumentView sourceView, ApplyRequestedEventArgs eventArgs)
	{
		try
		{
			if (!_views.IsRegisteredAndNotStopping(sourceView))
				return;

			await ReplaceCoreAsync(eventArgs.Request, sourceView).ConfigureAwait(false);
		}
		catch (ObjectDisposedException) when (!_views.IsRegisteredAndNotStopping(sourceView))
		{
			// A stop that began after the registration check rejected the apply. The stop owns the
			// view teardown, so the apply is not recorded as a view failure; a disposed store while
			// the coordinator is still active keeps the registration and falls through to the recording
			// path below.
		}
		catch (Exception exception)
		{
			_views.RecordFailure(
				sourceView,
				new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.ViewApplyFailed, exception.Message, exception)
			);

			// A text view holds its later publications back until this one is acknowledged, and no acknowledgment follows a thrown
			// apply. The call runs before the apply completes, so a save that waits for the apply finds the view free to publish.
			if (sourceView is WorkspaceDocumentTextViewBase textView && _views.IsRegisteredAndNotStopping(sourceView))
				await EndLostPublicationAsync(textView, eventArgs.Request).ConfigureAwait(false);
		}
	}

	// A dispatcher that faults here leaves the publication in flight, so the view stays recorded as unsynchronized with that fault, and the apply
	// still completes: a later wait on the applies of the view never rethrows the fault of an earlier one.
	private async Task EndLostPublicationAsync(WorkspaceDocumentTextViewBase view, WorkspaceDocumentReplaceRequest request)
	{
		try
		{
			await _dispatchViewAction(() => view.EndLostPublication(request)).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			_views.RecordFailure(
				view,
				new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.ViewApplyFailed, exception.Message, exception)
			);
		}
	}

	private Task<WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentMutationResult>> ReplaceCoreAsync(
		WorkspaceDocumentReplaceRequest request,
		IWorkspaceDocumentView? sourceView,
		CancellationToken cancellationToken = default
	)
	{
		return _operations.RunAsync(async () =>
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (sourceView is not null && !_views.IsRegisteredAndNotStopping(sourceView))
				throw new InvalidOperationException("A detached view published a document replacement.");

			// The store mutation runs on the caller's context, matching the other coordinator store
			// calls; the synchronizer carries the view acknowledgment and the peer refreshes in one
			// dispatched action, so no view access runs outside the dispatcher.
			WorkspaceDocumentMutationResult result = _store.Replace(request);
			_views.ObserveSnapshot(result.Snapshot);

			List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues = await _synchronizer
				.AcknowledgeReplacementAsync(result, sourceView)
				.ConfigureAwait(false);

			return WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentMutationResult>.FromStore(
				result,
				ComposeSynchronization(failedViewIssues)
			);
		});
	}
}
