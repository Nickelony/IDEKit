using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Attaches a view to a document the store opened, so the coordinator tracks the view from then on.</summary>
/// <remarks>
/// <para>
/// The registration check, the view's open and the registration itself run in one dispatched action, so a dispatcher that serializes its actions
/// cannot interleave a concurrent open of the same view instance between them.
/// </para>
/// <para>
/// The apply subscription follows the registration outside the coordinator's lock. A detach that wins the race between the two is detected
/// by identity and undone through the registration's own forwarder, so it never strips the handler of a later re-attach.
/// </para>
/// </remarks>
internal sealed class ViewAttacher
{
	// The detail reported when a view cannot supply a usable id, shared by attachment validation and
	// the attach path.
	private const string ViewIdUnreportedMessage = "The view did not report a view id.";

	private readonly ViewTracker _views;
	private readonly Func<Action, Task> _dispatchViewAction;
	private readonly IWorkspaceDocumentReader _documents;

	/// <param name="views">The tracker of the attached views.</param>
	/// <param name="dispatchViewAction">The delegate that runs actions where view access is legal.</param>
	/// <param name="documents">The document authority that resolves the snapshot of a registered view's document.</param>
	internal ViewAttacher(ViewTracker views, Func<Action, Task> dispatchViewAction, IWorkspaceDocumentReader documents)
	{
		_views = views;
		_dispatchViewAction = dispatchViewAction;
		_documents = documents;
	}

	// Finds the open result that rejects the view before the document is loaded, or null when the view may
	// attach, with the failure to report when the view could not report its id or state. The view-id read and
	// the state probe run through the dispatcher; duplicate detection uses the view ids captured at
	// registration, so no view member is read while the coordinator's lock is held. That lock is never held
	// across an await: taking it inside dispatched code is safe only because no member awaits a dispatcher
	// action while holding it. State-based unavailability (an existing document, pending edits, or a conflict)
	// carries no failure, since the host can inspect the view itself.
	internal async Task<WorkspaceViewCoordinatorOpenResult?> FindRejectionAsync(IWorkspaceDocumentView view)
	{
		WorkspaceViewCoordinatorOpenOutcome? rejection = null;
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
					rejection = WorkspaceViewCoordinatorOpenOutcome.ViewUnavailable;
					failure = new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.OpenFailed, exception.Message, exception);
					return;
				}

				if (string.IsNullOrWhiteSpace(viewId))
				{
					rejection = WorkspaceViewCoordinatorOpenOutcome.ViewUnavailable;
					failure = new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.OpenFailed, ViewIdUnreportedMessage);
					return;
				}

				ViewTracker.Claim claim = _views.FindClaim(view, viewId);

				if (claim == ViewTracker.Claim.ViewRegistered)
				{
					rejection = WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen;
					return;
				}

				if (claim == ViewTracker.Claim.ViewIdInUse)
				{
					rejection = WorkspaceViewCoordinatorOpenOutcome.ViewInUse;
					return;
				}

				try
				{
					if (view.DocumentKey is not null || view.HasPendingEdits || view.HasConflict)
						rejection = WorkspaceViewCoordinatorOpenOutcome.ViewUnavailable;
				}
				catch (Exception exception)
				{
					rejection = WorkspaceViewCoordinatorOpenOutcome.ViewUnavailable;
					failure = new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.OpenFailed, exception.Message, exception);
				}
			})
			.ConfigureAwait(false);

		return rejection switch
		{
			null => null,
			WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen => new(WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen, GetRegisteredViewSnapshot(view)),
			WorkspaceViewCoordinatorOpenOutcome.ViewInUse => new(WorkspaceViewCoordinatorOpenOutcome.ViewInUse, null),
			_ => new(WorkspaceViewCoordinatorOpenOutcome.ViewUnavailable, null, failure),
		};
	}

	// Opens the document through the store and attaches the view to it. A stop that starts during the open or the attach is reported as
	// disposal, and the store's invalid-open-options contract is kept instead of degrading the error into a result; any other failure
	// becomes an OpenFailed or Canceled result.
	internal async Task<WorkspaceViewCoordinatorOpenResult> OpenWithViewAsync(
		IWorkspaceDocumentView view,
		Func<Task<WorkspaceDocumentOpenResult>> openAsync
	)
	{
		WorkspaceDocumentOpenResult openResult;

		try
		{
			WorkspaceViewCoordinatorOpenResult? rejection = await FindRejectionAsync(view).ConfigureAwait(false);

			if (rejection is not null)
				return rejection;

			openResult = await openAsync().ConfigureAwait(false);
		}
		catch (Exception exception) when (RethrowsOpenFailure(exception, preserveArgumentError: true))
		{
			throw;
		}
		catch (Exception exception)
		{
			return MapOpenFailure(exception);
		}

		try
		{
			return await AttachAsync(view, openResult).ConfigureAwait(false);
		}
		catch (Exception exception) when (RethrowsOpenFailure(exception, preserveArgumentError: false))
		{
			throw;
		}
		catch (Exception exception)
		{
			return MapOpenFailure(exception);
		}
	}

	/// <summary>
	/// Determines whether an open failure propagates to the caller instead of being mapped to a result: a disposal always propagates, and the phase
	/// that runs the store open also keeps the store's invalid-open-options argument-error contract.
	/// </summary>
	private static bool RethrowsOpenFailure(Exception exception, bool preserveArgumentError) =>
		exception is ObjectDisposedException || (preserveArgumentError && exception is ArgumentException);

	/// <summary>
	/// Maps an open failure that does not propagate
	/// to a result: cancellation is reported as <see cref="WorkspaceViewCoordinatorOpenOutcome.Canceled"/>, and any other failure becomes
	/// an <see cref="WorkspaceViewCoordinatorOpenOutcome.OpenFailed"/> result.
	/// </summary>
	private static WorkspaceViewCoordinatorOpenResult MapOpenFailure(Exception exception)
	{
		return exception is OperationCanceledException
			? new WorkspaceViewCoordinatorOpenResult(WorkspaceViewCoordinatorOpenOutcome.Canceled, null)
			: new WorkspaceViewCoordinatorOpenResult(
				WorkspaceViewCoordinatorOpenOutcome.OpenFailed,
				null,
				new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.OpenFailed, exception.Message, exception)
			);
	}

	internal async Task<WorkspaceViewCoordinatorOpenResult> AttachAsync(IWorkspaceDocumentView view, WorkspaceDocumentOpenResult openResult)
	{
		if (openResult.Outcome is not WorkspaceDocumentOpenOutcome.Opened and not WorkspaceDocumentOpenOutcome.AlreadyOpen)
		{
			return new WorkspaceViewCoordinatorOpenResult(
				Outcome: WorkspaceViewOutcomeConversion.ToOpenOutcome(openResult.Outcome),
				Snapshot: null,
				Failure: openResult.Failure,
				StoreResult: openResult
			);
		}

		WorkspaceDocumentSnapshot snapshot =
			openResult.Snapshot ?? throw new InvalidOperationException("A successful document open must include a snapshot.");

		bool registered = false;

		try
		{
			// The registration check, the attach, and the registration itself run in one dispatched
			// action: a dispatcher that serializes its actions (the documented obligation for views that
			// are not thread-safe) cannot interleave a concurrent open of the same instance between
			// them, so the second open resolves through the already-registered paths.
			ViewAttachResult registration = await RegisterAttachedViewAsync(view, snapshot).ConfigureAwait(false);
			WorkspaceDocumentViewOpenResult? attachResult = registration.OpenResult;
			string? viewId = registration.ViewId;

			bool alreadyRegistered = registration.AlreadyRegistered;
			bool closeDuplicateAttachment = registration.CloseDuplicateAttachment;

			registered = registration.Registered;

			if (alreadyRegistered)
			{
				if (closeDuplicateAttachment)
				{
					await CloseAfterFailedOpenAsync(view).ConfigureAwait(false);
					return new WorkspaceViewCoordinatorOpenResult(WorkspaceViewCoordinatorOpenOutcome.ViewInUse, snapshot, StoreResult: openResult);
				}

				// The view is already registered: the result reports the snapshot of the document the
				// view is attached to (or null when that document is not tracked), matching the
				// short-circuit in FindRejectionAsync. A concurrent open can register the instance
				// while the store open runs, so this branch is reached even with a serializing delegate.
				// Only a delegate that does not serialize its actions can register it after this call's
				// view.Open already ran.
				return new WorkspaceViewCoordinatorOpenResult(
					WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen,
					GetRegisteredViewSnapshot(view),
					StoreResult: openResult
				);
			}

			WorkspaceDocumentViewOpenResult result = attachResult ?? throw new InvalidOperationException("The view did not return an attach result.");

			if (result.Outcome == WorkspaceDocumentViewOpenOutcome.AlreadyOpen)
			{
				// The view reports that it is already attached. When a concurrent attach registered the same
				// instance concurrently - possible when the dispatcher does not serialize its actions -
				// the open is a no-op reported as AlreadyOpen; otherwise, the view is attached elsewhere
				// and stays untouched.
				if (_views.Contains(view))
				{
					return new WorkspaceViewCoordinatorOpenResult(
						WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen,
						GetRegisteredViewSnapshot(view),
						StoreResult: openResult
					);
				}

				return new WorkspaceViewCoordinatorOpenResult(
					Outcome: WorkspaceViewCoordinatorOpenOutcome.ViewInUse,
					snapshot,
					Failure: result.Failure,
					StoreResult: openResult
				);
			}

			if (result.Outcome != WorkspaceDocumentViewOpenOutcome.Opened)
			{
				await CloseAfterFailedOpenAsync(view).ConfigureAwait(false);

				return new WorkspaceViewCoordinatorOpenResult(
					Outcome: WorkspaceViewCoordinatorOpenOutcome.ViewRejected,
					snapshot,

					Failure: result.Failure
						?? new WorkspaceOperationFailure(
							WorkspaceViewOperationFailureCodes.OpenFailed,
							"The view did not accept the workspace document attachment."
						),

					StoreResult: openResult
				);
			}

			if (string.IsNullOrWhiteSpace(viewId))
			{
				// A view without a usable id cannot be indexed for duplicate detection or reported in
				// results.
				await CloseAfterFailedOpenAsync(view).ConfigureAwait(false);

				return new WorkspaceViewCoordinatorOpenResult(
					Outcome: WorkspaceViewCoordinatorOpenOutcome.ViewRejected,
					snapshot,
					Failure: new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.OpenFailed, ViewIdUnreportedMessage),
					StoreResult: openResult
				);
			}

			// The dispatched action that opened the view with a usable id also registered it.
			if (registration.Entry is not { } attachRegistration)
				throw new InvalidOperationException("The view opened without being registered.");

			// The subscription and the registration must end in the same state; a concurrent
			// DetachView can win the race, in which case the attach reports AlreadyOpen.
			WorkspaceViewCoordinatorOpenResult? superseded = await SubscribeAttachedViewAsync(view, attachRegistration, openResult)
				.ConfigureAwait(false);

			if (superseded is not null)
				return superseded;

			// A replacement that reached the store between its open and this registration found the document without a view, so the tracker
			// learns the current snapshot now, outside its lock, and the operation raises the content change after the open.
			if (_documents.TryGetSnapshot(snapshot.DocumentId, out WorkspaceDocumentSnapshot? current) && current.DocumentKey == snapshot.DocumentKey)
				_views.ObserveSnapshot(current);

			return new WorkspaceViewCoordinatorOpenResult(WorkspaceViewCoordinatorOpenOutcome.Opened, snapshot, StoreResult: openResult);
		}
		catch
		{
			if (!registered)
				await CloseAfterFailedOpenAsync(view).ConfigureAwait(false);

			throw;
		}
	}

	// Performs the dispatched registration step for a successfully opened document: under one
	// dispatched action it checks for an existing registration, invokes the view's open, and, when the
	// view accepted the document with a usable id, registers the attachment. Returns the state the
	// attach path branches on afterwards.
	private async Task<ViewAttachResult> RegisterAttachedViewAsync(IWorkspaceDocumentView view, WorkspaceDocumentSnapshot snapshot)
	{
		// A dispatcher that never runs the action leaves the default, which the attach path reports as a view without an attach result.
		ViewAttachResult result = default;

		await _dispatchViewAction(() =>
			{
				if (!_views.TryAdmitAttach(view))
				{
					result = ViewAttachResult.NotAdmitted();
					return;
				}

				WorkspaceDocumentViewOpenResult openResult = view.Open(snapshot);
				string? viewId = view.ViewId;

				if (openResult.Outcome != WorkspaceDocumentViewOpenOutcome.Opened || string.IsNullOrWhiteSpace(viewId))
				{
					result = ViewAttachResult.Unregistered(openResult, viewId);
					return;
				}

				// A concurrent second open of the same instance must not close the registration the first call
				// created; only a different instance duplicating an existing ViewId is detached as a failed attach.
				ViewTracker.RegisterResult registration = _views.Register(view, viewId, snapshot);

				result = registration.Entry is { } entry
					? ViewAttachResult.Created(openResult, viewId, entry)
					: ViewAttachResult.Duplicate(openResult, viewId, registration.CloseDuplicateAttachment);
			})
			.ConfigureAwait(false);

		return result;
	}

	// Subscribes a registered view to its apply requests through the registration's own forwarder
	// and re-checks that the live registration is still the one this attach created. Returns the
	// already-open result when a concurrent detach won the race, or null when the subscription is
	// live.
	private async Task<WorkspaceViewCoordinatorOpenResult?> SubscribeAttachedViewAsync(
		IWorkspaceDocumentView view,
		ViewEntry registration,
		WorkspaceDocumentOpenResult openResult
	)
	{
		// The event subscription is the one view member the attacher touches directly, and the accessor
		// belongs to the view: subscribing outside the coordinator's lock keeps a blocking or marshaling accessor from
		// stalling every other coordinator operation. The add runs on the thread that resumes this method, which is
		// not necessarily the dispatch thread or the caller's thread for an asynchronous delegate, so event
		// accessors must be usable from any thread. A raise between the registration above and the subscription
		// is dropped; the same raise would have been ignored while the view was detached, so the contract holds.
		try
		{
			view.ApplyRequested += registration.Forwarder.Handler;
		}
		catch
		{
			// A view without a working event channel cannot be coordinated: detach this registration
			// and report the attach as failed instead of tracking a view that never publishes. The
			// detach is leased by identity so it cannot remove a registration a concurrent re-attach
			// created after this one was replaced.
			_views.RemoveIfCurrent(view, registration);

			await CloseAfterFailedOpenAsync(view).ConfigureAwait(false);
			throw;
		}

		// The subscription and the registration must end in the same state. A concurrent
		// DetachView can remove this registration after the dispatched action above and run
		// its own removal before this add lands, and a concurrent re-attach can then register the same
		// view again. The re-check therefore confirms that the live registration is the one this attach
		// created rather than that some registration exists, and the undo removes the add through this
		// registration's own forwarder, so it can never strip the re-attach's handler.
		if (!_views.IsCurrent(view, registration))
		{
			try
			{
				view.ApplyRequested -= registration.Forwarder.Handler;
			}
			catch (Exception)
			{
				// The registration is already gone, so a throwing accessor must not fault the open; the subscription, when the add
				// took effect, is abandoned with the detached view, and the attacher has no logger to report it.
			}

			// The concurrent detach won: the document was opened, but the view is not
			// attached, so the result reports the same shape as an already-registered view - the
			// (null) registered snapshot and the store result.
			return new WorkspaceViewCoordinatorOpenResult(
				WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen,
				GetRegisteredViewSnapshot(view),
				StoreResult: openResult
			);
		}

		return null;
	}

	// The state the attach path branches on after the dispatched registration step: the view's own open
	// result, its id, and what the registration did.
	private readonly struct ViewAttachResult
	{
		private ViewAttachResult(
			WorkspaceDocumentViewOpenResult? openResult,
			string? viewId,
			bool alreadyRegistered,
			bool closeDuplicateAttachment,
			ViewEntry? entry
		)
		{
			OpenResult = openResult;
			ViewId = viewId;
			AlreadyRegistered = alreadyRegistered;
			CloseDuplicateAttachment = closeDuplicateAttachment;
			Entry = entry;
		}

		// The result of the view's own open, which is null exactly when the attach was not admitted.
		internal WorkspaceDocumentViewOpenResult? OpenResult { get; }

		internal string? ViewId { get; }

		internal bool AlreadyRegistered { get; }

		internal bool CloseDuplicateAttachment { get; }

		// The entry this attach created, which is null unless the view was registered.
		internal ViewEntry? Entry { get; }

		internal bool Registered => Entry is not null;

		// The view was already registered, so its open never ran.
		internal static ViewAttachResult NotAdmitted() =>
			new(openResult: null, viewId: null, alreadyRegistered: true, closeDuplicateAttachment: false, entry: null);

		// The view opened but the attach stops before registration, because the view rejected the document or reported no usable id.
		internal static ViewAttachResult Unregistered(WorkspaceDocumentViewOpenResult openResult, string? viewId) =>
			new(openResult, viewId, alreadyRegistered: false, closeDuplicateAttachment: false, entry: null);

		// The view opened, and the same instance or the same id was registered meanwhile.
		internal static ViewAttachResult Duplicate(WorkspaceDocumentViewOpenResult openResult, string viewId, bool closeDuplicateAttachment) =>
			new(openResult, viewId, alreadyRegistered: true, closeDuplicateAttachment, entry: null);

		internal static ViewAttachResult Created(WorkspaceDocumentViewOpenResult openResult, string viewId, ViewEntry entry) =>
			new(openResult, viewId, alreadyRegistered: false, closeDuplicateAttachment: false, entry);
	}

	/// <summary>Removes a view's registration and unsubscribes from its apply requests.</summary>
	/// <param name="view">The view instance.</param>
	/// <remarks>
	/// The registration is removed before the unsubscription, so later raises are ignored; a throwing event accessor must not fault the caller,
	/// matching the stop and delete teardown paths.
	/// </remarks>
	internal void Detach(IWorkspaceDocumentView view)
	{
		ViewEntry? registration = _views.Remove(view);

		if (registration is null)
			return;

		try
		{
			view.ApplyRequested -= registration.Forwarder.Handler;
		}
		catch (Exception)
		{
			// The registration is already removed, so later raises are ignored, and Detach has no result or logger to carry the fault.
		}
	}

	/// <summary>Closes and detaches a view whose document was removed, reporting a failed close in <paramref name="failedViewIssues"/>.</summary>
	/// <param name="view">The view to close.</param>
	/// <param name="failedViewIssues">Receives an issue that carries the thrown exception when the view's close throws.</param>
	/// <remarks>
	/// Runs inside a dispatched action. A view whose close fails is still detached. The detach happens first so a view
	/// that raises <see cref="IWorkspaceDocumentView.ApplyRequested"/> from <see cref="IWorkspaceDocumentView.Close"/> cannot re-enter a replacement
	/// through a handler that is about to be removed. The close is unconditional: <see cref="Detach"/> does not close the view, so a captured view
	/// that was only detached is still closed here, and a view the host already closed is closed again, which Close documents as safe.
	/// </remarks>
	internal void CloseDeletedView(IWorkspaceDocumentView view, List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues)
	{
		Detach(view);

		try
		{
			view.Close();
		}
		catch (Exception exception)
		{
			failedViewIssues.Add(
				new WorkspaceDocumentViewSynchronizationIssue(
					ViewIdReader.Read(view),
					new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.ViewCloseFailed, exception.Message, exception)
				)
			);
		}
	}

	/// <summary>Closes the views that attached while a store operation was in flight and are still attached to the removed instance.</summary>
	/// <param name="documentId">The id of the removed document.</param>
	/// <param name="deletedDocumentKey">The key of the removed document instance.</param>
	/// <param name="failedViewIssues">Receives an issue for each view whose close throws.</param>
	/// <remarks>
	/// A view attached to a document reopened at the same path carries a new document key and is left alone.
	/// </remarks>
	internal void CloseLateAttachedViews(
		string documentId,
		WorkspaceDocumentKey deletedDocumentKey,
		List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues
	)
	{
		foreach (IWorkspaceDocumentView view in _views.GetPeers(documentId, sourceView: null))
		{
			if (TargetsDeletedInstance(view, deletedDocumentKey))
				CloseDeletedView(view, failedViewIssues);
		}
	}

	// A view that reports no document key, or throws while reporting one, is treated as attached to the removed instance; a view
	// attached to a document reopened at the same path reports the new key and is left alone.
	private static bool TargetsDeletedInstance(IWorkspaceDocumentView view, WorkspaceDocumentKey deletedDocumentKey)
	{
		try
		{
			return view.DocumentKey is not { } documentKey || documentKey == deletedDocumentKey;
		}
		catch (Exception)
		{
			return true;
		}
	}

	private async Task CloseAfterFailedOpenAsync(IWorkspaceDocumentView view)
	{
		try
		{
			await _dispatchViewAction(view.Close).ConfigureAwait(false);
		}
		catch (Exception)
		{
			// The caller reports the failure that made the open fail, as a rejected result or as the rethrown exception, and a fault
			// from closing the view must not replace it; the attacher has no logger to carry it.
		}
	}

	// Resolves the current snapshot of the document a registered view is attached to, or null when
	// the view is not registered or its document is not tracked. The document id is read from the
	// tracker; the snapshot is resolved through the document authority.
	private WorkspaceDocumentSnapshot? GetRegisteredViewSnapshot(IWorkspaceDocumentView view)
	{
		string? documentId = _views.GetDocumentId(view);

		if (documentId is null)
			return null;

		return _documents.TryGetSnapshot(documentId, out WorkspaceDocumentSnapshot? snapshot) ? snapshot : null;
	}
}
