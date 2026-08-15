using Nickelony.IDEKit.Core.Pathing;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;
using Nickelony.IDEKit.Workspace.Tests;
using System.Collections.Concurrent;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

[TestClass]
public sealed partial class WorkspaceDocumentManagerTests
{
	private static readonly WorkspaceDocumentOpenOptions s_openOptions = new(
		TextEncodingKind.Utf8,
		TestSnapshots.FileFormat);

	private static string[] ViewIdsOf(WorkspaceDocumentViewSynchronizationResult synchronization)
		=> [.. synchronization.Issues.Select(issue => issue.ViewId)];

	private sealed class TestView(ManagerFixture fixture, string viewId = "test-view")
		: IWorkspaceDocumentDeleteGuardView
	{
		private string _text = string.Empty;
		private WorkspaceDocumentKey? _documentKey;
		private EventHandler<WorkspaceDocumentViewApplyRequestedEventArgs>? _applyRequested;
		private int _documentKeyReads;
		private int _refreshCount;
		private bool _hasPendingEdits;

		public event EventHandler<WorkspaceDocumentViewApplyRequestedEventArgs>? ApplyRequested
		{
			add
			{
				if (ThrowOnSubscribe)
					throw new InvalidOperationException("The view cannot subscribe.");

				ApplySubscribeThreadId = Environment.CurrentManagedThreadId;

				// A test parks the add so it can unregister the view between the manager's registration and
				// its subscription, exercising the attach/unregister race.
				SubscribeEntered?.TrySetResult();
				SubscribeGate?.Task.GetAwaiter().GetResult();

				_applyRequested += value;
			}
			remove
			{
				if (ThrowOnUnsubscribe)
					throw new InvalidOperationException("The view cannot unsubscribe.");

				ApplyUnsubscribeThreadId = Environment.CurrentManagedThreadId;
				_applyRequested -= value;
			}
		}

		public string ViewId => ViewIdProvider?.Invoke() ?? viewId;

		/// <summary>
		/// Gets or sets a provider for the view id; the default is the id supplied to the constructor.
		/// </summary>
		public Func<string>? ViewIdProvider { get; set; }

		public string? DocumentId { get; private set; }

		/// <summary>
		/// Gets or sets the number of successful document-key reads after which the getter throws; a
		/// negative value (the default) never throws.
		/// </summary>
		public int ThrowAfterDocumentKeyReads { get; set; } = -1;

		public bool ThrowOnSubscribe { get; set; }

		public bool ThrowOnUnsubscribe { get; set; }

		/// <summary>
		/// Gets or sets a gate the subscription add waits on, so a test can unregister the view between the
		/// manager's registration and its subscription.
		/// </summary>
		public TaskCompletionSource? SubscribeGate { get; set; }

		/// <summary>
		/// Gets or sets a source that completes when the subscription add is entered, before it blocks on
		/// <see cref="SubscribeGate"/>.
		/// </summary>
		public TaskCompletionSource? SubscribeEntered { get; set; }

		public int ApplySubscriberCount => _applyRequested?.GetInvocationList().Length ?? 0;

		/// <summary>
		/// Gets or sets a value indicating whether <see cref="Open"/> reports
		/// <see cref="WorkspaceDocumentViewOpenOutcome.AlreadyOpen"/> when the view already has a
		/// document, matching the interface contract.
		/// </summary>
		public bool ReportAlreadyOpenWhenAttached { get; set; }

		/// <summary>
		/// Gets or sets an action that runs at the end of <see cref="Open"/>, after the view adopted the
		/// snapshot, so tests can change the view's reported state inside the attach itself.
		/// </summary>
		public Action? OnAfterOpen { get; set; }

		/// <summary>
		/// Gets or sets an action that runs at the start of <see cref="Refresh"/>, before the snapshot
		/// is adopted, so tests can re-enter the manager from inside a dispatched view member.
		/// </summary>
		public Action? OnRefresh { get; set; }

		public int? ApplySubscribeThreadId { get; private set; }

		public int? ApplyUnsubscribeThreadId { get; private set; }

		public int? LastOpenThreadId { get; private set; }

		public int? LastRefreshThreadId { get; private set; }

		public int? LastCloseThreadId { get; private set; }

		public WorkspaceDocumentKey? DocumentKey
		{
			get
			{
				if (ThrowAfterDocumentKeyReads >= 0 && _documentKeyReads++ >= ThrowAfterDocumentKeyReads)
					throw new InvalidOperationException("The view cannot report its document key.");

				return DocumentKeyOverride ?? _documentKey;
			}
			private set => _documentKey = value;
		}

		/// <summary>
		/// Gets or sets a document key the view reports instead of its adopted one, so tests can model a
		/// view rebound to another document instance at the same path between capture and sweep.
		/// </summary>
		public WorkspaceDocumentKey? DocumentKeyOverride { get; set; }

		public bool HasPendingEdits
		{
			get => ThrowOnStateProbe
				? throw new InvalidOperationException("The view cannot report its pending-edit state.")
				: _hasPendingEdits;
			set => _hasPendingEdits = value;
		}

		public bool HasConflict { get; set; }

		public bool FailRefresh { get; set; }

		/// <summary>
		/// Gets or sets the refresh outcome returned when <see cref="FailRefresh"/> is not set; a
		/// non-refreshed outcome models a view that keeps its content without throwing.
		/// </summary>
		public WorkspaceDocumentViewRefreshOutcome RefreshOutcome { get; set; } = WorkspaceDocumentViewRefreshOutcome.Refreshed;

		public bool FailIdentityAck { get; set; }

		public bool BlockDeleteGuard { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether the view throws while a delete guard is being
		/// entered, so tests can pin that a throwing entry is reported with the guard failure code.
		/// </summary>
		public bool ThrowOnDeleteGuardEntry { get; set; }

		public bool FailDeleteGuardRelease { get; set; }

		public bool ThrowOnDeleteGuardRelease { get; set; }

		/// <summary>
		/// Gets or sets the failure carried by a rejected or failed delete-guard result, so tests can
		/// pin that the manager surfaces the view-provided detail.
		/// </summary>
		public WorkspaceOperationFailure? DeleteGuardFailure { get; set; }

		/// <summary>
		/// Runs when a delete guard is entered, after the manager captured its view set but before the
		/// store delete starts; tests use it to attach competing views inside that window.
		/// </summary>
		public Action? OnDeleteGuardEntered { get; set; }

		public WorkspaceDocumentViewOpenOutcome OpenOutcome { get; set; } = WorkspaceDocumentViewOpenOutcome.Opened;

		public WorkspaceOperationFailure? OpenFailure { get; set; }

		public bool ThrowOnOpen { get; set; }

		public bool ThrowOnClose { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether the view throws while reporting its pending-edit
		/// state, so tests can pin how a throwing state probe blocks an operation.
		/// </summary>
		public bool ThrowOnStateProbe { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether the view throws while acknowledging an applied
		/// mutation, so tests can pin the synthesized acknowledgment failure.
		/// </summary>
		public bool ThrowOnAcknowledgeApply { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether the view throws while acknowledging an identity
		/// change, so tests can pin the synthesized identity-update failure.
		/// </summary>
		public bool ThrowOnIdentityAck { get; set; }

		public int CloseCount { get; private set; }

		public int OpenCount { get; private set; }

		/// <summary>
		/// Gets the number of completed refresh calls. The count is synchronization-safe so tests can
		/// wait for an acknowledgment that runs on another thread.
		/// </summary>
		public int RefreshCount => Volatile.Read(ref _refreshCount);

		public int DeleteGuardReleaseCount { get; private set; }

		public bool ReceivedSnapshotInDispatch { get; private set; }

		public string Text => _text;

		public void SetText(string text) => _text = text;

		public WorkspaceDocumentViewOpenResult Open(WorkspaceDocumentSnapshot snapshot)
		{
			if (ThrowOnOpen)
				throw new InvalidOperationException("The view cannot attach.");

			OpenCount++;
			ReceivedSnapshotInDispatch = fixture.IsInsideDispatch;
			LastOpenThreadId = Environment.CurrentManagedThreadId;

			if (ReportAlreadyOpenWhenAttached && _documentKey is not null)
				return new WorkspaceDocumentViewOpenResult(WorkspaceDocumentViewOpenOutcome.AlreadyOpen, OpenFailure);

			AdoptSnapshot(snapshot);
			OnAfterOpen?.Invoke();
			return new WorkspaceDocumentViewOpenResult(OpenOutcome, OpenFailure);
		}

		public WorkspaceDocumentViewRefreshResult Refresh(WorkspaceDocumentSnapshot snapshot)
		{
			if (FailRefresh)
				throw new InvalidOperationException("The view cannot refresh the document.");

			LastRefreshThreadId = Environment.CurrentManagedThreadId;
			OnRefresh?.Invoke();
			if (RefreshOutcome != WorkspaceDocumentViewRefreshOutcome.Refreshed)
			{
				// A view that cannot apply the snapshot reports a non-refreshed outcome without throwing;
				// it keeps its current content.
				Interlocked.Increment(ref _refreshCount);
				return new WorkspaceDocumentViewRefreshResult(
					RefreshOutcome,
					new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.ViewRefreshFailed, "The view kept its current content."));
			}

			// The snapshot is adopted before the count is published, so a test that waits for the count
			// observes the applied content as well.
			AdoptSnapshot(snapshot);
			Interlocked.Increment(ref _refreshCount);
			return new WorkspaceDocumentViewRefreshResult(WorkspaceDocumentViewRefreshOutcome.Refreshed);
		}

		public WorkspaceDocumentViewIdentityResult AcknowledgeIdentity(WorkspaceDocumentViewIdentityChange change)
		{
			if (ThrowOnIdentityAck)
				throw new InvalidOperationException("The view cannot acknowledge the identity change.");

			if (FailIdentityAck)
				return new WorkspaceDocumentViewIdentityResult(
					WorkspaceDocumentViewIdentityOutcome.Failed,
					new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.ViewIdentityUpdateFailed, "The view rejected the identity change."));

			AdoptSnapshot(change.Snapshot);
			return new WorkspaceDocumentViewIdentityResult(WorkspaceDocumentViewIdentityOutcome.Updated);
		}

		public WorkspaceDocumentViewDeleteGuardResult ApplyDeleteGuard()
		{
			OnDeleteGuardEntered?.Invoke();

			if (ThrowOnDeleteGuardEntry)
				throw new InvalidOperationException("Guard entry failed.");

			return BlockDeleteGuard
				? new(WorkspaceDocumentViewDeleteGuardOutcome.Failed, DeleteGuardFailure)
				: new(WorkspaceDocumentViewDeleteGuardOutcome.Succeeded);
		}

		public WorkspaceDocumentViewDeleteGuardResult ReleaseDeleteGuard()
		{
			DeleteGuardReleaseCount++;
			if (ThrowOnDeleteGuardRelease)
				throw new InvalidOperationException("Guard release failed.");

			return FailDeleteGuardRelease
				? new(WorkspaceDocumentViewDeleteGuardOutcome.Failed, DeleteGuardFailure)
				: new(WorkspaceDocumentViewDeleteGuardOutcome.Succeeded);
		}

		public WorkspaceDocumentViewApplyResult AcknowledgeApply(WorkspaceDocumentMutationResult result)
		{
			if (ThrowOnAcknowledgeApply)
				throw new InvalidOperationException("The view cannot acknowledge the applied mutation.");

			if (result.Snapshot is null)
				return new WorkspaceDocumentViewApplyResult(WorkspaceDocumentViewApplyOutcome.Applied);

			WorkspaceDocumentViewRefreshResult refresh = Refresh(result.Snapshot);

			if (refresh.Outcome != WorkspaceDocumentViewRefreshOutcome.Refreshed)
				return new WorkspaceDocumentViewApplyResult(WorkspaceDocumentViewApplyOutcome.Failed, refresh.Failure);

			// The acknowledgment of an applied mutation is the view's own published content becoming
			// the document state, so successfully applied acknowledgments clear the pending edits that
			// would otherwise keep blocking disk operations.
			HasPendingEdits = false;

			return new WorkspaceDocumentViewApplyResult(WorkspaceDocumentViewApplyOutcome.Applied);
		}

		public void Close()
		{
			// Closing detaches the view from its document, matching the interface contract.
			CloseCount++;
			if (ThrowOnClose)
				throw new InvalidOperationException("The view cannot close.");

			LastCloseThreadId = Environment.CurrentManagedThreadId;
			DocumentId = null;
			DocumentKey = null;
		}

		public void RaiseApply(WorkspaceDocumentReplaceRequest request)
			=> _applyRequested?.Invoke(this, new WorkspaceDocumentViewApplyRequestedEventArgs(request));

		private void AdoptSnapshot(WorkspaceDocumentSnapshot snapshot)
		{
			DocumentId = snapshot.DocumentId;
			DocumentKey = snapshot.DocumentKey;
			_text = snapshot.Content;
		}
	}

	[TestMethod]
	public async Task AsyncOperations_NullRequest_ThrowSynchronously()
	{
		await using var fixture = new ManagerFixture();
		var operations = new (string Name, Action Invoke)[]
		{
			("RenameAsync", () => _ = fixture.Manager.RenameAsync(null!)),
			("SaveAsAsync", () => _ = fixture.Manager.SaveAsAsync(null!)),
			("DeleteAsync", () => _ = fixture.Manager.DeleteAsync(null!)),
			("RenameDirectoryAsync", () => _ = fixture.Manager.RenameDirectoryAsync(null!)),
			("DeleteDirectoryAsync", () => _ = fixture.Manager.DeleteDirectoryAsync(null!)),
			("CommitAsync", () => _ = fixture.Manager.CommitAsync(null!)),
			("ReloadAsync", () => _ = fixture.Manager.ReloadAsync(null!)),
			("ResolveExternalConflictAsync", () => _ = fixture.Manager.ResolveExternalConflictAsync(null!)),
		};

		// A null request is an argument error and must surface as an immediate exception instead of a
		// faulted task, matching the manager's synchronous members.
		foreach ((string name, Action invoke) in operations)
			Assert.ThrowsExactly<ArgumentNullException>(invoke, $"{name} must throw ArgumentNullException synchronously.");
	}

	[TestMethod]
	public async Task StopAsync_ClosesRemainingViewsWhenAnEarlierViewThrowsOnClose()
	{
		await using var fixture = new ManagerFixture();
		await fixture.OpenViewAsync();
		var secondView = new TestView(fixture, "second-view");
		WorkspaceDocumentManagerOpenResult opened = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			secondView);
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, opened.Outcome);
		fixture.View.ThrowOnClose = true;

		await fixture.Manager.StopAsync();

		// A throwing close must not prevent the remaining views from being released or fault the
		// memoized stop task for later callers.
		Assert.AreEqual(1, secondView.CloseCount);
		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => fixture.Manager.OpenAsync(
			fixture.DocumentPath,
			s_openOptions));
	}

	[TestMethod]
	public async Task StopAsync_WithoutRegisteredViewsDoesNotInvokeTheDispatchDelegate()
	{
		// Releasing the views runs through the dispatch delegate, and with no registration there is
		// nothing to run: a dispatcher that cannot run anything must not fault the stop task.
		await using var fixture = new ManagerFixture(throwingDispatch: true);

		await fixture.Manager.StopAsync();

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => fixture.Manager.OpenAsync(
			fixture.DocumentPath,
			s_openOptions));
	}

	[TestMethod]
	public async Task StopAsync_ViewThatThrowsWhileUnsubscribingStillClosesEveryView()
	{
		await using var fixture = new ManagerFixture();
		await fixture.OpenViewAsync();
		var secondView = new TestView(fixture, "second-view");
		WorkspaceDocumentManagerOpenResult opened = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			secondView);
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, opened.Outcome);
		fixture.View.ThrowOnUnsubscribe = true;

		await fixture.Manager.StopAsync();

		// The unsubscribe of one view failing must not fault the teardown or leave the remaining views
		// attached.
		Assert.AreEqual(1, fixture.View.CloseCount);
		Assert.AreEqual(1, secondView.CloseCount);
	}

	[TestMethod]
	public async Task StopAsync_IsIdempotentClosesViewsAndRejectsNewOperations()
	{
		await using var fixture = new ManagerFixture();
		await fixture.OpenViewAsync();

		Task first = fixture.Manager.StopAsync();
		Task second = fixture.Manager.StopAsync();

		Assert.AreSame(first, second);
		await first;

		Assert.AreEqual(1, fixture.View.CloseCount);
		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => fixture.Manager.OpenAsync(
			fixture.DocumentPath,
			s_openOptions));
	}

	[TestMethod]
	public async Task Documents_ExposesTrackedSnapshotsFromStore()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		IReadOnlyList<WorkspaceDocumentSnapshot> snapshots = fixture.Manager.Documents.GetSnapshotsUnderDirectory(fixture.DirectoryPath);

		Assert.AreEqual(1, snapshots.Count);
		Assert.AreEqual(snapshot.DocumentId, snapshots[0].DocumentId);
		Assert.AreEqual(snapshot.Version, snapshots[0].Version);
	}

	[TestMethod]
	public async Task Constructor_InheritsStorePathComparisonWhenNoOverrideIsSupplied()
	{
		await using var fixture = new ManagerFixture(
			options: new WorkspaceDocumentOptions { PathComparison = LocalPathComparisonPolicy.CaseSensitive });
		WorkspaceDocumentSnapshot first = await fixture.OpenViewAsync();
		string caseVariantPath = Path.Combine(fixture.DirectoryPath, "DOCUMENT.txt");
		var secondView = new TestView(fixture, "second-view");

		WorkspaceDocumentManagerOpenResult second = await fixture.Manager.OpenWithViewAsync(
			caseVariantPath,
			s_openOptions,
			secondView);
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, second.Outcome);
		Assert.AreNotEqual(first.DocumentId, second.Snapshot!.DocumentId);

		// A mutation of the first document must not refresh the case-variant view: the manager uses the
		// case-sensitive policy of the store instead of the operating-system default.
		WorkspaceDocumentManagerMutationResult replaced = await fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(first.DocumentKey, first.DocumentId, first.Version),
			"edited",
			first.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.Outcome);
		Assert.AreEqual(1, fixture.View.RefreshCount);
		Assert.AreEqual(0, secondView.RefreshCount);
		Assert.AreEqual(second.Snapshot.DocumentId, secondView.DocumentId);
	}

	[TestMethod]
	public async Task Rename_IdentitySynchronizationRekeysViewForLaterOperations()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		string destinationPath = Path.Combine(fixture.DirectoryPath, "renamed.txt");

		WorkspaceDocumentManagerRenameResult renamed = await fixture.Manager.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, renamed.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, renamed.ViewSynchronization.Outcome);
		Assert.AreEqual(renamed.Snapshot!.DocumentId, fixture.View.DocumentId);

		// The view is now tracked under the destination id and its new version, so a later mutation
		// must still find it as a peer instead of leaving it bound to the old identity.
		int refreshBefore = fixture.View.RefreshCount;
		WorkspaceDocumentManagerMutationResult replaced = await fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(renamed.Snapshot.DocumentKey, renamed.Snapshot.DocumentId, renamed.Snapshot.Version),
			"after rename",
			renamed.Snapshot.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.Outcome);
		Assert.AreEqual(refreshBefore + 1, fixture.View.RefreshCount);
		Assert.AreEqual("after rename", fixture.View.Text);
	}

	[TestMethod]
	public async Task DirectoryRename_SucceedsAndRekeysSynchronizedView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		using var destination = new TemporaryDirectory();
		string destinationPath = Path.Combine(destination.Path, "moved");

		WorkspaceDocumentManagerDirectoryRenameResult result = await fixture.Manager.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(fixture.DirectoryPath, destinationPath));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		Assert.IsNotNull(result.StoreResult);
		Assert.AreEqual(1, result.StoreResult.Snapshots.Count);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.AreEqual(0, result.ViewSynchronization.Issues.Count);

		string movedDocumentPath = Path.Combine(destinationPath, "document.txt");
		Assert.AreEqual(Path.GetFullPath(movedDocumentPath), result.Snapshots[0].DocumentId);
		Assert.AreEqual(result.Snapshots[0].DocumentId, fixture.View.DocumentId);
		Assert.IsTrue(File.Exists(movedDocumentPath));
		Assert.IsFalse(File.Exists(fixture.DocumentPath));

		// The view is tracked under the moved document's new identity, so a later mutation still
		// reaches it.
		int refreshBefore = fixture.View.RefreshCount;
		WorkspaceDocumentManagerMutationResult replaced = await fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(result.Snapshots[0].DocumentKey, result.Snapshots[0].DocumentId, result.Snapshots[0].Version),
			"after move",
			result.Snapshots[0].FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.Outcome);
		Assert.AreEqual(refreshBefore + 1, fixture.View.RefreshCount);
		Assert.AreEqual("after move", fixture.View.Text);
	}

	[TestMethod]
	public async Task StopAsync_WaitsForActiveOperationToFinish()
	{
		var fileSystem = new BlockingDeleteFileSystem(new LocalWorkspaceFileSystem());
		await using var fixture = new ManagerFixture(fileSystem: fileSystem);
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		Task<WorkspaceDocumentManagerDeleteResult> delete = fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));
		await fileSystem.DeleteEntered.WaitAsync(TimeSpan.FromSeconds(10));

		// The delete holds the active-operation registration, so the stop must wait for it instead of
		// detaching views while the operation is still running.
		Task stop = fixture.Manager.StopAsync();
		Assert.IsFalse(stop.IsCompleted);

		fileSystem.ReleaseDelete();
		WorkspaceDocumentManagerDeleteResult result = await delete;
		await stop;

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, result.Outcome);
		Assert.AreEqual(1, fixture.View.CloseCount);
	}

	// Holds file deletes until released so tests can keep a delete operation in flight while they
	// inspect the manager; the decorator base forwards every other member to the real file system.
	private sealed class BlockingDeleteFileSystem(LocalWorkspaceFileSystem inner) : WorkspaceFileSystemDecorator(inner)
	{
		private readonly TaskCompletionSource _deleteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _deleteEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task DeleteEntered => _deleteEntered.Task;

		public void ReleaseDelete() => _deleteGate.TrySetResult();

		public override async Task<WorkspaceFileDeleteResult> DeleteAsync(
			string path,
			FileStamp expectedStamp,
			CancellationToken cancellationToken)
		{
			_deleteEntered.TrySetResult();
			await _deleteGate.Task.ConfigureAwait(false);
			return await Inner.DeleteAsync(path, expectedStamp, cancellationToken).ConfigureAwait(false);
		}
	}

	// Owns a temporary directory with one document file, the store, and a manager whose dispatch
	// shapes model the delegate contract: inline, deferred completion, throwing, and a dedicated
	// single-threaded pump that runs actions on its own thread.
	private sealed class ManagerFixture : IAsyncDisposable
	{
		private readonly TemporaryDirectory _tempDirectory;
		private readonly BlockingCollection<(Action Action, TaskCompletionSource Completion)>? _pumpQueue;
		private readonly Thread? _pumpThread;
		private int _dispatchDepth;

		public ManagerFixture(
			IWorkspaceFileSystem? fileSystem = null,
			WorkspaceDocumentOptions? options = null,
			bool deferredDispatch = false,
			bool throwingDispatch = false,
			bool pumpDispatch = false,
			Func<Action, Task>? customDispatch = null)
		{
			_tempDirectory = new TemporaryDirectory();
			DirectoryPath = _tempDirectory.Path;
			DocumentPath = Path.Combine(DirectoryPath, "document.txt");
			File.WriteAllText(DocumentPath, "initial");
			Store = new WorkspaceDocumentStore(fileSystem ?? new LocalWorkspaceFileSystem(options), options);

			Func<Action, Task> dispatch;
			if (customDispatch is not null)
				dispatch = customDispatch;
			else if (throwingDispatch)
				dispatch = RunThrowing;
			else if (deferredDispatch)
				dispatch = RunDeferred;
			else if (pumpDispatch)
			{
				_pumpQueue = new BlockingCollection<(Action, TaskCompletionSource)>();
				_pumpThread = new Thread(RunPump)
				{
					IsBackground = true,
					Name = "views-test-pump",
				};
				_pumpThread.Start();
				dispatch = RunOnPump;
			}
			else
				dispatch = RunInline;

			Manager = new WorkspaceDocumentManager(Store, dispatch);
			View = new TestView(this);
		}

		public string DirectoryPath { get; }

		public string DocumentPath { get; }

		public int PumpThreadId { get; private set; }

		public WorkspaceDocumentStore Store { get; }

		public WorkspaceDocumentManager Manager { get; }

		public TestView View { get; }

		/// <summary>
		/// Gets or sets a value indicating whether the fixture's inline dispatcher faults before running
		/// an action, so tests can pin how the manager reports a delegate that violates the contract.
		/// </summary>
		public bool FailDispatch { get; set; }

		public bool IsInsideDispatch => _dispatchDepth > 0;

		public async Task<WorkspaceDocumentSnapshot> OpenViewAsync()
		{
			WorkspaceDocumentManagerOpenResult result = await Manager.OpenWithViewAsync(
				DocumentPath,
				s_openOptions,
				View);

			Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, result.Outcome);
			return result.Snapshot!;
		}

		// Discard acknowledges the views of a clean document, which returns NoChange without
		// touching the file. The failing refresh records the view as unsynchronized.
		public async Task MarkViewUnsynchronizedAsync(WorkspaceDocumentSnapshot snapshot)
		{
			View.FailRefresh = true;
			WorkspaceDocumentManagerMutationResult discard = await Manager.DiscardAsync(
				new WorkspaceDocumentDiscardRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)));
			View.FailRefresh = false;

			Assert.AreEqual(WorkspaceDocumentMutationOutcome.NoChange, discard.Outcome);
		}

		/// <summary>
		/// Starts an open of a competing view from the fixture view's delete-guard callback, which is the
		/// window between the manager's view capture and the store operation. The returned task completes
		/// when the attach finished; an operation that reached its store call always triggered it.
		/// </summary>
		public Task<WorkspaceDocumentManagerOpenResult> AttachViewDuringDeleteGuard(TestView view)
		{
			TaskCompletionSource<WorkspaceDocumentManagerOpenResult> attach = new(TaskCreationOptions.RunContinuationsAsynchronously);
			View.OnDeleteGuardEntered = () =>
			{
				if (!attach.Task.IsCompleted)
					_ = AttachAsync();
			};

			return attach.Task;

			async Task AttachAsync()
			{
				try
				{
					attach.TrySetResult(await Manager.OpenWithViewAsync(DocumentPath, s_openOptions, view));
				}
				catch (Exception exception)
				{
					attach.TrySetException(exception);
				}
			}
		}

		public async ValueTask DisposeAsync()
		{
			try
			{
				await Manager.DisposeAsync();
			}
			catch (InvalidOperationException)
			{
				// A test may deliberately fault the dispatcher during teardown; the manager stays stopped
				// and the fixture still releases the store and the temporary directory.
			}

			await Store.DisposeAsync();

			if (_pumpQueue is not null)
			{
				_pumpQueue.CompleteAdding();
				_pumpThread!.Join();
				_pumpQueue.Dispose();
			}

			_tempDirectory.Dispose();
		}

		private Task RunInline(Action action)
		{
			ThrowIfDispatchFails();
			_dispatchDepth++;
			try
			{
				action();
			}
			finally
			{
				_dispatchDepth--;
			}

			return Task.CompletedTask;
		}

		// Completes asynchronously after running the action, which is the dispatcher shape the
		// constructor contract allows in addition to the inline one.
		private async Task RunDeferred(Action action)
		{
			ThrowIfDispatchFails();
			_dispatchDepth++;
			try
			{
				await Task.Yield();
				action();
			}
			finally
			{
				_dispatchDepth--;
			}
		}

		// Violates the delegate contract by faulting before the action runs; tests pin how the
		// manager reports that fault.
		private Task RunThrowing(Action action)
		{
			throw new InvalidOperationException("The dispatch delegate cannot run the action.");
		}

		// Runs the action on the fixture's dedicated pump thread; the returned task completes after the
		// action ran, which is the shape a real single-threaded UI host provides.
		private Task RunOnPump(Action action)
		{
			TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
			_pumpQueue!.Add((action, completion));
			return completion.Task;
		}

		private void RunPump()
		{
			PumpThreadId = Environment.CurrentManagedThreadId;

			foreach ((Action action, TaskCompletionSource completion) in _pumpQueue!.GetConsumingEnumerable())
			{
				_dispatchDepth++;
				try
				{
					action();
				}
				finally
				{
					_dispatchDepth--;
					completion.TrySetResult();
				}
			}
		}

		private void ThrowIfDispatchFails()
		{
			if (FailDispatch)
				throw new InvalidOperationException("The dispatch delegate cannot run the action.");
		}
	}
}
