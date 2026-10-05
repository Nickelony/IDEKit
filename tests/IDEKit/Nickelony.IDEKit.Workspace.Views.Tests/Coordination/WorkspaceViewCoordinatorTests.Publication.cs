using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Reloading;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceViewCoordinatorTests
{
	[TestMethod]
	public async Task PublishPendingEditsAsync_PublishesTheHeldBackText_AndCompletesOnceTheStoreAppliedIt()
	{
		await using var fixture = new CoordinatorTestHost();
		var view = new PublishingView("publisher");

		WorkspaceDocumentSnapshot opened = (await fixture.Coordinator.OpenWithViewAsync(fixture.DocumentPath, s_openOptions, view)).Snapshot!;
		view.Type("edited");

		WorkspaceViewCoordinatorPublicationResult published = await fixture.Coordinator.PublishPendingEditsAsync(
			opened.DocumentId,
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, published.ViewSynchronization.Outcome);
		Assert.IsFalse(view.HasPendingEdits);
		Assert.IsTrue(fixture.Store.TryGetSnapshot(opened.DocumentId, out WorkspaceDocumentSnapshot? current));
		Assert.AreEqual("edited", current.Content);
	}

	[TestMethod]
	public async Task PublishPendingEditsAsync_AViewThatCannotPublish_IsReportedAsBlocking()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot opened = await fixture.OpenViewAsync();
		fixture.View.HasPendingEdits = true;

		WorkspaceViewCoordinatorPublicationResult published = await fixture.Coordinator.PublishPendingEditsAsync(
			opened.DocumentId,
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, published.ViewSynchronization.Outcome);
		Assert.AreEqual(fixture.View.ViewId, published.ViewSynchronization.Issues.Single().ViewId);
	}

	[TestMethod]
	public async Task PublishPendingEditsAsync_AViewThatThrows_IsReportedWithItsFailure()
	{
		await using var fixture = new CoordinatorTestHost();
		var view = new PublishingView("publisher") { ThrowOnPublish = true };

		WorkspaceDocumentSnapshot opened = (await fixture.Coordinator.OpenWithViewAsync(fixture.DocumentPath, s_openOptions, view)).Snapshot!;

		WorkspaceViewCoordinatorPublicationResult published = await fixture.Coordinator.PublishPendingEditsAsync(
			opened.DocumentId,
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, published.ViewSynchronization.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewPublishFailed, published.ViewSynchronization.Issues.Single().Failure?.Code);
	}

	[TestMethod]
	public async Task ReloadHooks_PublishTheHeldBackText_SoAChangedFileUnderItIsAConflict_ThatTheDecisionResolves()
	{
		await using var fixture = new CoordinatorTestHost();
		var view = new PublishingView("publisher");

		await fixture.Coordinator.OpenWithViewAsync(fixture.DocumentPath, s_openOptions, view);

		view.Type("typed");
		File.WriteAllText(fixture.DocumentPath, "changed on disk");

		WorkspaceDocumentReloadOutcome? decided = null;

		var reloads = new FileReloadCoordinator<WorkspaceDocumentConflictResolutionMode?>(
			fixture.Store,

			WorkspaceViewReloadHooksFactory.Create(
				fixture.Coordinator,
				fixture.Dispatcher,

				(conflict, _) =>
				{
					decided = conflict.Outcome;
					return Task.FromResult<WorkspaceDocumentConflictResolutionMode?>(WorkspaceDocumentConflictResolutionMode.ResolveWithDisk);
				}
			)
		);

		reloads.QueueFiles([fixture.DocumentPath]);
		await reloads.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		// Without the publication the store would see a clean document and reload over the typed text without asking.
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, decided);
		Assert.AreEqual("changed on disk", view.Text);
		Assert.IsFalse(view.HasPendingEdits);
	}

	[TestMethod]
	public async Task ReloadHooks_ANullDecision_KeepsTheConflictAndReportsIt()
	{
		await using var fixture = new CoordinatorTestHost();
		var view = new PublishingView("publisher");

		await fixture.Coordinator.OpenWithViewAsync(fixture.DocumentPath, s_openOptions, view);

		view.Type("typed");
		File.WriteAllText(fixture.DocumentPath, "changed on disk");

		var reported = new List<WorkspaceDocumentReloadResult>();

		var reloads = new FileReloadCoordinator<WorkspaceDocumentConflictResolutionMode?>(
			fixture.Store,

			WorkspaceViewReloadHooksFactory.Create(
				coordinator: fixture.Coordinator,
				dispatcher: fixture.Dispatcher,
				decideConflict: (_, _) => Task.FromResult<WorkspaceDocumentConflictResolutionMode?>(null),

				reportFailure: (result, _) =>
				{
					reported.Add(result);
					return Task.CompletedTask;
				}
			)
		);

		reloads.QueueFiles([fixture.DocumentPath]);
		await reloads.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, reported.Single().Outcome);
		Assert.AreEqual("typed", view.Text);
	}

	[TestMethod]
	public async Task ReloadHooks_ThePromptOfAPassStartedOffTheDispatcher_StartsOnTheDispatcherThread()
	{
		await using var fixture = new CoordinatorTestHost(pumpDispatch: true);
		var view = new PublishingView("publisher");

		await fixture.Coordinator.OpenWithViewAsync(fixture.DocumentPath, s_openOptions, view);

		view.Type("typed");
		File.WriteAllText(fixture.DocumentPath, "changed on disk");

		int promptThreadId = -1;

		var reloads = new FileReloadCoordinator<WorkspaceDocumentConflictResolutionMode?>(
			fixture.Store,

			WorkspaceViewReloadHooksFactory.Create(
				fixture.Coordinator,
				fixture.Dispatcher,

				(_, _) =>
				{
					promptThreadId = Environment.CurrentManagedThreadId;
					return Task.FromResult<WorkspaceDocumentConflictResolutionMode?>(WorkspaceDocumentConflictResolutionMode.ResolveWithDisk);
				}
			)
		);

		reloads.QueueFiles([fixture.DocumentPath]);
		await reloads.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		// The pass runs on the test thread and on the pool, and the prompt of a host shows a dialog, so it must reach the views' thread.
		Assert.AreNotEqual(-1, promptThreadId);
		Assert.AreEqual(fixture.PumpThreadId, promptThreadId);
		Assert.AreEqual("changed on disk", view.Text);
	}

	[TestMethod]
	public async Task ReloadHooks_TheFailureReportOfACallOffTheDispatcher_StartsOnTheDispatcherThread()
	{
		await using var fixture = new CoordinatorTestHost(pumpDispatch: true);
		int reportThreadId = -1;

		FileReloadHooks<WorkspaceDocumentConflictResolutionMode?> hooks = WorkspaceViewReloadHooksFactory.Create(
			coordinator: fixture.Coordinator,
			dispatcher: fixture.Dispatcher,
			decideConflict: (_, _) => Task.FromResult<WorkspaceDocumentConflictResolutionMode?>(null),

			reportFailure: (_, _) =>
			{
				reportThreadId = Environment.CurrentManagedThreadId;
				return Task.CompletedTask;
			}
		);

		await hooks.ReportReloadFailure!(CreateConflict(fixture), TestContext.CancellationToken);

		Assert.AreNotEqual(Environment.CurrentManagedThreadId, reportThreadId);
		Assert.AreEqual(fixture.PumpThreadId, reportThreadId);
	}

	[TestMethod]
	public async Task ReloadHooks_ADispatcherThatCompletesWithoutRunningTheCall_FaultsTheHook()
	{
		await using var fixture = new CoordinatorTestHost();
		bool decided = false;

		FileReloadHooks<WorkspaceDocumentConflictResolutionMode?> hooks = WorkspaceViewReloadHooksFactory.Create(
			fixture.Coordinator,
			new ScriptedUiDispatcher(_ => Task.CompletedTask),

			(_, _) =>
			{
				decided = true;
				return Task.FromResult<WorkspaceDocumentConflictResolutionMode?>(null);
			}
		);

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => hooks.DecideReload!(CreateConflict(fixture), TestContext.CancellationToken));

		Assert.IsFalse(decided);
	}

	[TestMethod]
	public async Task ReloadHooks_ANullArgument_Throws()
	{
		await using var fixture = new CoordinatorTestHost();

		static Task<WorkspaceDocumentConflictResolutionMode?> decide(WorkspaceDocumentReloadResult _1, CancellationToken _2) =>
			Task.FromResult<WorkspaceDocumentConflictResolutionMode?>(null);

		Assert.ThrowsExactly<ArgumentNullException>(() => WorkspaceViewReloadHooksFactory.Create(null!, fixture.Dispatcher, decide));
		Assert.ThrowsExactly<ArgumentNullException>(() => WorkspaceViewReloadHooksFactory.Create(fixture.Coordinator, null!, decide));
		Assert.ThrowsExactly<ArgumentNullException>(() => WorkspaceViewReloadHooksFactory.Create(fixture.Coordinator, fixture.Dispatcher, null!));
	}

	[TestMethod]
	public async Task PublishPendingEditsAsync_AnotherSpellingOfThePath_ReachesTheViews()
	{
		await using var fixture = new CoordinatorTestHost();
		var view = new PublishingView("publisher");

		WorkspaceDocumentSnapshot opened = (await fixture.Coordinator.OpenWithViewAsync(fixture.DocumentPath, s_openOptions, view)).Snapshot!;
		view.Type("edited");

		string otherSpelling = Path.Combine(fixture.DirectoryPath, ".", Path.GetFileName(fixture.DocumentPath));

		WorkspaceViewCoordinatorPublicationResult published = await fixture.Coordinator.PublishPendingEditsAsync(
			otherSpelling,
			TestContext.CancellationToken
		);

		Assert.AreNotEqual(opened.DocumentId, otherSpelling);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, published.ViewSynchronization.Outcome);
		Assert.IsTrue(fixture.Store.TryGetSnapshot(opened.DocumentId, out WorkspaceDocumentSnapshot? current));
		Assert.AreEqual("edited", current.Content);
	}

	[TestMethod]
	public async Task PublishPendingEditsAsync_ReadsTheSnapshotInTheActionOfTheFinalCheck()
	{
		Queue<(Action Action, TaskCompletionSource Completion)> queue = new();
		bool inline = false;

		// Once the test body ends, the dispatcher runs each action inline, so the teardown of a failed test does not wait for a pump.
		await using var fixture = new CoordinatorTestHost(customDispatch: action =>
		{
			if (inline)
			{
				action();
				return Task.CompletedTask;
			}

			TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
			queue.Enqueue((action, completion));
			return completion.Task;
		});

		Task<WorkspaceViewCoordinatorOpenResult> opening = fixture.Coordinator.OpenWithViewAsync(fixture.DocumentPath, s_openOptions, fixture.View);

		while (!opening.IsCompleted)
			await RunNextAsync();

		WorkspaceDocumentSnapshot opened = (await opening).Snapshot!;
		IWorkspaceViewCoordinator coordinator = fixture.Coordinator;

		Task<WorkspaceViewCoordinatorPublicationResult> publishing = coordinator.PublishPendingEditsAsync(
			opened.DocumentId,
			TestContext.CancellationToken
		);

		// The publication of the views, then the final check.
		await RunNextAsync();
		(Action check, TaskCompletionSource checkCompletion) = await DequeueAsync();
		check();

		// A publication that typing scheduled runs on the views' thread before the dispatcher completes the check.
		Assert.AreEqual(
			WorkspaceDocumentMutationOutcome.Changed,
			fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(opened.Identity, "typed", opened.FileFormat)).Outcome
		);

		checkCompletion.TrySetResult();

		while (!publishing.IsCompleted)
			await RunNextAsync();

		WorkspaceViewCoordinatorPublicationResult published = await publishing;
		inline = true;

		while (queue.Count > 0)
			await RunNextAsync();

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, published.ViewSynchronization.Outcome);
		Assert.AreEqual(opened.Version, published.Snapshot?.Version);
		Assert.AreEqual(opened.Content, published.Snapshot?.Content);

		async Task<(Action Action, TaskCompletionSource Completion)> DequeueAsync()
		{
			(Action Action, TaskCompletionSource Completion) next;

			while (!queue.TryDequeue(out next))
				await Task.Delay(1, TestContext.CancellationToken);

			return next;
		}

		async Task RunNextAsync()
		{
			if (queue.TryDequeue(out (Action Action, TaskCompletionSource Completion) next))
			{
				next.Action();
				next.Completion.TrySetResult();
			}
			else
			{
				await Task.Delay(1, TestContext.CancellationToken);
			}
		}
	}

	private static WorkspaceDocumentReloadResult CreateConflict(CoordinatorTestHost fixture) =>
		new(WorkspaceDocumentReloadOutcome.ExternalFileConflict, new WorkspaceDocumentRequestIdentity(default, fixture.DocumentPath, 0), null);

	// Holds typed text back until the coordinator asks it to publish, as an editor view that publishes when typing pauses does.
	private sealed class PublishingView(string viewId) : IWorkspaceDocumentPublishingView
	{
		private WorkspaceDocumentSnapshot? _synchronized;

		public event EventHandler<ApplyRequestedEventArgs>? ApplyRequested;

		public string ViewId => viewId;

		public WorkspaceDocumentKey? DocumentKey => _synchronized?.DocumentKey;

		public bool HasPendingEdits => _synchronized is not null && Text != _synchronized.Content;

		public bool HasConflict => false;

		public string Text { get; private set; } = string.Empty;

		public bool ThrowOnPublish { get; set; }

		public void Type(string text) => Text = text;

		public void PublishPendingEdits()
		{
			if (ThrowOnPublish)
				throw new InvalidOperationException("The view cannot publish.");

			if (HasPendingEdits && _synchronized is { } synchronized)
			{
				ApplyRequested?.Invoke(
					this,
					new ApplyRequestedEventArgs(new WorkspaceDocumentReplaceRequest(synchronized.Identity, Text, synchronized.FileFormat))
				);
			}
		}

		public WorkspaceDocumentViewOpenResult Open(WorkspaceDocumentSnapshot snapshot)
		{
			Adopt(snapshot);
			return new WorkspaceDocumentViewOpenResult(WorkspaceDocumentViewOpenOutcome.Opened);
		}

		public WorkspaceDocumentViewRefreshResult Refresh(WorkspaceDocumentSnapshot snapshot)
		{
			Adopt(snapshot);
			return new WorkspaceDocumentViewRefreshResult(WorkspaceDocumentViewRefreshOutcome.Refreshed);
		}

		public WorkspaceDocumentViewIdentityResult AcknowledgeIdentity(WorkspaceDocumentViewIdentityChange change)
		{
			_synchronized = change.Snapshot;
			return new WorkspaceDocumentViewIdentityResult(WorkspaceDocumentViewIdentityOutcome.Updated);
		}

		public WorkspaceDocumentViewApplyResult AcknowledgeApply(WorkspaceDocumentMutationResult result)
		{
			if (result.Snapshot is { } snapshot)
				_synchronized = snapshot;

			return new WorkspaceDocumentViewApplyResult(WorkspaceDocumentViewApplyOutcome.Applied);
		}

		public void Close() => _synchronized = null;

		private void Adopt(WorkspaceDocumentSnapshot snapshot)
		{
			_synchronized = snapshot;
			Text = snapshot.Content;
		}
	}
}
