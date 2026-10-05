using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Coordinates view attachment, publication, and synchronization with workspace document authority.</summary>
/// <remarks>
/// <para>
/// The coordinator tracks its active operations and its registered views. It tracks views by normalized document id and records failed view
/// synchronization as unsynchronized, retaining the failure detail for reporting; a successful refresh or identity acknowledgment clears that state,
/// while detaching the view always removes it. The recovery operations (commit, reload, and conflict resolution) retry the synchronization of a view
/// whose only outstanding state is such a failure instead of blocking the operation permanently.
/// </para>
/// <para>
/// The coordinator never reads or invokes a view member while holding its own state lock. A throwing view accessor is isolated:
/// reads used for bookkeeping degrade to a safe default, and the operation reports the view as unsynchronized where it can report a result.
/// </para>
/// </remarks>
public sealed class WorkspaceViewCoordinator : IWorkspaceViewCoordinator
{
	private readonly IWorkspaceDocumentStore _store;
	private readonly ViewTracker _views;
	private readonly ViewAttacher _attacher;
	private readonly AttachedDocumentChangeForwarder _attachedChanges;
	private readonly CoordinatorOperationRunner _operations;
	private readonly CoordinatorTeardown _teardown;
	private readonly DestroyOperationRunner _destroy;
	private readonly RecoverOperationRunner _recover;
	private readonly FollowOperationRunner _follow;
	private readonly PublicationRunner _publication;

	/// <param name="store">The workspace document store that owns document content and disk operations.</param>
	/// <param name="dispatcher">
	/// The dispatcher that runs actions touching a view's members where view access is legal, such as the dispatcher of the UI thread that owns
	/// the views.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="dispatcher"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// <para>
	/// The coordinator runs every action that touches a view member through <see cref="IUiDispatcher.InvokeAsync"/>, except the event subscription,
	/// and never blocks while waiting. The returned task must complete only after the action has run, so the coordinator can read the results
	/// the action captured; completion may itself be asynchronous, which supports queue-based dispatchers that cannot block their caller.
	/// </para>
	/// <para>
	/// The dispatcher hosts every view thread-affinity decision: actions may be issued from arbitrary
	/// threads and from concurrent coordinator operations, and the coordinator may invoke the dispatcher again from inside a dispatched
	/// action when a view raises <see cref="IWorkspaceDocumentView.ApplyRequested"/>. A dispatcher whose views are not thread-safe
	/// or reentrancy-safe must serialize or queue the actions itself; the coordinator gives no ordering guarantee between concurrent operations.
	/// One coordinator coordinates one store: a second coordinator over the same store would track only its own views.
	/// </para>
	/// <para>
	/// A view fault is reported through the operation result where the operation reports one and is recorded
	/// as unsynchronized view state otherwise; a fault raised by the dispatcher itself surfaces as a failure of the operation (the open path reports
	/// it as <see cref="WorkspaceViewCoordinatorOpenOutcome.OpenFailed"/>).
	/// </para>
	/// </remarks>
	public WorkspaceViewCoordinator(IWorkspaceDocumentStore store, IUiDispatcher dispatcher)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(dispatcher);

		_store = store;

		// The one lock that orders the stop gate and the view tracker.
		object stateLock = new();
		StopGate<Task> stopGate = new(stateLock);

		// A dispatcher that queues the action to a thread of its own may not flow the execution context, so
		// the action carries the mark that tells a stop requested from inside it from one outside.
		Task dispatchViewActionAsync(Action action) => dispatcher.InvokeAsync(stopGate.Carry(action));

		_views = new ViewTracker(owner: this, syncRoot: stateLock, stopGate, options: _store.Options, onApplyRequested: HandleApplyRequested);

		var deleteLeases = new DeleteLeaseCoordinator(_views, dispatchViewActionAsync);
		var synchronizer = new ViewSynchronizer(_views, dispatchViewActionAsync);

		_attacher = new ViewAttacher(_views, dispatchViewActionAsync, _store);

		_attachedChanges = new AttachedDocumentChangeForwarder(
			syncRoot: stateLock,
			views: _views,
			dispatchViewAction: dispatchViewActionAsync,
			sender: this,
			readHandlers: () => AttachedDocumentChanged,
			post: action => dispatcher.Post(action)
		);

		_operations = new CoordinatorOperationRunner(owner: this, syncRoot: stateLock, stopGate, attachedChanges: _attachedChanges);

		_teardown = new CoordinatorTeardown(
			syncRoot: stateLock,
			stopGate,
			views: _views,
			dispatchViewAction: dispatchViewActionAsync,
			attachedChanges: _attachedChanges
		);

		_destroy = new DestroyOperationRunner(
			store: _store,
			views: _views,
			synchronizer,
			deleteLeases,
			attacher: _attacher,
			dispatchViewAction: dispatchViewActionAsync,
			operations: _operations
		);

		_recover = new RecoverOperationRunner(store: _store, views: _views, synchronizer, operations: _operations);
		_follow = new FollowOperationRunner(store: _store, views: _views, synchronizer, deleteLeases, operations: _operations);

		_publication = new PublicationRunner(
			store: _store,
			views: _views,
			synchronizer,
			dispatchViewAction: dispatchViewActionAsync,
			operations: _operations
		);
	}

	/// <inheritdoc/>
	public event EventHandler<AttachedDocumentChangedEventArgs>? AttachedDocumentChanged;

	/// <inheritdoc/>
	public IWorkspaceDocumentReader Documents => _store;

	/// <inheritdoc/>
	public IReadOnlyList<WorkspaceDocumentSnapshot> AttachedDocuments => _views.GetAttachedDocuments();

	/// <inheritdoc/>
	public async Task<WorkspaceViewCoordinatorOpenResult> OpenAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(options);

		WorkspaceDocumentOpenResult openResult = await _operations
			.RunAsync(() => _store.OpenAsync(filePath, options, cancellationToken))
			.ConfigureAwait(false);

		return WorkspaceViewOutcomeConversion.ToOpenResult(openResult);
	}

	/// <inheritdoc/>
	public Task<WorkspaceViewCoordinatorOpenResult> OpenWithViewAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		IWorkspaceDocumentView view,
		CancellationToken cancellationToken = default
	)
	{
		// Validated before dispatch so a null view or null options throw synchronously instead of surfacing as a
		// faulted task, matching the coordinator's other argument validation.
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(options);

		return _operations.RunAsync(() => _attacher.OpenWithViewAsync(view, () => _store.OpenAsync(filePath, options, cancellationToken)));
	}

	/// <inheritdoc/>
	public void DetachView(IWorkspaceDocumentView view)
	{
		ArgumentNullException.ThrowIfNull(view);
		_attacher.Detach(view);

		// The member is synchronous, so it does not wait for the close of a document whose last view this was.
		_ = _attachedChanges.RaisePendingAsync();
	}

	/// <inheritdoc/>
	public Task<WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentMutationResult>> DiscardAsync(
		WorkspaceDocumentDiscardRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request);
		return _follow.DiscardAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult>> ReplaceAsync(
		WorkspaceDocumentReplaceRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request);
		return _destroy.ReplaceAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult>> RenameAsync(
		WorkspaceDocumentRenameRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request);
		return _follow.RenameAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentSaveAsResult>> SaveAsAsync(
		WorkspaceDocumentSaveAsRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request);
		return _follow.SaveAsAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult>> DeleteAsync(
		WorkspaceDocumentDeleteRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request);
		return _destroy.DeleteAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult>> RenameDirectoryAsync(
		WorkspaceDocumentDirectoryRenameRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request);
		return _follow.RenameDirectoryAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryDeleteResult>> DeleteDirectoryAsync(
		WorkspaceDocumentDirectoryDeleteRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request);
		return _destroy.DeleteDirectoryAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult>> CommitAsync(
		WorkspaceDocumentCommitRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request);
		return _recover.CommitAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<WorkspaceViewCoordinatorPublicationResult> PublishPendingEditsAsync(string filePath, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		return _publication.PublishPendingEditsAsync(filePath, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult>> ReloadAsync(
		WorkspaceDocumentReloadRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request);
		return _recover.ReloadAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentConflictResolutionResult>> ResolveExternalConflictAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request);
		return _recover.ResolveExternalConflictAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	public Task StopAsync() => _teardown.StopAsync();

	/// <summary>Stops the coordinator and releases all registered views.</summary>
	/// <returns>A task that completes when the coordinator has stopped.</returns>
	/// <remarks>
	/// Disposal delegates to <see cref="StopAsync"/>: new operations are rejected, active operations are awaited, and registered views are closed
	/// through the dispatcher. The store the coordinator was constructed with is not disposed; its lifetime belongs to the host. Calling this member
	/// more than once observes the same stop completion, and calling it from inside an active operation throws as <see cref="StopAsync"/> does.
	/// </remarks>
	public ValueTask DisposeAsync() => new(StopAsync());

	// The tracker needs the handler of every registered view before the publication runner exists, because the runner needs the tracker.
	private void HandleApplyRequested(object? sender, ApplyRequestedEventArgs eventArgs) => _publication.HandleApplyRequested(sender, eventArgs);
}
