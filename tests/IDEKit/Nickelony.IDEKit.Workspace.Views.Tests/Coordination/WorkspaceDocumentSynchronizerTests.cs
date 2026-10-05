using System.Collections.Concurrent;
using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.LanguageFeatures.Synchronization;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

[TestClass]
public sealed class WorkspaceDocumentSynchronizerTests
{
	private static readonly WorkspaceDocumentOpenOptions s_openOptions = new([TextEncoding.Utf8], TestSnapshots.FileFormat);

	[TestMethod]
	public async Task ClosingTheFirstOfTwoViews_KeepsTheTargetDocumentOpen()
	{
		await using var host = new TestHost();
		using WorkspaceDocumentSynchronizer synchronizer = host.Synchronize();

		FakeWorkspaceView first = await host.AttachAsync("a.lua");
		FakeWorkspaceView second = await host.AttachAsync("a.lua");

		host.Coordinator.DetachView(first);
		Assert.AreSequenceEqual(["open a.lua: text"], host.Target.Calls);

		host.Coordinator.DetachView(second);
		Assert.AreSequenceEqual(["open a.lua: text", "close a.lua"], host.Target.Calls);
	}

	[TestMethod]
	public async Task AMovedFile_IsRenamedOnTheTarget()
	{
		await using var host = new TestHost();
		using WorkspaceDocumentSynchronizer synchronizer = host.Synchronize();

		FakeWorkspaceView view = await host.AttachAsync("a.lua");
		await host.RenameAsync("a.lua", "b.lua");
		host.Coordinator.DetachView(view);

		Assert.AreSequenceEqual(["open a.lua: text", "rename a.lua to b.lua: text", "close b.lua"], host.Target.Calls);
	}

	[TestMethod]
	public async Task AMoveToAnExcludedId_ClosesTheDocument_AndAMoveBackOpensIt()
	{
		await using var host = new TestHost();
		using WorkspaceDocumentSynchronizer synchronizer = host.Synchronize(IsLua);

		await host.AttachAsync("a.lua");

		await host.RenameAsync("a.lua", "a.txt");
		await host.RenameAsync("a.txt", "c.lua");

		Assert.AreSequenceEqual(["open a.lua: text", "close a.lua", "open c.lua: text"], host.Target.Calls);
	}

	[TestMethod]
	public async Task AnExcludedDocument_NeverReachesTheTarget()
	{
		await using var host = new TestHost();
		using WorkspaceDocumentSynchronizer synchronizer = host.Synchronize(IsLua);

		FakeWorkspaceView view = await host.AttachAsync("notes.txt");
		await host.ReplaceAsync("notes.txt", "edited");
		host.Coordinator.DetachView(view);

		Assert.IsEmpty(host.Target.Calls);
	}

	[TestMethod]
	public async Task AnAppliedReplacement_UpdatesTheTarget()
	{
		await using var host = new TestHost();
		using WorkspaceDocumentSynchronizer synchronizer = host.Synchronize();

		await host.AttachAsync("a.lua");
		await host.ReplaceAsync("a.lua", "edited");

		Assert.AreSequenceEqual(["open a.lua: text", "update a.lua: edited"], host.Target.Calls);
	}

	[TestMethod]
	public async Task ADelete_ClosesTheDocument()
	{
		await using var host = new TestHost();
		using WorkspaceDocumentSynchronizer synchronizer = host.Synchronize();

		await host.AttachAsync("a.lua");
		WorkspaceDocumentSnapshot snapshot = host.Snapshot("a.lua");

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> deleted = await host.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(snapshot.Identity, snapshot.OnDiskStamp)
		);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, deleted.StoreResult?.Outcome);
		Assert.AreSequenceEqual(["open a.lua: text", "close a.lua"], host.Target.Calls);
	}

	[TestMethod]
	public async Task AStop_ClosesTheDocumentsOnTheTarget()
	{
		await using var host = new TestHost();
		using WorkspaceDocumentSynchronizer synchronizer = host.Synchronize();

		await host.AttachAsync("a.lua");
		await host.Coordinator.StopAsync();

		Assert.AreSequenceEqual(["open a.lua: text", "close a.lua"], host.Target.Calls);
	}

	[TestMethod]
	public async Task Dispose_ClosesEveryOpenedDocument_AndIgnoresLaterChanges()
	{
		await using var host = new TestHost();
		WorkspaceDocumentSynchronizer synchronizer = host.Synchronize();

		await host.AttachAsync("a.lua");
		await host.AttachAsync("b.lua");

		host.Target.Calls.Clear();

		synchronizer.Dispose();
		Assert.AreSequenceEqual(["close a.lua", "close b.lua"], host.Target.Calls.Order(StringComparer.Ordinal));

		host.Target.Calls.Clear();
		await host.ReplaceAsync("a.lua", "edited");
		synchronizer.Dispose();

		Assert.IsEmpty(host.Target.Calls);
	}

	[TestMethod]
	public async Task TheConstructor_OpensTheDocumentsAttachedBeforeIt()
	{
		await using var host = new TestHost();

		await host.AttachAsync("a.lua");
		await host.ReplaceAsync("a.lua", "edited");

		using WorkspaceDocumentSynchronizer synchronizer = host.Synchronize();

		Assert.AreSequenceEqual(["open a.lua: edited"], host.Target.Calls);
	}

	[TestMethod]
	public async Task AnOpenRaisedAfterTheConstructorReadTheDocument_OpensItOnce()
	{
		await using var host = new TestHost(queued: true);
		File.WriteAllText(host.PathOf("a.lua"), "text");

		Task<WorkspaceViewCoordinatorOpenResult> open = host.Coordinator.OpenWithViewAsync(
			host.PathOf("a.lua"),
			s_openOptions,
			new FakeWorkspaceView()
		);

		// The attach registers the view in one dispatched action and raises the open in a later one, so the constructor runs between them.
		await host.RunUntilAsync(() => host.Coordinator.AttachedDocuments.Count == 1);
		using WorkspaceDocumentSynchronizer synchronizer = host.Synchronize();
		await host.RunUntilAsync(() => open.IsCompleted);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, (await open).Outcome);
		Assert.AreSequenceEqual(["open a.lua: text"], host.Target.Calls);
	}

	[TestMethod]
	public async Task AThrowingTarget_CostsASecondSynchronizerNoCall()
	{
		await using var host = new TestHost();
		using var failing = new WorkspaceDocumentSynchronizer(host.Coordinator, new ThrowingSynchronizationTarget());

		using WorkspaceDocumentSynchronizer synchronizer = host.Synchronize();

		FakeWorkspaceView view = await host.AttachAsync("a.lua");
		await host.ReplaceAsync("a.lua", "edited");
		host.Coordinator.DetachView(view);

		Assert.AreSequenceEqual(["open a.lua: text", "update a.lua: edited", "close a.lua"], host.Target.Calls);
	}

	[TestMethod]
	public async Task AReplacementBetweenTheStoreOpenAndTheRegistration_ReachesTheTargetAfterTheOpen()
	{
		await using var host = new TestHost(queued: true);
		File.WriteAllText(host.PathOf("a.lua"), "text");
		using WorkspaceDocumentSynchronizer synchronizer = host.Synchronize();

		Task<WorkspaceViewCoordinatorOpenResult> open = host.Coordinator.OpenWithViewAsync(
			host.PathOf("a.lua"),
			s_openOptions,
			new FakeWorkspaceView()
		);

		// The rejection check runs first; the registration is queued once the store opened the document, and the replacement lands before it.
		Assert.IsTrue(host.RunOne());
		await TestPolling.UntilAsync(() => host.QueuedCount == 1, TimeSpan.FromSeconds(10));

		WorkspaceDocumentSnapshot snapshot = host.Snapshot("a.lua");
		Task replace = host.Coordinator.ReplaceAsync(new WorkspaceDocumentReplaceRequest(snapshot.Identity, "edited", snapshot.FileFormat));

		await host.RunUntilAsync(() => open.IsCompleted && replace.IsCompleted);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, (await open).Outcome);
		Assert.AreSequenceEqual(["open a.lua: text", "update a.lua: edited"], host.Target.Calls);
	}

	[TestMethod]
	public async Task Constructor_NullArguments_Throw()
	{
		await using var host = new TestHost();

		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceDocumentSynchronizer(null!, host.Target));
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceDocumentSynchronizer(host.Coordinator, null!));
	}

	private static bool IsLua(string documentId) => documentId.EndsWith(".lua", StringComparison.OrdinalIgnoreCase);

	private sealed class ThrowingSynchronizationTarget : IDocumentSynchronizationTarget
	{
		public void OpenDocument(string documentId, string documentText) => throw new InvalidOperationException("The target is down.");

		public void UpdateDocument(string documentId, string documentText) => throw new InvalidOperationException("The target is down.");

		public void RenameDocument(string oldDocumentId, string newDocumentId, string documentText) =>
			throw new InvalidOperationException("The target is down.");

		public void CloseDocument(string documentId) => throw new InvalidOperationException("The target is down.");
	}

	private sealed class TestHost : IAsyncDisposable
	{
		private readonly TemporaryDirectory _directory = new();
		private readonly ConcurrentQueue<(Action Action, TaskCompletionSource Completion)>? _queue;

		private int _viewCount;

		// A queued host holds each dispatched action until RunUntilAsync runs it; another host runs it inline.
		internal TestHost(bool queued = false)
		{
			_queue = queued ? new() : null;
			Store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());
			Coordinator = new WorkspaceViewCoordinator(Store, new ScriptedUiDispatcher(queued ? Enqueue : RunInline));
		}

		internal WorkspaceDocumentStore Store { get; }

		internal WorkspaceViewCoordinator Coordinator { get; }

		internal RecordingSynchronizationTarget Target { get; } = new();

		internal string PathOf(string name) => _directory.GetFilePath(name);

		internal WorkspaceDocumentSynchronizer Synchronize(Func<string, bool>? includes = null) => new(Coordinator, Target, includes);

		internal WorkspaceDocumentSnapshot Snapshot(string name)
		{
			return Store.TryGetSnapshot(PathOf(name), out WorkspaceDocumentSnapshot? snapshot)
				? snapshot
				: throw new AssertFailedException($"{name} is not open.");
		}

		// Attaches a new view to a file, which the first call writes with the text "text".
		internal async Task<FakeWorkspaceView> AttachAsync(string name)
		{
			string path = PathOf(name);

			if (!File.Exists(path))
				File.WriteAllText(path, "text");

			var view = new FakeWorkspaceView($"view {++_viewCount}");
			WorkspaceViewCoordinatorOpenResult opened = await Coordinator.OpenWithViewAsync(path, s_openOptions, view);

			Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, opened.Outcome);
			return view;
		}

		internal async Task ReplaceAsync(string name, string text)
		{
			WorkspaceDocumentSnapshot snapshot = Snapshot(name);

			WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await Coordinator.ReplaceAsync(
				new WorkspaceDocumentReplaceRequest(snapshot.Identity, text, snapshot.FileFormat)
			);

			Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.StoreResult.Outcome);
		}

		internal async Task RenameAsync(string name, string newName)
		{
			WorkspaceDocumentSnapshot snapshot = Snapshot(name);

			WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult> renamed = await Coordinator.RenameAsync(
				new WorkspaceDocumentRenameRequest(snapshot.Identity, snapshot.OnDiskStamp, PathOf(newName))
			);

			Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, renamed.StoreResult.Outcome);
		}

		internal int QueuedCount => _queue?.Count ?? 0;

		// Runs the oldest queued action, and reports whether one was queued.
		internal bool RunOne()
		{
			if (_queue?.TryDequeue(out (Action Action, TaskCompletionSource Completion) entry) != true)
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

		internal async Task RunUntilAsync(Func<bool> condition)
		{
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

			while (!condition())
			{
				if (!RunOne())
					await Task.Delay(1, timeout.Token);
			}
		}

		public async ValueTask DisposeAsync()
		{
			Task stop = Coordinator.StopAsync();

			if (_queue is not null)
				await RunUntilAsync(() => stop.IsCompleted);

			await stop;
			await Store.DisposeAsync();
			_directory.Dispose();
		}

		private static Task RunInline(Action action)
		{
			action();
			return Task.CompletedTask;
		}

		private Task Enqueue(Action action)
		{
			var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			_queue?.Enqueue((action, completion));
			return completion.Task;
		}
	}
}
