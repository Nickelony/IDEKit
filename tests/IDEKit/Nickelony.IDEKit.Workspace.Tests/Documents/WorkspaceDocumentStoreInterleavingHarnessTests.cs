using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// Runs a seeded, deterministic sequence of store operations over a real local file system and checks
/// the invariants that must hold after every step.
/// </summary>
/// <remarks>
/// <para>
/// The deterministic suite pins one interleaving per test. This harness is deliberately separate: it
/// scripts no results and drives the public store surface with a seeded operation sequence over a small
/// pool of paths, so the reservation, tracking, gating and rebase machinery is exercised in
/// combinations no test names in advance. A failure reports its seed, and the same seed replays the
/// exact sequence, so a finding turns into a deterministic regression test.
/// </para>
/// <para>
/// Every step is awaited before the next one starts, so the harness fuzzes the operation order, not
/// thread interleavings; the concurrency contracts stay with the deterministic suite.
/// </para>
/// </remarks>
[TestClass]
public sealed class WorkspaceDocumentStoreInterleavingHarnessTests
{
	private const int StepCount = 40;

	private static readonly WorkspaceDocumentOpenOptions s_openOptions = new(
		TextEncodingKind.Utf8,
		TestSnapshots.FileFormat);

	/// <summary>
	/// Runs one seeded operation sequence over a temporary directory and re-checks the store's
	/// invariants after every step.
	/// </summary>
	/// <param name="seed">The sequence seed; it selects the operations and the paths they act on.</param>
	[TestMethod]
	[DataRow(1)]
	[DataRow(2)]
	[DataRow(3)]
	[DataRow(5)]
	[DataRow(8)]
	[DataRow(13)]
	[Timeout(60000)]
	public async Task Harness_SeededOperationSequence_UpholdsTheStoreInvariants(int seed)
	{
		using var temp = new TemporaryDirectory("store-harness-");
		string root = temp.Path;
		string[] paths = [.. Enumerable.Range(0, 4).Select(index => Path.Combine(root, $"doc{index}.txt"))];
		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());
		var random = new Random(seed);
		int persisted = 0;

		for (int step = 0; step < StepCount; step++)
		{
			WorkspaceDocumentSnapshot? affected = await RunStepAsync(store, random, paths, root);

			// The invariants are checked after every step, so a failing sequence names the step it
			// broke at as well as the seed it replays from.
			AssertStoreInvariants(store, root);
			if (affected is not null && await CheckPersistedContentIsIndependentlyVisibleAsync(affected, step, seed))
				persisted++;
		}

		// A sequence that never reached a persisted, clean document would pass every check by skipping
		// them, so the run must have observed at least one.
		Assert.IsTrue(persisted > 0, $"Seed {seed} never reached a persisted document.");
	}

	/// <summary>
	/// Runs one seeded operation against the store and returns the snapshot the operation affected, or
	/// <see langword="null"/> when the operation produced none.
	/// </summary>
	/// <remarks>
	/// Every documented outcome is accepted: the harness asserts the state the store reports, not which
	/// outcome a given operation order produces. Requests are built from the store's own snapshots, so
	/// the documented argument errors stay out of the sequence.
	/// </remarks>
	private static async Task<WorkspaceDocumentSnapshot?> RunStepAsync(
		WorkspaceDocumentStore store,
		Random random,
		string[] paths,
		string root)
	{
		IReadOnlyList<WorkspaceDocumentSnapshot> tracked = store.GetSnapshotsUnderDirectory(root);
		string path = paths[random.Next(paths.Length)];

		// With nothing tracked, opening is the only operation that can make progress.
		int action = random.Next(tracked.Count == 0 ? 1 : 10);
		if (action == 0)
		{
			WorkspaceDocumentOpenResult opened = await store.OpenAsync(path, s_openOptions);
			return opened.Snapshot;
		}

		WorkspaceDocumentSnapshot target = tracked[random.Next(tracked.Count)];
		var identity = new WorkspaceDocumentRequestIdentity(target.DocumentKey, target.DocumentId, target.Version);

		switch (action)
		{
			case 1:
				return store.Replace(new WorkspaceDocumentReplaceRequest(
					identity,
					CreateContent(random),
					target.FileFormat)).Snapshot;

			case 2:
				return (await store.CommitAsync(new WorkspaceDocumentCommitRequest(identity, target.OnDiskStamp))).Snapshot;

			case 3:
				return (await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
					identity,
					target.OnDiskStamp,
					path))).Snapshot;

			case 4:
				return (await store.RenameAsync(new WorkspaceDocumentRenameRequest(
					identity,
					target.OnDiskStamp,
					path))).Snapshot;

			case 5:
				return (await store.ReloadAsync(new WorkspaceDocumentReloadRequest(identity))).Snapshot;

			case 6:
				return store.Discard(new WorkspaceDocumentDiscardRequest(identity)).Snapshot;

			case 7:
				// A successful delete reports the document state immediately before its removal, so the
				// result deliberately still describes the file the delete removed. The invariant check
				// starts from the store's tracked state instead, which no longer contains the document.
				await store.DeleteAsync(new WorkspaceDocumentDeleteRequest(identity, target.OnDiskStamp));
				return null;

			case 8:
				return (await store.ResolveExternalConflictAsync(new WorkspaceDocumentConflictResolutionRequest(
					identity,
					target.OnDiskStamp,
					WorkspaceDocumentConflictResolutionChoice.UseDisk))).Snapshot;

			default:
				return (await store.ResolveExternalConflictAsync(new WorkspaceDocumentConflictResolutionRequest(
					identity,
					target.OnDiskStamp,
					WorkspaceDocumentConflictResolutionChoice.UseLogical))).Snapshot;
		}
	}

	/// <summary>
	/// Asserts that the store's own view of its tracked documents is internally consistent.
	/// </summary>
	private static void AssertStoreInvariants(WorkspaceDocumentStore store, string root)
	{
		IReadOnlyList<WorkspaceDocumentSnapshot> snapshots = store.GetSnapshotsUnderDirectory(root);
		var identities = new HashSet<string>(StringComparer.Ordinal);

		foreach (WorkspaceDocumentSnapshot snapshot in snapshots)
		{
			// An identity is tracked once and stays under the directory it was found in, and the lookup
			// returns the same document the listing describes.
			Assert.IsTrue(identities.Add(snapshot.DocumentId), $"'{snapshot.DocumentId}' is listed twice.");
			Assert.IsTrue(
				snapshot.DocumentId.StartsWith(root, StringComparison.Ordinal),
				$"'{snapshot.DocumentId}' is listed under '{root}' but is not inside it.");
			Assert.IsTrue(store.TryGetSnapshot(snapshot.DocumentId, out WorkspaceDocumentSnapshot? tracked));
			Assert.AreEqual(snapshot.DocumentKey, tracked!.DocumentKey);
			Assert.AreEqual(snapshot.Version, tracked.Version);
			Assert.AreEqual(snapshot.PersistedVersion, tracked.PersistedVersion);
			Assert.AreEqual(snapshot.Content, tracked.Content);
		}
	}

	/// <summary>
	/// Asserts that a clean document the store reports as persisted is visible to an independent store.
	/// </summary>
	/// <remarks>
	/// Only a document that reports an on-disk state and no unsaved content makes a claim about the
	/// file's bytes, so a dirty or not-yet-written document is skipped.
	/// </remarks>
	/// <returns><see langword="true"/> when the snapshot was checked; otherwise, <see langword="false"/>.</returns>
	private static async Task<bool> CheckPersistedContentIsIndependentlyVisibleAsync(
		WorkspaceDocumentSnapshot snapshot,
		int step,
		int seed)
	{
		if (!snapshot.ExistsOnDisk || snapshot.IsDirty)
			return false;

		Assert.IsTrue(
			File.Exists(snapshot.DocumentId),
			$"The store reports '{snapshot.DocumentId}' as persisted but no file exists (step {step}, seed {seed}).");

		// The file is read by a second store that shares no state with the one under test, which is what
		// makes the claim independent of the store's bookkeeping.
		await using var reader = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());
		WorkspaceDocumentOpenResult reopened = await reader.OpenAsync(snapshot.DocumentId, s_openOptions);

		Assert.AreEqual(
			WorkspaceDocumentOpenOutcome.Opened,
			reopened.Outcome,
			$"The persisted file of '{snapshot.DocumentId}' could not be opened (step {step}, seed {seed}).");
		Assert.AreEqual(snapshot.Content, reopened.Snapshot!.Content, $"Content mismatch at step {step}, seed {seed}.");
		Assert.AreEqual(snapshot.OnDiskStamp, reopened.Snapshot.OnDiskStamp, $"Stamp mismatch at step {step}, seed {seed}.");
		return true;
	}

	// Text content without line breaks, so the round trip through any newline style and encoding is
	// lossless and the assertion compares what the store was given with what the file holds.
	private static string CreateContent(Random random)
	{
		int length = random.Next(1, 12);
		return string.Create(length, random, static (span, random) =>
		{
			for (int index = 0; index < span.Length; index++)
				span[index] = (char)('a' + random.Next(26));
		});
	}
}
