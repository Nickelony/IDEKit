using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// Holds a commit of a dirty document at the instant a platform replace leaves no file at the destination, and checks that a reload
/// of the same document does not read that state.
/// </summary>
[TestClass]
public sealed class WorkspaceDocumentStoreReloadRaceTests
{
	private static readonly WorkspaceDocumentOpenOptions s_openOptions = new([TextEncoding.Utf8], TestSnapshots.FileFormat);

	public required TestContext TestContext { get; set; }

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Reload_DirtyDocumentWhileItsCommitIsBetweenTheWriteAndTheMove_ReportsOperationInProgressAndLeavesTheCommitAlone()
	{
		using var temp = new TemporaryDirectory("reload-race-");
		string path = Path.Combine(temp.Path, "doc.txt");
		await File.WriteAllTextAsync(path, "original", TestContext.CancellationToken);

		var reached = new TestSignal();
		using var release = new ManualResetEventSlim();

		// The commit has written its temporary file; this replace unlinks the destination and stops before it links the new file.
		WorkspaceFileSystemPrimitives primitives = WorkspaceFileSystemPrimitives.Default with
		{
			Replace = (source, target) =>
			{
				File.Delete(target);
				reached.Advance();
				release.Wait(TestContext.CancellationToken);
				File.Move(source, target);
			},
		};

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem(options: null, primitives));

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(path, s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentSnapshot dirty = store
			.Replace(
				new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "logical", initial.FileFormat)
			)
			.Snapshot!;

		Task<WorkspaceDocumentCommitResult> commit = Task.Run(
			() =>
				store.CommitAsync(
					new WorkspaceDocumentCommitRequest(new(dirty.DocumentKey, dirty.DocumentId, dirty.Version), dirty.OnDiskStamp),
					TestContext.CancellationToken
				),

			TestContext.CancellationToken
		);

		WorkspaceDocumentReloadResult reload;

		try
		{
			await reached.WhenReachedAsync(1);

			// Without the commit's disk gate, the reload would read a missing destination and report an external deletion.
			reload = await store.ReloadAsync(
				new WorkspaceDocumentReloadRequest(new(dirty.DocumentKey, dirty.DocumentId, dirty.Version)),
				TestContext.CancellationToken
			);
		}
		finally
		{
			release.Set();
		}

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.OperationInProgress, reload.Outcome);
		Assert.IsTrue(reload.Snapshot!.IsDirty);
		Assert.AreEqual("logical", reload.Snapshot.Content);

		WorkspaceDocumentCommitResult committed = await commit;

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.Outcome);
		Assert.AreEqual("logical", await File.ReadAllTextAsync(path, TestContext.CancellationToken));
	}
}
