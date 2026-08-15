using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

public sealed partial class WorkspaceDocumentManager
{
	/// <inheritdoc/>
	public async Task<WorkspaceDocumentManagerOpenResult> OpenAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		CancellationToken cancellationToken = default)
	{
		WorkspaceDocumentOpenResult openResult = await RunOperationAsync(
			() => _store.OpenAsync(filePath, options, cancellationToken)).ConfigureAwait(false);

		return openResult.Outcome switch
		{
			WorkspaceDocumentOpenOutcome.Opened => new(WorkspaceDocumentManagerOpenOutcome.Opened, openResult.Snapshot, StoreResult: openResult),
			WorkspaceDocumentOpenOutcome.AlreadyOpen => new(WorkspaceDocumentManagerOpenOutcome.AlreadyOpen, openResult.Snapshot, StoreResult: openResult),
			_ => new(MapOpenOutcome(openResult.Outcome), null, openResult.Failure, openResult)
		};
	}

	/// <inheritdoc/>
	public Task<WorkspaceDocumentManagerOpenResult> OpenWithViewAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		IWorkspaceDocumentView view,
		CancellationToken cancellationToken = default)
	{
		// Validated before dispatch so a null view throws synchronously instead of surfacing as a
		// faulted task, matching the manager's other argument validation.
		ArgumentNullException.ThrowIfNull(view);

		return RunOperationAsync(async () =>
		{
			WorkspaceDocumentOpenResult openResult;

			try
			{
				(ViewAttachmentOutcome validation, WorkspaceOperationFailure? validationFailure) = await ValidateViewAsync(view).ConfigureAwait(false);

				if (validation == ViewAttachmentOutcome.AlreadyRegistered)
					return new WorkspaceDocumentManagerOpenResult(
						WorkspaceDocumentManagerOpenOutcome.AlreadyOpen,
						GetRegisteredViewSnapshot(view));

				if (validation == ViewAttachmentOutcome.DuplicateViewId)
					return new WorkspaceDocumentManagerOpenResult(
						WorkspaceDocumentManagerOpenOutcome.ViewInUse,
						null);

				if (validation == ViewAttachmentOutcome.Unavailable)
					return new WorkspaceDocumentManagerOpenResult(
						WorkspaceDocumentManagerOpenOutcome.ViewUnavailable,
						null,
						validationFailure);

				openResult = await _store
					.OpenAsync(filePath, options, cancellationToken)
					.ConfigureAwait(false);
			}
			catch (Exception exception) when (RethrowsOpenFailure(exception, preserveArgumentError: true))
			{
				// A stop that starts during the open is reported as disposal, and the store's
				// invalid-open-options contract is kept instead of degrading the error into a result.
				throw;
			}
			catch (Exception exception)
			{
				return MapOpenFailure(exception);
			}

			try
			{
				return await AttachViewAsync(view, openResult).ConfigureAwait(false);
			}
			catch (Exception exception) when (RethrowsOpenFailure(exception, preserveArgumentError: false))
			{
				// A stop that starts while the view is being attached is reported as disposal, matching the
				// documented contract for operations that run while the manager stops.
				throw;
			}
			catch (Exception exception)
			{
				return MapOpenFailure(exception);
			}
		});
	}

	/// <summary>
	/// Determines whether an open failure propagates to the caller instead of being mapped to a result:
	/// a disposal always propagates, and the phase that runs the store open also keeps the store's
	/// invalid-open-options argument-error contract.
	/// </summary>
	/// <param name="exception">The failure raised by the open or attach phase.</param>
	/// <param name="preserveArgumentError">
	/// <see langword="true"/> for the phase that runs the store open, whose argument errors are contract
	/// violations that must escape; <see langword="false"/> for the attach phase.
	/// </param>
	/// <returns><see langword="true"/> when the failure must propagate; otherwise, <see langword="false"/>.</returns>
	private static bool RethrowsOpenFailure(Exception exception, bool preserveArgumentError)
		=> exception is ObjectDisposedException || (preserveArgumentError && exception is ArgumentException);

	/// <summary>
	/// Maps an open failure that does not propagate to a result: cancellation is reported as
	/// <see cref="WorkspaceDocumentManagerOpenOutcome.Canceled"/>, and any other failure becomes an
	/// <see cref="WorkspaceDocumentManagerOpenOutcome.OpenFailed"/> result.
	/// </summary>
	/// <param name="exception">The failure raised by the open or attach phase.</param>
	/// <returns>The mapped result.</returns>
	private static WorkspaceDocumentManagerOpenResult MapOpenFailure(Exception exception)
		=> exception is OperationCanceledException
			? new WorkspaceDocumentManagerOpenResult(WorkspaceDocumentManagerOpenOutcome.Canceled, null)
			: CreateOpenFailedResult(exception);

	private async Task<WorkspaceDocumentManagerOpenResult> AttachViewAsync(
		IWorkspaceDocumentView view,
		WorkspaceDocumentOpenResult openResult)
	{
		if (openResult.Outcome != WorkspaceDocumentOpenOutcome.Opened
			&& openResult.Outcome != WorkspaceDocumentOpenOutcome.AlreadyOpen)
		{
			return new WorkspaceDocumentManagerOpenResult(
				MapOpenOutcome(openResult.Outcome),
				null,
				openResult.Failure,
				openResult);
		}

		WorkspaceDocumentSnapshot snapshot = openResult.Snapshot
			?? throw new InvalidOperationException("A successful document open must include a snapshot.");
		bool registered = false;

		try
		{
			// The registration check, the attach, and the registration itself run in one dispatched
			// action: a dispatcher that serializes its actions (the documented obligation for views that
			// are not thread-safe) cannot interleave a concurrent open of the same instance between
			// them, so the second open resolves through the already-registered paths.
			ViewAttachRegistration registration = await RegisterAttachedViewAsync(view, snapshot).ConfigureAwait(false);
			WorkspaceDocumentViewOpenResult? attachResult = registration.AttachResult;
			string? viewId = registration.ViewId;
			bool alreadyRegistered = registration.AlreadyRegistered;
			bool closeDuplicateAttachment = registration.CloseDuplicateAttachment;
			ViewRegistration attachRegistration = registration.Registration!;
			registered = registration.Registered;

			if (alreadyRegistered)
			{
				if (closeDuplicateAttachment)
				{
					await CloseAfterFailedOpenAsync(view).ConfigureAwait(false);
					return new WorkspaceDocumentManagerOpenResult(
						WorkspaceDocumentManagerOpenOutcome.ViewInUse,
						snapshot,
						StoreResult: openResult);
				}

				// The view is already registered: the result reports the snapshot of the document the
				// view is attached to (or null when that document is no longer tracked), matching the
				// short-circuit path in OpenWithViewAsync. This branch is reachable only through a delegate
				// that does not serialize its actions: a concurrent open registered the instance after this
				// call's first check ran, while this call's attach had already invoked the view; a
				// serializing delegate answers through the first check instead.
				return new WorkspaceDocumentManagerOpenResult(
					WorkspaceDocumentManagerOpenOutcome.AlreadyOpen,
					GetRegisteredViewSnapshot(view),
					StoreResult: openResult);
			}

			WorkspaceDocumentViewOpenResult result = attachResult
				?? throw new InvalidOperationException("The view did not return an attach result.");
			if (result.Outcome == WorkspaceDocumentViewOpenOutcome.AlreadyOpen)
			{
				// The view reports that it is already attached. When this manager registered the same
				// instance concurrently - possible when the dispatcher does not serialize its actions -
				// the open is a no-op reported as AlreadyOpen; otherwise, the view is attached elsewhere
				// and stays untouched.
				bool sameInstanceRegistered;
				lock (_stateLock)
					sameInstanceRegistered = _registrations.ContainsKey(view);

				if (sameInstanceRegistered)
					return new WorkspaceDocumentManagerOpenResult(
						WorkspaceDocumentManagerOpenOutcome.AlreadyOpen,
						GetRegisteredViewSnapshot(view),
						StoreResult: openResult);

				return new WorkspaceDocumentManagerOpenResult(
					WorkspaceDocumentManagerOpenOutcome.ViewInUse,
					snapshot,
					result.Failure,
					openResult);
			}

			if (result.Outcome != WorkspaceDocumentViewOpenOutcome.Opened)
			{
				await CloseAfterFailedOpenAsync(view).ConfigureAwait(false);
				return new WorkspaceDocumentManagerOpenResult(
					WorkspaceDocumentManagerOpenOutcome.ViewRejected,
					snapshot,
					result.Failure ?? new WorkspaceOperationFailure(
						WorkspaceViewOperationFailureCodes.OpenFailed,
						"The view did not accept the workspace document attachment."),
					openResult);
			}

			if (string.IsNullOrWhiteSpace(viewId))
			{
				// A view without a usable id cannot be indexed for duplicate detection or reported in
				// results.
				await CloseAfterFailedOpenAsync(view).ConfigureAwait(false);
				return new WorkspaceDocumentManagerOpenResult(
					WorkspaceDocumentManagerOpenOutcome.ViewRejected,
					snapshot,
					new WorkspaceOperationFailure(
						WorkspaceViewOperationFailureCodes.OpenFailed,
						ViewIdUnreportedMessage),
					openResult);
			}

			// The subscription and the registration must end in the same state; a concurrent
			// UnregisterOpenView can win the race, in which case the attach reports AlreadyOpen.
			WorkspaceDocumentManagerOpenResult? superseded = await SubscribeAttachedViewAsync(
				view,
				attachRegistration,
				openResult).ConfigureAwait(false);
			if (superseded is not null)
				return superseded;

			return new WorkspaceDocumentManagerOpenResult(
				WorkspaceDocumentManagerOpenOutcome.Opened,
				snapshot,
				StoreResult: openResult);
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
	private async Task<ViewAttachRegistration> RegisterAttachedViewAsync(
		IWorkspaceDocumentView view,
		WorkspaceDocumentSnapshot snapshot)
	{
		WorkspaceDocumentViewOpenResult? attachResult = null;
		string? viewId = null;
		bool alreadyRegistered = false;
		bool closeDuplicateAttachment = false;
		bool registered = false;
		ViewRegistration? createdRegistration = null;

		await _dispatchViewAction(() =>
		{
			lock (_stateLock)
			{
				ThrowIfStoppingUnderLock();

				if (_registrations.ContainsKey(view))
				{
					alreadyRegistered = true;
					return;
				}
			}

			attachResult = view.Open(snapshot);
			viewId = view.ViewId;

			if (attachResult.Outcome != WorkspaceDocumentViewOpenOutcome.Opened
				|| string.IsNullOrWhiteSpace(viewId))
				return;

			lock (_stateLock)
			{
				ThrowIfStoppingUnderLock();

				bool sameInstance = _registrations.ContainsKey(view);
				alreadyRegistered = sameInstance || _viewsByViewId.ContainsKey(viewId);

				// A concurrent second open of the same instance must not close the registration the
				// first call just made; only a different instance duplicating an existing ViewId is
				// detached as a failed attach.
				closeDuplicateAttachment = alreadyRegistered && !sameInstance;
				if (!alreadyRegistered)
				{
					createdRegistration = new ViewRegistration(
						viewId,
						snapshot.DocumentId,
						snapshot.DocumentKey,
						snapshot.Version,
						new ApplyRequestedSubscription(this));
					_registrations.Add(view, createdRegistration);
					_viewsByViewId.Add(viewId, view);
					if (!_viewsByDocument.TryGetValue(snapshot.DocumentId, out List<IWorkspaceDocumentView>? documentViews))
					{
						documentViews = [];
						_viewsByDocument.Add(snapshot.DocumentId, documentViews);
					}

					documentViews.Add(view);
					registered = true;
				}
			}
		}).ConfigureAwait(false);

		return new ViewAttachRegistration
		{
			AttachResult = attachResult,
			ViewId = viewId,
			AlreadyRegistered = alreadyRegistered,
			CloseDuplicateAttachment = closeDuplicateAttachment,
			Registered = registered,
			Registration = createdRegistration
		};
	}

	// Subscribes a registered view to its apply requests through the registration's own subscription
	// token and re-checks that the live registration is still the one this attach created. Returns the
	// already-open result when a concurrent unregister won the race, or null when the subscription is
	// live.
	private async Task<WorkspaceDocumentManagerOpenResult?> SubscribeAttachedViewAsync(
		IWorkspaceDocumentView view,
		ViewRegistration registration,
		WorkspaceDocumentOpenResult openResult)
	{
		// The event subscription is the one view member the manager touches directly, and the
		// accessor belongs to the view: subscribing outside the state lock keeps a blocking or
		// marshaling accessor from stalling every other manager operation. The add runs on the
		// thread that resumes this method, which is not necessarily the dispatch thread or the
		// caller's thread for an asynchronous delegate, so event accessors must be usable from any
		// thread. A raise that happens between the registration above and the subscription is
		// dropped; the same raise would have been ignored while the view was unregistered, so the
		// registration contract is unchanged.
		try
		{
			view.ApplyRequested += registration.Subscription.Handler;
		}
		catch
		{
			// A view without a working event channel cannot be coordinated: detach this registration
			// and report the attach as failed instead of tracking a view that never publishes. The
			// detach is guarded by identity so it cannot remove a registration a concurrent re-attach
			// created after this one was replaced.
			lock (_stateLock)
				RemoveRegistrationLockedIfCurrent(view, registration);

			await CloseAfterFailedOpenAsync(view).ConfigureAwait(false);
			throw;
		}

		// The subscription and the registration must end in the same state. A concurrent
		// UnregisterOpenView can remove this registration after the dispatched action above and run
		// its own removal before this add lands, and a concurrent re-attach can then register the same
		// view again. The re-check therefore confirms that the live registration is the one this attach
		// created rather than that some registration exists, and the undo removes the add through this
		// registration's own token, so it can never strip the re-attach's handler.
		bool stillRegistered;
		lock (_stateLock)
			stillRegistered = _registrations.TryGetValue(view, out ViewRegistration? current)
				&& ReferenceEquals(current, registration);

		if (!stillRegistered)
		{
			try
			{
				view.ApplyRequested -= registration.Subscription.Handler;
			}
			catch
			{
				// The registration is already gone, so a throwing accessor must not fault the open;
				// the subscription, when the add took effect, is abandoned with the unregistered view.
			}

			// The concurrent unregister won: the document was opened, but the view is no longer
			// attached, so the result reports the same shape as an already-registered view - the
			// (null) registered snapshot and the store result.
			return new WorkspaceDocumentManagerOpenResult(
				WorkspaceDocumentManagerOpenOutcome.AlreadyOpen,
				GetRegisteredViewSnapshot(view),
				StoreResult: openResult);
		}

		return null;
	}

	// The state the attach path branches on after the dispatched registration step: the view's own open
	// result, its id, and the registration flags.
	private readonly struct ViewAttachRegistration
	{
		public WorkspaceDocumentViewOpenResult? AttachResult { get; init; }
		public string? ViewId { get; init; }
		public bool AlreadyRegistered { get; init; }
		public bool CloseDuplicateAttachment { get; init; }
		public bool Registered { get; init; }
		public ViewRegistration? Registration { get; init; }
	}

	// Every named store outcome is mapped explicitly; an unmapped value throws instead of silently
	// degrading into OpenFailed. The store outcomes are a closed set defined by this library, so a
	// value outside it means the store implementation (or a version mismatch) violates the contract;
	// mapping it to OpenFailed would hide that. The open-outcome test enumerates the store outcomes
	// and exercises each one, so adding a store outcome fails that test until it is mapped here.
	private static WorkspaceDocumentManagerOpenOutcome MapOpenOutcome(WorkspaceDocumentOpenOutcome outcome)
		=> outcome switch
		{
			WorkspaceDocumentOpenOutcome.Opened => WorkspaceDocumentManagerOpenOutcome.Opened,
			WorkspaceDocumentOpenOutcome.AlreadyOpen => WorkspaceDocumentManagerOpenOutcome.AlreadyOpen,
			WorkspaceDocumentOpenOutcome.InvalidPath => WorkspaceDocumentManagerOpenOutcome.InvalidPath,
			WorkspaceDocumentOpenOutcome.NotFound => WorkspaceDocumentManagerOpenOutcome.NotFound,
			WorkspaceDocumentOpenOutcome.IsDirectory => WorkspaceDocumentManagerOpenOutcome.IsDirectory,
			WorkspaceDocumentOpenOutcome.LoadFailed => WorkspaceDocumentManagerOpenOutcome.LoadFailed,
			WorkspaceDocumentOpenOutcome.Canceled => WorkspaceDocumentManagerOpenOutcome.Canceled,
			_ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "The store returned an unmapped open outcome.")
		};

	private static WorkspaceDocumentManagerOpenResult CreateOpenFailedResult(Exception exception)
		=> new(
			WorkspaceDocumentManagerOpenOutcome.OpenFailed,
			null,
			new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.OpenFailed, exception.Message, exception));

	private async Task CloseAfterFailedOpenAsync(IWorkspaceDocumentView view)
	{
		try
		{
			await _dispatchViewAction(view.Close).ConfigureAwait(false);
		}
		catch
		{ }
	}
}
