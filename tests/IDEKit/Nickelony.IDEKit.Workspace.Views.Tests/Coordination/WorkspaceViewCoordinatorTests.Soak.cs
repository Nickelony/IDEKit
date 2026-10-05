using System.Diagnostics;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceViewCoordinatorTests
{
	// The soak runs several tasks at once against one coordinator, one store and one dispatcher thread, each task on a document and a
	// view of its own and all of them on a few shared paths. A task on its own document expects every operation to succeed and its view to
	// follow the document, because no other task touches them. tests/TestSupport/README.md describes the seed.
	private const int SoakTaskCount = 6;
	private const int SoakSharedPathCount = 4;
	private const int SoakStepsPerTask = 5000;

	private static readonly TimeSpan s_soakStepBudget = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan s_soakHangBound = TimeSpan.FromSeconds(30);

	[TestMethod]
	[Timeout(90000, CooperativeCancellation = true)]
	public async Task Soak_ConcurrentOperationsOnOwnAndSharedDocuments_UpholdTheCoordinatorInvariants()
	{
		int seed = TestSoak.ReadSeed(TestContext);

		await using var fixture = new CoordinatorTestHost(pumpDispatch: true);
		string[] shared = [.. Enumerable.Range(0, SoakSharedPathCount).Select(index => Path.Combine(fixture.DirectoryPath, $"shared{index}.txt"))];

		var log = new RecordingSoakLog();
		var owned = new SoakOwnDocument[SoakTaskCount];
		var sharedViews = new FakeCoordinatorView[SoakTaskCount][];
		var clock = Stopwatch.StartNew();

		await TestSoak.RunAsync(
			taskCount: SoakTaskCount,

			body: async index =>
			{
				var random = new Random(HashCode.Combine(seed, index));

				var own = new SoakOwnDocument(
					new FakeCoordinatorView(fixture, $"own-{index}"),
					Path.Combine(fixture.DirectoryPath, $"own{index}-a.txt"),
					Path.Combine(fixture.DirectoryPath, $"own{index}-b.txt")
				);

				FakeCoordinatorView[] views = [new(fixture, $"shared-{index}-a"), new(fixture, $"shared-{index}-b")];

				owned[index] = own;
				sharedViews[index] = views;

				Dictionary<string, WorkspaceDocumentSnapshot> remembered = new(StringComparer.Ordinal);

				for (int step = 0; step < SoakStepsPerTask && clock.Elapsed < s_soakStepBudget; step++)
				{
					if (random.Next(3) == 0)
						await RunSoakOwnStepAsync(fixture, log, own, random, token: $"own{index}-{step}");
					else
						await RunSoakSharedStepAsync(fixture, log, shared, views, remembered, random, token: $"shared{index}-{step}");
				}
			},

			seed,
			hangBound: s_soakHangBound
		);

		TestContext.WriteLine($"Soak outcomes: {log.Summary}");

		foreach (SoakOwnDocument own in owned)
			VerifySoakOwnDocument(fixture, log, own);

		log.CheckTrackedDocuments(fixture.Coordinator.Documents, fixture.DirectoryPath);
		VerifySoakViews(fixture, log, [.. sharedViews.SelectMany(views => views), .. owned.Select(own => own.View)]);

		await fixture.Coordinator.StopAsync().WaitAsync(s_soakHangBound, TestContext.CancellationToken);

		foreach (FakeCoordinatorView view in sharedViews.SelectMany(views => views).Concat(owned.Select(own => own.View)))
		{
			if (view.DocumentId is not null || view.ApplySubscriberCount != 0)
				log.Violate($"The view {view.ViewId} is still attached after the stop.");
		}

		if (fixture.Coordinator.AttachedDocuments.Count != 0)
			log.Violate("The coordinator still lists attached documents after the stop.");

		log.AssertNoViolations(seed);
	}

	private static async Task RunSoakOwnStepAsync(CoordinatorTestHost fixture, RecordingSoakLog log, SoakOwnDocument own, Random random, string token)
	{
		WorkspaceViewCoordinator coordinator = fixture.Coordinator;

		if (!own.Attached)
		{
			WorkspaceViewCoordinatorOpenResult opened = await coordinator.OpenWithViewAsync(own.Path, s_openOptions, own.View);

			log.Record("OpenWithView", opened.Outcome, opened.Failure);
			log.Observe(opened.Snapshot);

			Expect(log, opened.Outcome == WorkspaceViewCoordinatorOpenOutcome.Opened, $"{own.Path} was not opened with its view ({opened.Outcome}).");

			Expect(
				log,
				own.View.Text == own.Logical,
				$"The view of {own.Path} opened with '{own.View.Text}' where the document holds '{own.Logical}'."
			);

			own.Attached = true;
			own.Tracked = true;
			return;
		}

		if (!coordinator.Documents.TryGetSnapshot(own.Path, out WorkspaceDocumentSnapshot? snapshot))
		{
			log.Violate($"{own.Path} vanished from the store, and no other task touches it.");
			own.Attached = false;
			own.Tracked = false;
			return;
		}

		WorkspaceDocumentRequestIdentity identity = snapshot.Identity;

		switch (random.Next(8))
		{
			case 0:
			case 1:
				var replace = new WorkspaceDocumentReplaceRequest(identity, token, snapshot.FileFormat);
				WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await coordinator.ReplaceAsync(replace);

				ExpectSynchronized(log, "Replace", replaced.ViewSynchronization);

				if (replaced.StoreResult is { } mutation)
				{
					log.RecordReplacement(identity, mutation);

					Expect(
						log,
						mutation.Outcome == WorkspaceDocumentMutationOutcome.Changed,
						$"The replacement of {own.Path} ended {mutation.Outcome}."
					);
				}

				own.Logical = token;
				Expect(log, own.View.Text == token, $"The view of {own.Path} shows '{own.View.Text}' after the replacement to '{token}'.");
				break;

			case 2:
				if (!snapshot.IsDirty)
					break;

				WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult> committed = await coordinator.CommitAsync(
					new WorkspaceDocumentCommitRequest(identity, snapshot.OnDiskStamp)
				);

				ExpectSynchronized(log, "Commit", committed.ViewSynchronization);

				if (committed.StoreResult is { } commit)
				{
					log.Record("Commit", commit.Outcome, commit.Failure);
					log.Observe(commit.Snapshot);
					Expect(log, commit.Outcome == WorkspaceDocumentCommitOutcome.Committed, $"The commit of {own.Path} ended {commit.Outcome}.");
				}

				own.Disk = own.Logical;
				break;

			case 3:
				if (own.Disk is null)
					break;

				WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult> reloaded = await coordinator.ReloadAsync(
					new WorkspaceDocumentReloadRequest(identity)
				);

				ExpectSynchronized(log, "Reload", reloaded.ViewSynchronization);

				if (reloaded.StoreResult is { } reload)
				{
					log.Record("Reload", reload.Outcome, reload.Failure);
					log.Observe(reload.Snapshot);
					Expect(log, reload.Outcome == WorkspaceDocumentReloadOutcome.Unchanged, $"The reload of {own.Path} ended {reload.Outcome}.");
				}

				break;

			case 4:
				WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentMutationResult> discarded = await coordinator.DiscardAsync(
					new WorkspaceDocumentDiscardRequest(identity)
				);

				ExpectSynchronized(log, "Discard", discarded.ViewSynchronization);
				log.Record("Discard", discarded.StoreResult.Outcome);
				log.Observe(discarded.StoreResult.Snapshot);
				Expect(log, discarded.StoreResult.Applied, $"The discard of {own.Path} ended {discarded.StoreResult.Outcome}.");
				own.Logical = own.Disk ?? string.Empty;

				Expect(
					log,
					own.View.Text == own.Logical,
					$"The view of {own.Path} shows '{own.View.Text}' after the discard ({discarded.StoreResult.Outcome}, store '{discarded.StoreResult.Snapshot?.Content}', dirty {discarded.StoreResult.Snapshot?.IsDirty}) to '{own.Logical}'."
				);

				break;

			case 5:
				if (own.Disk is null)
					break;

				WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult> renamed = await coordinator.RenameAsync(
					new WorkspaceDocumentRenameRequest(identity, snapshot.OnDiskStamp, own.OtherPath)
				);

				ExpectSynchronized(log, "Rename", renamed.ViewSynchronization);
				log.Record("Rename", renamed.StoreResult.Outcome, renamed.StoreResult.Failure);
				log.Observe(renamed.StoreResult.Snapshot);

				Expect(
					log,
					renamed.StoreResult.Outcome == WorkspaceDocumentRenameOutcome.Renamed,
					$"The rename of {own.Path} ended {renamed.StoreResult.Outcome}."
				);

				own.SwapPath();
				Expect(log, own.View.DocumentId == own.Path, $"The view follows the document to '{own.View.DocumentId}', not to '{own.Path}'.");
				break;

			case 6:
				if (own.Disk is null)
					break;

				WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> deleted = await coordinator.DeleteAsync(
					new WorkspaceDocumentDeleteRequest(identity, snapshot.OnDiskStamp)
				);

				ExpectSynchronized(log, "Delete", deleted.ViewSynchronization);

				if (deleted.StoreResult is { } delete)
				{
					log.Record("Delete", delete.Outcome, delete.Failure);
					log.Observe(delete.Snapshot);
					Expect(log, delete.Outcome == WorkspaceDocumentDeleteOutcome.Deleted, $"The delete of {own.Path} ended {delete.Outcome}.");
				}

				own.Attached = false;
				own.Tracked = false;
				own.Disk = null;
				own.Logical = string.Empty;
				Expect(log, own.View.DocumentId is null, $"The view of {own.Path} is still attached to '{own.View.DocumentId}' after the delete.");
				break;

			default:
				coordinator.DetachView(own.View);

				// A detach unsubscribes the view and leaves it bound to its document, so the host closes it on the dispatcher thread.
				await fixture.Dispatcher.InvokeAsync(own.View.Close);
				own.Attached = false;
				Expect(log, own.View.ApplySubscriberCount == 0, $"The view of {own.Path} is still subscribed after the detach.");
				break;
		}
	}

	/// <summary>
	/// Runs one operation on a shared path through one of the task's views. A third of the operations use the snapshot the task saw earlier,
	/// so stale identities race with fresh ones.
	/// </summary>
	private static async Task RunSoakSharedStepAsync(
		CoordinatorTestHost fixture,
		RecordingSoakLog log,
		string[] shared,
		FakeCoordinatorView[] views,
		Dictionary<string, WorkspaceDocumentSnapshot> remembered,
		Random random,
		string token
	)
	{
		WorkspaceViewCoordinator coordinator = fixture.Coordinator;
		string path = shared[random.Next(shared.Length)];

		coordinator.Documents.TryGetSnapshot(path, out WorkspaceDocumentSnapshot? current);
		remembered.TryGetValue(path, out WorkspaceDocumentSnapshot? earlier);

		WorkspaceDocumentSnapshot? basis = earlier is not null && random.Next(3) == 0 ? earlier : current ?? earlier;

		if (basis is null || random.Next(6) == 0)
		{
			FakeCoordinatorView view = views[random.Next(views.Length)];
			WorkspaceViewCoordinatorOpenResult opened = await coordinator.OpenWithViewAsync(path, s_openOptions, view);

			log.Record("OpenWithView", opened.Outcome, opened.Failure);
			log.Observe(opened.Snapshot);
			Remember(remembered, path, opened.Snapshot);
			return;
		}

		string destination = shared[random.Next(shared.Length)];
		WorkspaceDocumentRequestIdentity identity = basis.Identity;

		if (random.Next(4) == 0)
			await Task.Yield();

		int action = random.Next(10);

		// A document without a file has nothing to reload, and its reload reports the missing file.
		if (!basis.ExistsOnDisk && action == 6)
			action = 0;

		switch (action)
		{
			case 0:
			case 1:
			case 2:
				WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await coordinator.ReplaceAsync(
					new WorkspaceDocumentReplaceRequest(identity, token, basis.FileFormat)
				);

				ExpectSynchronized(log, "Replace", replaced.ViewSynchronization);

				if (replaced.StoreResult is { } mutation)
				{
					log.RecordReplacement(identity, mutation);
					Remember(remembered, path, mutation.Snapshot);
				}

				break;

			case 3:
				WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult> committed = await coordinator.CommitAsync(
					new WorkspaceDocumentCommitRequest(identity, basis.OnDiskStamp)
				);

				ExpectSynchronized(log, "Commit", committed.ViewSynchronization);

				if (committed.StoreResult is { } commit)
				{
					log.Record("Commit", commit.Outcome, commit.Failure);
					log.Observe(commit.Snapshot);
					Remember(remembered, path, commit.Snapshot);
				}

				break;

			case 4:
				WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentSaveAsResult> savedAs = await coordinator.SaveAsAsync(
					new WorkspaceDocumentSaveAsRequest(identity, basis.OnDiskStamp, destination)
				);

				ExpectSynchronized(log, "SaveAs", savedAs.ViewSynchronization);
				log.Record("SaveAs", savedAs.StoreResult.Outcome, savedAs.StoreResult.Failure);
				log.Observe(savedAs.StoreResult.Snapshot);
				Remember(remembered, destination, savedAs.StoreResult.Snapshot);
				break;

			case 5:
				WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult> renamed = await coordinator.RenameAsync(
					new WorkspaceDocumentRenameRequest(identity, basis.OnDiskStamp, destination)
				);

				ExpectSynchronized(log, "Rename", renamed.ViewSynchronization);
				log.Record("Rename", renamed.StoreResult.Outcome, renamed.StoreResult.Failure);
				log.Observe(renamed.StoreResult.Snapshot);
				Remember(remembered, destination, renamed.StoreResult.Snapshot);
				break;

			case 6:
				WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult> reloaded = await coordinator.ReloadAsync(
					new WorkspaceDocumentReloadRequest(identity)
				);

				ExpectSynchronized(log, "Reload", reloaded.ViewSynchronization);

				if (reloaded.StoreResult is { } reload)
				{
					log.Record("Reload", reload.Outcome, reload.Failure);
					log.Observe(reload.Snapshot);
					Remember(remembered, path, reload.Snapshot);
				}

				break;

			case 7:
				WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentMutationResult> discarded = await coordinator.DiscardAsync(
					new WorkspaceDocumentDiscardRequest(identity)
				);

				ExpectSynchronized(log, "Discard", discarded.ViewSynchronization);
				log.Record("Discard", discarded.StoreResult.Outcome);
				log.Observe(discarded.StoreResult.Snapshot);
				Remember(remembered, path, discarded.StoreResult.Snapshot);
				break;

			case 8:
				WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> deleted = await coordinator.DeleteAsync(
					new WorkspaceDocumentDeleteRequest(identity, basis.OnDiskStamp)
				);

				ExpectSynchronized(log, "Delete", deleted.ViewSynchronization);

				if (deleted.StoreResult is { } delete)
				{
					log.Record("Delete", delete.Outcome, delete.Failure);
					log.Observe(delete.Snapshot);
				}

				break;

			default:
				FakeCoordinatorView detached = views[random.Next(views.Length)];

				coordinator.DetachView(detached);
				await fixture.Dispatcher.InvokeAsync(detached.Close);
				break;
		}
	}

	private static void Remember(Dictionary<string, WorkspaceDocumentSnapshot> remembered, string path, WorkspaceDocumentSnapshot? snapshot)
	{
		if (snapshot is not null)
			remembered[path] = snapshot;
	}

	private static void Expect(RecordingSoakLog log, bool condition, string message)
	{
		if (!condition)
			log.Violate(message);
	}

	/// <summary>
	/// Checks that no view blocked an operation or failed to follow it. The soak's views never hold unsaved edits and never fail, so each operation
	/// reports its views as synchronized.
	/// </summary>
	private static void ExpectSynchronized(RecordingSoakLog log, string operation, WorkspaceDocumentViewSynchronizationResult synchronization)
	{
		log.Record($"{operation}.Views", synchronization.Outcome);

		if (synchronization.Outcome != WorkspaceDocumentViewSynchronizationOutcome.Synchronized)
		{
			log.Violate(
				$"{operation} reported its views as {synchronization.Outcome}: {string.Join(", ", synchronization.Issues.Select(issue => issue.ViewId))}."
			);
		}
	}

	/// <summary>Checks that a document and a view only their task touched ended as the task left them.</summary>
	private static void VerifySoakOwnDocument(CoordinatorTestHost fixture, RecordingSoakLog log, SoakOwnDocument own)
	{
		bool tracked = fixture.Coordinator.Documents.TryGetSnapshot(own.Path, out WorkspaceDocumentSnapshot? snapshot);

		if (tracked != own.Tracked)
		{
			log.Violate($"{own.Path} is {(tracked ? "tracked" : "not tracked")} but its task left it {(own.Tracked ? "open" : "closed")}.");
			return;
		}

		if (snapshot is not null && (snapshot.Content != own.Logical || (own.Attached && own.View.Text != own.Logical)))
		{
			log.Violate(
				$"{own.Path} holds '{snapshot.Content}' and its view '{own.View.Text}' where its task left '{own.Logical}', so an edit is lost."
			);
		}

		string? onDisk = File.Exists(own.Path) ? File.ReadAllText(own.Path) : null;

		if (onDisk != own.Disk)
			log.Violate($"The file {own.Path} holds '{onDisk}' where its task left '{own.Disk}'.");
	}

	/// <summary>
	/// Checks every view against the store once the tasks have finished: an attached view follows a tracked document, holds its text
	/// and is subscribed once, a detached view is subscribed never, and the coordinator lists exactly the documents the views are attached to.
	/// </summary>
	private static void VerifySoakViews(CoordinatorTestHost fixture, RecordingSoakLog log, FakeCoordinatorView[] views)
	{
		HashSet<string> attachedIds = new(StringComparer.OrdinalIgnoreCase);

		foreach (FakeCoordinatorView view in views)
		{
			if (view.DocumentId is null)
			{
				if (view.ApplySubscriberCount != 0)
					log.Violate($"The detached view {view.ViewId} is still subscribed.");

				continue;
			}

			attachedIds.Add(view.DocumentId);

			if (view.ApplySubscriberCount != 1)
				log.Violate($"The attached view {view.ViewId} has {view.ApplySubscriberCount} subscriptions.");

			if (!fixture.Coordinator.Documents.TryGetSnapshot(view.DocumentId, out WorkspaceDocumentSnapshot? snapshot))
				log.Violate($"The view {view.ViewId} is attached to {view.DocumentId}, which the store no longer tracks.");
			else if (view.Text != snapshot.Content)
				log.Violate($"The view {view.ViewId} shows '{view.Text}' where the document {view.DocumentId} holds '{snapshot.Content}'.");
		}

		HashSet<string> listedIds = new(
			fixture.Coordinator.AttachedDocuments.Select(snapshot => snapshot.DocumentId),
			StringComparer.OrdinalIgnoreCase
		);

		if (!attachedIds.SetEquals(listedIds))
		{
			log.Violate(
				$"The coordinator lists the attached documents [{string.Join(", ", listedIds.Order())}] where the views are attached to [{string.Join(", ", attachedIds.Order())}]."
			);
		}
	}

	/// <summary>A document and a view that one task alone opens, edits, saves, reloads, renames between two paths, deletes and detaches.</summary>
	private sealed class SoakOwnDocument(FakeCoordinatorView view, string first, string second)
	{
		public FakeCoordinatorView View { get; } = view;

		public string Path { get; private set; } = first;

		public string OtherPath { get; private set; } = second;

		/// <summary>Whether the task left the view attached to the document.</summary>
		public bool Attached { get; set; }

		/// <summary>Whether the store tracks the document, which a detached view leaves open with its unsaved text.</summary>
		public bool Tracked { get; set; }

		/// <summary>The text the store holds for the document.</summary>
		public string Logical { get; set; } = string.Empty;

		/// <summary>The text of the file, or <see langword="null"/> when the document has none.</summary>
		public string? Disk { get; set; }

		public void SwapPath() => (Path, OtherPath) = (OtherPath, Path);
	}
}
