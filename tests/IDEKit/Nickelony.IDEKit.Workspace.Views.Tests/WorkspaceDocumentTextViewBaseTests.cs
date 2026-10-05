using System.Collections.Concurrent;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

[TestClass]
public sealed partial class WorkspaceDocumentTextViewBaseTests
{
	private const string DocumentId = "view-test.lua";

	private static readonly WorkspaceDocumentOpenOptions s_openOptions = new([TextEncoding.Utf8], TestSnapshots.FileFormat);

	public required TestContext TestContext { get; set; }

	private static WorkspaceDocumentSnapshot Snapshot(string text, long version = 0, WorkspaceDocumentKey? key = null) =>
		TestSnapshots.Create(DocumentId, content: text, version, documentKey: key);

	private static WorkspaceDocumentMutationResult Mutation(
		WorkspaceDocumentMutationOutcome outcome,
		WorkspaceDocumentSnapshot requested,
		WorkspaceDocumentSnapshot? current
	) => new(outcome, requested.Identity, current);

	private static WorkspaceDocumentMutationResult Acknowledgment(
		WorkspaceDocumentReplaceRequest request,
		WorkspaceDocumentMutationOutcome outcome,
		WorkspaceDocumentSnapshot? current
	) => new(outcome, request.Identity, current);

	private static List<WorkspaceDocumentReplaceRequest> CapturePublications(TestTextView view)
	{
		List<WorkspaceDocumentReplaceRequest> published = [];
		view.ApplyRequested += (_, e) => published.Add(e.Request);
		return published;
	}

	[TestMethod]
	public async Task Open_ThroughTheCoordinator_ShowsTheDocumentTextWithoutPendingEdits()
	{
		await using var host = new CoordinatorTestHost();
		var view = new TestTextView();

		WorkspaceViewCoordinatorOpenResult opened = await host.Coordinator.OpenWithViewAsync(host.DocumentPath, s_openOptions, view);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, opened.Outcome);
		Assert.AreEqual("initial", view.Text);
		Assert.AreEqual(opened.Snapshot?.DocumentKey, view.DocumentKey);
		Assert.AreEqual(opened.Snapshot?.DocumentId, view.DocumentId);
		Assert.IsFalse(view.HasPendingEdits);
		Assert.IsFalse(view.HasConflict);
		Assert.AreEqual(WorkspaceDocumentViewOpenOutcome.AlreadyOpen, view.Open(Snapshot("other")).Outcome);
		Assert.AreEqual("initial", view.Text);
	}

	[TestMethod]
	public void Refresh_OfAViewWithoutPendingEdits_AdoptsTheSnapshotText()
	{
		var view = new TestTextView();
		view.Open(Snapshot("print(1)"));

		WorkspaceDocumentViewRefreshResult refreshed = view.Refresh(Snapshot("print(2)", version: 1));

		Assert.AreEqual(WorkspaceDocumentViewRefreshOutcome.Refreshed, refreshed.Outcome);
		Assert.AreEqual("print(2)", view.Text);
		Assert.AreEqual("print(2)", view.SynchronizedText);
	}

	[TestMethod]
	public void Refresh_OfAViewWithPendingEdits_MarksItStaleAndKeepsTheTypedText()
	{
		var view = new TestTextView();
		view.Open(Snapshot("print(1)"));

		view.Type("print(2)");

		Assert.IsTrue(view.HasPendingEdits);
		Assert.AreEqual(WorkspaceDocumentViewRefreshOutcome.MarkedStale, view.Refresh(Snapshot("disk")).Outcome);
		Assert.AreEqual("print(2)", view.Text);
		Assert.AreEqual("print(1)", view.SynchronizedText);
	}

	[TestMethod]
	public void Refresh_WithASnapshotOlderThanTheOneHeld_IsIgnored()
	{
		var view = new TestTextView();
		WorkspaceDocumentSnapshot opened = Snapshot("print(1)", version: 2);
		view.Open(opened);

		WorkspaceDocumentViewRefreshResult refreshed = view.Refresh(Snapshot("print(0)", version: 1, key: opened.DocumentKey));

		Assert.AreEqual(WorkspaceDocumentViewRefreshOutcome.Refreshed, refreshed.Outcome);
		Assert.AreEqual("print(1)", view.Text);
		Assert.AreEqual("print(1)", view.SynchronizedText);
	}

	[TestMethod]
	public void AcknowledgeApply_OfAPublicationThatAnOverlappingChangeOvertook_ReportsAConflictAndKeepsTheTypedTextUnpublished()
	{
		var view = new TestTextView();
		WorkspaceDocumentSnapshot opened = Snapshot("print(1)");
		view.Open(opened);

		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);
		int conflictChanges = 0;

		view.ConflictChanged += (_, _) => conflictChanges++;
		view.Type("print(typed)");
		view.PublishPendingEdits();

		WorkspaceDocumentSnapshot current = Snapshot("print(disk)", version: 3, key: opened.DocumentKey);

		WorkspaceDocumentViewApplyResult acknowledged = view.AcknowledgeApply(
			Acknowledgment(published[0], WorkspaceDocumentMutationOutcome.StaleDocument, current)
		);

		// The view acknowledged the newer snapshot, so the coordinator records no failure, and the conflict blocks by its state.
		Assert.AreEqual(WorkspaceDocumentViewApplyOutcome.Applied, acknowledged.Outcome);
		Assert.IsNull(acknowledged.Failure);
		Assert.IsTrue(view.HasConflict);
		Assert.AreEqual(1, conflictChanges);
		Assert.IsTrue(view.HasPendingEdits);
		Assert.AreEqual("print(typed)", view.Text);
		Assert.AreEqual("print(disk)", view.SynchronizedText);

		// The conflict holds the typed text back until the host resolves it.
		view.Type("print(typed!)");
		view.PublishPendingEdits();

		Assert.HasCount(1, published);

		view.KeepViewText();

		Assert.IsFalse(view.HasConflict);
		Assert.AreEqual(2, conflictChanges);
		Assert.HasCount(2, published);
		Assert.AreEqual(3, published[1].Identity.Version);
		Assert.AreEqual("print(typed!)", published[1].Content);
	}

	[TestMethod]
	public void AcknowledgeApply_OfADiscardThatIsNotThisViewsPublication_ReplacesTheEditorText()
	{
		var view = new TestTextView();
		WorkspaceDocumentSnapshot opened = Snapshot("print(1)");
		view.Open(opened);
		view.Type("print(typed)");

		WorkspaceDocumentSnapshot restored = Snapshot("print(1)", version: 1, key: opened.DocumentKey);
		WorkspaceDocumentViewApplyResult acknowledged = view.AcknowledgeApply(Mutation(WorkspaceDocumentMutationOutcome.Changed, opened, restored));

		Assert.AreEqual(WorkspaceDocumentViewApplyOutcome.Applied, acknowledged.Outcome);
		Assert.AreEqual("print(1)", view.Text);
		Assert.IsFalse(view.HasPendingEdits);
	}

	[TestMethod]
	public async Task RenameDirectory_ThroughTheCoordinator_MovesTheViewAndRaisesDocumentRenamed()
	{
		await using var host = new CoordinatorTestHost();
		var view = new TestTextView();

		await host.Coordinator.OpenWithViewAsync(host.DocumentPath, s_openOptions, view);
		using var destination = new TemporaryDirectory();
		string destinationPath = Path.Combine(destination.Path, "moved");

		int renamed = 0;
		view.DocumentRenamed += (_, _) => renamed++;

		WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult> result = await host.Coordinator.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(host.DirectoryPath, destinationPath),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.AreEqual(result.StoreResult?.Snapshots[0].DocumentId, view.DocumentId);
		Assert.AreEqual(1, renamed);

		// The move leased the view, and its release gave the editor back its typing.
		Assert.IsFalse(view.ReadOnly);
		Assert.HasCount(2, view.ReadOnlyWrites);
	}

	[TestMethod]
	public void AcknowledgeIdentity_OfAViewWithPendingEdits_KeepsTheTypedTextPendingAgainstTheRenamedDocument()
	{
		var view = new TestTextView();
		WorkspaceDocumentSnapshot opened = Snapshot("print(1)");
		view.Open(opened);
		view.Type("print(typed)");

		WorkspaceDocumentSnapshot renamed = TestSnapshots.Create(
			documentId: "renamed.lua",
			content: "print(1)",
			version: 1,
			documentKey: opened.DocumentKey
		);

		var change = new WorkspaceDocumentViewIdentityChange(opened.DocumentKey, opened.DocumentId, renamed);

		Assert.AreEqual(WorkspaceDocumentViewIdentityOutcome.Updated, view.AcknowledgeIdentity(change).Outcome);
		Assert.AreEqual("renamed.lua", view.DocumentId);
		Assert.AreEqual("print(typed)", view.Text);
		Assert.IsTrue(view.HasPendingEdits);
	}

	[TestMethod]
	public void AcknowledgeIdentity_ThatKeepsTheDocumentId_AdoptsTheSnapshotWithoutRaisingDocumentRenamed()
	{
		var view = new TestTextView();
		WorkspaceDocumentSnapshot opened = Snapshot("print(1)");
		view.Open(opened);

		int renamed = 0;
		view.DocumentRenamed += (_, _) => renamed++;

		var change = new WorkspaceDocumentViewIdentityChange(opened.DocumentKey, opened.DocumentId, Snapshot("print(2)", 1, opened.DocumentKey));

		Assert.AreEqual(WorkspaceDocumentViewIdentityOutcome.Updated, view.AcknowledgeIdentity(change).Outcome);
		Assert.AreEqual(opened.DocumentKey, view.DocumentKey);
		Assert.AreEqual("print(2)", view.SynchronizedText);
		Assert.AreEqual("print(2)", view.Text);
		Assert.AreEqual(0, renamed);
	}

	[TestMethod]
	public void DirtyChanged_IsRaisedOnlyWhenIsDirtyChanges()
	{
		var view = new TestTextView();
		WorkspaceDocumentSnapshot opened = Snapshot("print(1)");
		view.Open(opened);
		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);

		var observed = new List<bool>();
		view.DirtyChanged += (_, _) => observed.Add(view.IsDirty);

		view.Type("print(2)");
		view.Type("print(3)");
		view.Type("print(1)");

		// The store holds the published text unsaved, so the view stays dirty once its pending edits are acknowledged.
		view.Type("print(4)");
		view.PublishPendingEdits();

		view.AcknowledgeApply(
			Acknowledgment(
				published[0],
				WorkspaceDocumentMutationOutcome.Changed,

				Snapshot("print(4)", 1, opened.DocumentKey) with
				{
					IsDirty = true,
				}
			)
		);

		Assert.IsFalse(view.HasPendingEdits);
		Assert.IsTrue(view.IsDirty);

		view.Close();

		Assert.AreSequenceEqual([true, false, true, false], observed);
		Assert.IsFalse(view.IsDirty);
	}

	[TestMethod]
	public async Task Commit_ThroughTheCoordinator_ClearsTheDirtyStateOfEveryCurrentView()
	{
		await using var host = new CoordinatorTestHost();
		var editing = new TestTextView("editing");
		var other = new TestTextView("other");

		await host.Coordinator.OpenWithViewAsync(host.DocumentPath, s_openOptions, editing);
		await host.Coordinator.OpenWithViewAsync(host.DocumentPath, s_openOptions, other);

		editing.Type("saved");
		await host.Coordinator.PublishPendingEditsAsync(editing.DocumentId!, TestContext.CancellationToken);

		Assert.IsTrue(editing.IsDirty);
		Assert.IsTrue(other.IsDirty);
		Assert.IsTrue(host.Store.TryGetSnapshot(editing.DocumentId!, out WorkspaceDocumentSnapshot? published));

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult> committed = await host.Coordinator.CommitAsync(
			new WorkspaceDocumentCommitRequest(published.Identity, published.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.StoreResult?.Outcome);
		Assert.AreEqual(published.Version, committed.StoreResult?.Snapshot?.Version);
		Assert.IsFalse(editing.IsDirty);
		Assert.IsFalse(other.IsDirty);
	}

	[TestMethod]
	public async Task ResolveExternalConflictWithLogical_ThroughTheCoordinator_ClearsTheDirtyStateOfEveryCurrentView()
	{
		await using var host = new CoordinatorTestHost();
		var editing = new TestTextView("editing");
		var other = new TestTextView("other");

		await host.Coordinator.OpenWithViewAsync(host.DocumentPath, s_openOptions, editing);
		await host.Coordinator.OpenWithViewAsync(host.DocumentPath, s_openOptions, other);

		editing.Type("logical");
		await host.Coordinator.PublishPendingEditsAsync(editing.DocumentId!, TestContext.CancellationToken);
		Assert.IsTrue(host.Store.TryGetSnapshot(editing.DocumentId!, out WorkspaceDocumentSnapshot? dirty));

		File.WriteAllText(host.DocumentPath, "external");

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult> conflict = await host.Coordinator.ReloadAsync(
			new WorkspaceDocumentReloadRequest(dirty.Identity),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, conflict.StoreResult?.Outcome);

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentConflictResolutionResult> resolved =
			await host.Coordinator.ResolveExternalConflictAsync(
				new WorkspaceDocumentConflictResolutionRequest(
					dirty.Identity,
					conflict.StoreResult!.ObservedOnDiskStamp!.Value,
					WorkspaceDocumentConflictResolutionMode.ResolveWithLogical
				),

				TestContext.CancellationToken
			);

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical, resolved.StoreResult?.Outcome);
		Assert.IsFalse(editing.IsDirty);
		Assert.IsFalse(other.IsDirty);
	}

	[TestMethod]
	public void AClosedView_RejectsTheCoordinatorCallsThatWouldReopenIt()
	{
		var view = new TestTextView();
		WorkspaceDocumentSnapshot opened = Snapshot("print(1)");
		view.Open(opened);
		view.Close();

		WorkspaceDocumentSnapshot later = Snapshot("print(2)", version: 1, key: opened.DocumentKey);

		WorkspaceDocumentViewRefreshResult refreshed = view.Refresh(later);
		WorkspaceDocumentViewApplyResult acknowledged = view.AcknowledgeApply(Mutation(WorkspaceDocumentMutationOutcome.Changed, opened, later));

		WorkspaceDocumentViewIdentityResult identity = view.AcknowledgeIdentity(
			new WorkspaceDocumentViewIdentityChange(opened.DocumentKey, opened.DocumentId, later)
		);

		Assert.AreEqual(WorkspaceDocumentViewRefreshOutcome.Failed, refreshed.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewRefreshFailed, refreshed.Failure?.Code);
		Assert.AreEqual(WorkspaceDocumentViewApplyOutcome.Failed, acknowledged.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewAcknowledgeFailed, acknowledged.Failure?.Code);
		Assert.AreEqual(WorkspaceDocumentViewIdentityOutcome.Failed, identity.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewIdentityUpdateFailed, identity.Failure?.Code);
		Assert.IsNull(view.DocumentKey);
		Assert.AreEqual("print(1)", view.Text);
	}

	[TestMethod]
	public void Dispose_StopsTheDebouncedPublication_AndTheCoordinatorCallsLeaveTheEditorAlone()
	{
		var dispatcher = new FakeUiDispatcher();
		var view = new TestTextView(dispatcher: dispatcher, publishDelay: TimeSpan.FromSeconds(1));

		WorkspaceDocumentSnapshot opened = Snapshot("print(1)");
		view.Open(opened);
		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);
		view.Type("print(2)");

		Assert.AreEqual(1, dispatcher.PendingCount);

		view.Dispose();

		Assert.AreEqual(0, dispatcher.PendingCount);

		int dirtyChanges = 0;
		view.DirtyChanged += (_, _) => dirtyChanges++;
		view.Type("print(1)");
		view.PublishPendingEdits();

		WorkspaceDocumentSnapshot later = Snapshot("print(3)", version: 1, key: opened.DocumentKey);

		Assert.AreEqual(WorkspaceDocumentViewRefreshOutcome.Failed, view.Refresh(later).Outcome);

		Assert.AreEqual(
			WorkspaceDocumentViewApplyOutcome.Failed,
			view.AcknowledgeApply(Mutation(WorkspaceDocumentMutationOutcome.Changed, opened, later)).Outcome
		);

		Assert.AreEqual(
			WorkspaceDocumentViewIdentityOutcome.Failed,
			view.AcknowledgeIdentity(new WorkspaceDocumentViewIdentityChange(opened.DocumentKey, opened.DocumentId, later)).Outcome
		);

		Assert.AreEqual(WorkspaceDocumentViewDeleteLeaseOutcome.Failed, view.EnterDeleteLease().Outcome);
		Assert.AreEqual(WorkspaceDocumentViewDeleteLeaseOutcome.Failed, view.ReleaseDeleteLease().Outcome);

		Assert.AreEqual(0, dirtyChanges);
		Assert.IsEmpty(published);
		Assert.IsEmpty(view.ReadOnlyWrites);
		Assert.AreEqual("print(1)", view.Text);
		Assert.ThrowsExactly<ObjectDisposedException>(() => new TestTextView().Disposed().Open(Snapshot("print(1)")));
	}

	[TestMethod]
	public void Constructor_RejectsANegativePublishDelay()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			new TestTextView(dispatcher: new FakeUiDispatcher(), publishDelay: TimeSpan.FromMilliseconds(-1))
		);
	}

	// An editor double: its text, its read-only flag, and the failures a test asks its host members for.
	private sealed class TestTextView(string viewId = "text-view", IUiDispatcher? dispatcher = null, TimeSpan? publishDelay = null)
		: WorkspaceDocumentTextViewBase(viewId, dispatcher ?? new FakeUiDispatcher(), publishDelay)
	{
		private int _readOnlyWriteAttempts;

		public string Text { get; private set; } = string.Empty;

		public bool ReadOnly { get; private set; }

		public List<bool> ReadOnlyWrites { get; } = [];

		public bool ThrowOnReplace { get; set; }

		/// <summary>Whether the replacement throws after the editor already shows the new text.</summary>
		public bool ThrowAfterReplace { get; set; }

		public bool ThrowOnReadOnly { get; set; }

		/// <summary>The one-based number of the read-only write that throws, or zero for none.</summary>
		public int ThrowOnReadOnlyWrite { get; set; }

		protected override bool IsReadOnly
		{
			get => ReadOnly;
			set
			{
				if (ThrowOnReadOnly || ++_readOnlyWriteAttempts == ThrowOnReadOnlyWrite)
					throw new InvalidOperationException("The editor cannot change its read-only state.");

				ReadOnlyWrites.Add(value);
				ReadOnly = value;
			}
		}

		public void Type(string text)
		{
			Text = text;
			HandleTextChanged();
		}

		// A keystroke that a read-only editor refuses; returns whether the editor took it.
		public bool TypeUnlessReadOnly(string text)
		{
			if (ReadOnly)
				return false;

			Type(text);
			return true;
		}

		public void MakeReadOnly() => ReadOnly = true;

		public TestTextView Disposed()
		{
			Dispose();
			return this;
		}

		protected override string ReadText() => Text;

		// An editor raises its change event inside the replacement, which the view must ignore.
		protected override void ReplaceText(string text)
		{
			if (ThrowOnReplace)
				throw new InvalidOperationException("The editor cannot replace its text.");

			Text = text;
			HandleTextChanged();

			if (ThrowAfterReplace)
				throw new InvalidOperationException("The editor failed after it replaced its text.");
		}
	}

	// Queues every dispatched action until the test runs the queue, as a message loop that answers later does. A queue that runs the newest
	// action first stands for a dispatcher that does not keep the order of its actions.
	private sealed class QueuedDispatch(bool runsNewestFirst = false)
	{
		private readonly ConcurrentQueue<(Action Action, TaskCompletionSource Completion)> _queue = new();
		private readonly ConcurrentStack<(Action Action, TaskCompletionSource Completion)> _stack = new();

		public int Count => _queue.Count + _stack.Count;

		public Task Dispatch(Action action)
		{
			var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			if (runsNewestFirst)
				_stack.Push((action, completion));
			else
				_queue.Enqueue((action, completion));

			return completion.Task;
		}

		// Runs one action, so the continuations it releases run before the next one, as they do between the messages of a loop.
		public bool RunOne()
		{
			if (!(runsNewestFirst ? _stack.TryPop(out (Action Action, TaskCompletionSource Completion) entry) : _queue.TryDequeue(out entry)))
				return false;

			try
			{
				entry.Action();
				entry.Completion.SetResult();
			}
			catch (Exception exception)
			{
				entry.Completion.SetException(exception);
			}

			return true;
		}

		public async Task<T> RunUntilAsync<T>(Task<T> task, CancellationToken cancellationToken)
		{
			await RunUntilAsync((Task)task, cancellationToken);
			return await task;
		}

		public async Task RunUntilAsync(Task task, CancellationToken cancellationToken)
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(TimeSpan.FromSeconds(10));

			while (!task.IsCompleted)
			{
				RunOne();
				await Task.Delay(5, timeout.Token);
			}
		}
	}

	// A store over a temporary directory with one document, and a coordinator over a dispatcher that runs each action inline unless the test
	// supplies a queue. The disposal runs the queue while the coordinator stops, so a test that fails early does not hang.
	private sealed class CoordinatorTestHost : IAsyncDisposable
	{
		private readonly TemporaryDirectory _directory = new();
		private readonly QueuedDispatch? _queue;

		public CoordinatorTestHost(QueuedDispatch? queue = null)
		{
			_queue = queue;
			DirectoryPath = _directory.Path;
			DocumentPath = Path.Combine(DirectoryPath, "document.txt");
			File.WriteAllText(DocumentPath, "initial");
			Store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

			Coordinator = new WorkspaceViewCoordinator(Store, queue is null ? new FakeUiDispatcher() : new ScriptedUiDispatcher(queue.Dispatch));
		}

		public string DirectoryPath { get; }

		public string DocumentPath { get; }

		public WorkspaceDocumentStore Store { get; }

		public WorkspaceViewCoordinator Coordinator { get; }

		public async ValueTask DisposeAsync()
		{
			if (_queue is not null)
				await _queue.RunUntilAsync(Coordinator.StopAsync(), CancellationToken.None);

			await Coordinator.DisposeAsync();
			await Store.DisposeAsync();

			_directory.Dispose();
		}
	}
}
