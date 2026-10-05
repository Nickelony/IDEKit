using System.Diagnostics;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// Runs several tasks at once against one store over a real local directory, each on documents of its own and all of them on a few shared paths,
/// and checks what the contracts promise once they finish.
/// </summary>
/// <remarks>
/// The seeded harness awaits every step, so it fuzzes the order of operations.
/// The soak overlaps them: opens, replacements, commits, save-as operations, renames, reloads, discards, deletes and conflict
/// resolutions race on the shared paths, with stale identities among them. A task on its own documents expects every operation to succeed,
/// because no other task touches them, so the soak also proves that the load on the shared paths does not reach an unrelated document.
/// The seed is logged and the environment variable <see cref="TestSoak.SeedVariable"/> replays it; the replay repeats each task's operations,
/// not the interleaving.
/// </remarks>
[TestClass]
public sealed class WorkspaceDocumentStoreSoakTests
{
	private const int TaskCount = 6;
	private const int SharedPathCount = 4;
	private const int StepsPerTask = 5000;

	private static readonly TimeSpan s_stepBudget = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan s_hangBound = TimeSpan.FromSeconds(30);

	private static readonly WorkspaceDocumentOpenOptions s_openOptions = new([TextEncoding.Utf8], TestSnapshots.FileFormat);

	public required TestContext TestContext { get; set; }

	[TestMethod]
	[Timeout(90000, CooperativeCancellation = true)]
	public async Task Soak_ConcurrentOperationsOnOwnAndSharedDocuments_UpholdTheStoreInvariants()
	{
		int seed = TestSoak.ReadSeed(TestContext);

		using var temp = new TemporaryDirectory("store-soak-");
		string[] shared = [.. Enumerable.Range(0, SharedPathCount).Select(index => Path.Combine(temp.Path, $"shared{index}.txt"))];

		var log = new RecordingSoakLog();
		var owned = new OwnDocument[TaskCount];
		var clock = Stopwatch.StartNew();

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		await TestSoak.RunAsync(
			TaskCount,

			body: async index =>
			{
				var random = new Random(HashCode.Combine(seed, index));
				var own = new OwnDocument(Path.Combine(temp.Path, $"own{index}-a.txt"), Path.Combine(temp.Path, $"own{index}-b.txt"));

				owned[index] = own;

				Dictionary<string, WorkspaceDocumentSnapshot> remembered = new(StringComparer.Ordinal);

				for (int step = 0; step < StepsPerTask && clock.Elapsed < s_stepBudget; step++)
				{
					if (random.Next(3) == 0)
						await RunOwnStepAsync(store, log, own, random, token: $"own{index}-{step}");
					else
						await RunSharedStepAsync(store, log, shared, remembered, random, token: $"shared{index}-{step}");
				}
			},

			seed,
			hangBound: s_hangBound
		);

		TestContext.WriteLine($"Soak outcomes: {log.Summary}");

		foreach (OwnDocument own in owned)
			VerifyOwnDocument(store, log, own);

		log.CheckTrackedDocuments(store, temp.Path);
		log.AssertNoViolations(seed);

		await store.DisposeAsync();

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => store.OpenAsync(shared[0], s_openOptions));
	}

	private static async Task RunOwnStepAsync(WorkspaceDocumentStore store, RecordingSoakLog log, OwnDocument own, Random random, string token)
	{
		if (!own.Tracked)
		{
			WorkspaceDocumentOpenResult opened = await store.OpenAsync(own.Path, s_openOptions);

			log.Record("Open", opened.Outcome, opened.Failure);
			log.Observe(opened.Snapshot);

			Expect(
				log,
				opened.Outcome == WorkspaceDocumentOpenOutcome.Opened,
				$"{own.Path} was not opened ({opened.Outcome}), and no other task touches it."
			);

			Expect(log, opened.Snapshot?.Content == (own.Disk ?? string.Empty), $"{own.Path} opened with another content than its file.");

			own.Tracked = true;
			own.Logical = own.Disk ?? string.Empty;
			return;
		}

		if (!store.TryGetSnapshot(own.Path, out WorkspaceDocumentSnapshot? snapshot))
		{
			log.Violate($"{own.Path} vanished from the store, and no other task touches it.");
			own.Tracked = false;
			own.Disk = null;
			return;
		}

		WorkspaceDocumentRequestIdentity identity = snapshot.Identity;

		switch (random.Next(7))
		{
			case 0:
			case 1:
				var replace = new WorkspaceDocumentReplaceRequest(identity, token, snapshot.FileFormat);
				WorkspaceDocumentMutationResult replaced = store.Replace(replace);

				log.RecordReplacement(identity, replaced);
				Expect(log, replaced.Outcome == WorkspaceDocumentMutationOutcome.Changed, $"The replacement of {own.Path} ended {replaced.Outcome}.");
				own.Logical = token;
				break;

			case 2:
				if (!snapshot.IsDirty)
					break;

				WorkspaceDocumentCommitResult committed = await store.CommitAsync(new WorkspaceDocumentCommitRequest(identity, snapshot.OnDiskStamp));

				log.Record("Commit", committed.Outcome, committed.Failure);
				log.Observe(committed.Snapshot);
				Expect(log, committed.Outcome == WorkspaceDocumentCommitOutcome.Committed, $"The commit of {own.Path} ended {committed.Outcome}.");
				own.Disk = own.Logical;
				break;

			case 3:
				if (own.Disk is null)
					break;

				WorkspaceDocumentReloadResult reloaded = await store.ReloadAsync(new WorkspaceDocumentReloadRequest(identity));

				log.Record("Reload", reloaded.Outcome, reloaded.Failure);
				log.Observe(reloaded.Snapshot);

				// The file did not change, so the reload has nothing to adopt, whether or not the text has unsaved edits.
				Expect(log, reloaded.Outcome == WorkspaceDocumentReloadOutcome.Unchanged, $"The reload of {own.Path} ended {reloaded.Outcome}.");
				break;

			case 4:
				WorkspaceDocumentMutationResult discarded = store.Discard(new WorkspaceDocumentDiscardRequest(identity));

				log.Record("Discard", discarded.Outcome);
				log.Observe(discarded.Snapshot);
				Expect(log, discarded.Applied, $"The discard of {own.Path} ended {discarded.Outcome}.");
				own.Logical = own.Disk ?? string.Empty;
				break;

			case 5:
				if (own.Disk is null)
					break;

				WorkspaceDocumentRenameResult renamed = await store.RenameAsync(
					new WorkspaceDocumentRenameRequest(identity, snapshot.OnDiskStamp, own.OtherPath)
				);

				log.Record("Rename", renamed.Outcome, renamed.Failure);
				log.Observe(renamed.Snapshot);
				Expect(log, renamed.Outcome == WorkspaceDocumentRenameOutcome.Renamed, $"The rename of {own.Path} ended {renamed.Outcome}.");
				own.SwapPath();
				break;

			default:
				if (own.Disk is null)
					break;

				WorkspaceDocumentDeleteResult deleted = await store.DeleteAsync(new WorkspaceDocumentDeleteRequest(identity, snapshot.OnDiskStamp));

				log.Record("Delete", deleted.Outcome, deleted.Failure);
				log.Observe(deleted.Snapshot);
				Expect(log, deleted.Outcome == WorkspaceDocumentDeleteOutcome.Deleted, $"The delete of {own.Path} ended {deleted.Outcome}.");
				own.Tracked = false;
				own.Disk = null;
				own.Logical = string.Empty;
				break;
		}
	}

	/// <summary>
	/// Runs one operation on a shared path. A third of the operations use the snapshot the task saw earlier, so stale identities race
	/// with fresh ones.
	/// </summary>
	private static async Task RunSharedStepAsync(
		WorkspaceDocumentStore store,
		RecordingSoakLog log,
		string[] shared,
		Dictionary<string, WorkspaceDocumentSnapshot> remembered,
		Random random,
		string token
	)
	{
		string path = shared[random.Next(shared.Length)];

		store.TryGetSnapshot(path, out WorkspaceDocumentSnapshot? current);
		remembered.TryGetValue(path, out WorkspaceDocumentSnapshot? earlier);

		WorkspaceDocumentSnapshot? basis = earlier is not null && random.Next(3) == 0 ? earlier : current ?? earlier;

		if (basis is null || random.Next(8) == 0)
		{
			WorkspaceDocumentOpenResult opened = await store.OpenAsync(path, s_openOptions);

			log.Record("Open", opened.Outcome, opened.Failure);
			log.Observe(opened.Snapshot);
			Remember(remembered, path, opened.Snapshot);
			return;
		}

		string destination = shared[random.Next(shared.Length)];
		WorkspaceDocumentRequestIdentity identity = basis.Identity;

		if (random.Next(4) == 0)
			await Task.Yield();

		// A document without a file has nothing to reload or to resolve against, and its reload reports the missing file.
		int action = random.Next(9);

		if (!basis.ExistsOnDisk && action == 6)
			action = 0;

		switch (action)
		{
			case 0:
			case 1:
			case 2:
				WorkspaceDocumentMutationResult replaced = store.Replace(new WorkspaceDocumentReplaceRequest(identity, token, basis.FileFormat));

				log.RecordReplacement(identity, replaced);
				Remember(remembered, path, replaced.Snapshot);
				break;

			case 3:
				WorkspaceDocumentCommitResult committed = await store.CommitAsync(new WorkspaceDocumentCommitRequest(identity, basis.OnDiskStamp));

				log.Record("Commit", committed.Outcome, committed.Failure);
				log.Observe(committed.Snapshot);
				Remember(remembered, path, committed.Snapshot);
				break;

			case 4:
				WorkspaceDocumentSaveAsResult savedAs = await store.SaveAsAsync(
					new WorkspaceDocumentSaveAsRequest(identity, basis.OnDiskStamp, destination)
				);

				log.Record("SaveAs", savedAs.Outcome, savedAs.Failure);
				log.Observe(savedAs.Snapshot);
				Remember(remembered, destination, savedAs.Snapshot);
				break;

			case 5:
				WorkspaceDocumentRenameResult renamed = await store.RenameAsync(
					new WorkspaceDocumentRenameRequest(identity, basis.OnDiskStamp, destination)
				);

				log.Record("Rename", renamed.Outcome, renamed.Failure);
				log.Observe(renamed.Snapshot);
				Remember(remembered, destination, renamed.Snapshot);
				break;

			case 6:
				WorkspaceDocumentReloadResult reloaded = await store.ReloadAsync(new WorkspaceDocumentReloadRequest(identity));

				log.Record("Reload", reloaded.Outcome, reloaded.Failure);
				log.Observe(reloaded.Snapshot);
				Remember(remembered, path, reloaded.Snapshot);
				break;

			case 7:
				WorkspaceDocumentMutationResult discarded = store.Discard(new WorkspaceDocumentDiscardRequest(identity));

				log.Record("Discard", discarded.Outcome);
				log.Observe(discarded.Snapshot);
				Remember(remembered, path, discarded.Snapshot);
				break;

			default:
				if (!basis.ExistsOnDisk || random.Next(2) == 0)
				{
					WorkspaceDocumentDeleteResult deleted = await store.DeleteAsync(new WorkspaceDocumentDeleteRequest(identity, basis.OnDiskStamp));

					log.Record("Delete", deleted.Outcome, deleted.Failure);
					log.Observe(deleted.Snapshot);
				}
				else
				{
					WorkspaceDocumentConflictResolutionMode mode =
						random.Next(2) == 0
							? WorkspaceDocumentConflictResolutionMode.ResolveWithDisk
							: WorkspaceDocumentConflictResolutionMode.ResolveWithLogical;

					WorkspaceDocumentConflictResolutionResult resolved = await store.ResolveExternalConflictAsync(
						new WorkspaceDocumentConflictResolutionRequest(identity, basis.OnDiskStamp, mode)
					);

					log.Record("ResolveExternalConflict", resolved.Outcome, resolved.Failure);
					log.Observe(resolved.Snapshot);
					Remember(remembered, path, resolved.Snapshot);
				}

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

	/// <summary>Checks that a document only its task touched ended as the task left it: its text, its file, and the store's view of both.</summary>
	private static void VerifyOwnDocument(WorkspaceDocumentStore store, RecordingSoakLog log, OwnDocument own)
	{
		bool tracked = store.TryGetSnapshot(own.Path, out WorkspaceDocumentSnapshot? snapshot);

		if (tracked != own.Tracked)
		{
			log.Violate($"{own.Path} is {(tracked ? "tracked" : "not tracked")} but its task left it {(own.Tracked ? "tracked" : "closed")}.");
			return;
		}

		if (snapshot is not null && snapshot.Content != own.Logical)
			log.Violate($"{own.Path} holds '{snapshot.Content}' where its task left '{own.Logical}', so an edit is lost.");

		string? onDisk = File.Exists(own.Path) ? File.ReadAllText(own.Path) : null;

		if (onDisk != own.Disk)
			log.Violate($"The file {own.Path} holds '{onDisk}' where its task left '{own.Disk}'.");

		if (File.Exists(own.OtherPath))
			log.Violate($"The file {own.OtherPath} exists, but its task moved the document away from it.");
	}

	/// <summary>
	/// A document that one task alone opens, edits, saves, reloads, renames between two paths and deletes, with the state it expects.
	/// </summary>
	private sealed class OwnDocument(string first, string second)
	{
		public string Path { get; private set; } = first;

		public string OtherPath { get; private set; } = second;

		public bool Tracked { get; set; }

		/// <summary>The text the store holds for the document.</summary>
		public string Logical { get; set; } = string.Empty;

		/// <summary>The text of the file, or <see langword="null"/> when the document has none.</summary>
		public string? Disk { get; set; }

		public void SwapPath() => (Path, OtherPath) = (OtherPath, Path);
	}
}
