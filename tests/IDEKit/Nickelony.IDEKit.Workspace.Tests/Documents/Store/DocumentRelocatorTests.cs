using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>Covers the relocator directly: the retarget of a rename and a save-as, the delete, and the reservations each one releases.</summary>
[TestClass]
public sealed class DocumentRelocatorTests
{
	private static readonly string s_root = Path.Combine(Path.GetTempPath(), "relocator-tests");

	private static readonly FileStamp s_stampA = StoreCollaboratorsTestHost.StampOf("a");
	private static readonly FileStamp s_stampB = StoreCollaboratorsTestHost.StampOf("b");

	public required TestContext TestContext { get; set; }

	private static string PathOf(string name) => Path.Combine(s_root, name);

	[TestMethod]
	public async Task RenameAsync_MovesTheFileAndRetargetsTheDocument()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument(PathOf("old.txt"), s_stampA);
		host.FileSystem.OnMove = (_, _, _, _) => Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved, s_stampB));

		WorkspaceDocumentRenameResult result = await host
			.Relocator.RenameAsync(
				new WorkspaceDocumentRenameRequest(StoreCollaboratorsTestHost.IdentityOf(document), s_stampA, PathOf("new.txt")),
				TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(PathOf("new.txt"), document.DocumentId);
		Assert.AreEqual(s_stampB, document.OnDiskStamp);
		Assert.IsTrue(host.Locked(tracker => tracker.TryGetDocument(PathOf("new.txt"), out _)));
		Assert.IsFalse(host.Locked(tracker => tracker.TryGetDocument(PathOf("old.txt"), out _)));

		AssertReleased(host, document, PathOf("new.txt"));
	}

	[TestMethod]
	public async Task RenameAsync_ToTheDocumentsOwnId_ReportsNoChangeWithoutTakingTheGate()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument(PathOf("same.txt"), s_stampA);

		WorkspaceDocumentRenameResult result = await host
			.Relocator.RenameAsync(
				new WorkspaceDocumentRenameRequest(StoreCollaboratorsTestHost.IdentityOf(document), s_stampA, PathOf("same.txt")),
				TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.NoChange, result.Outcome);
		Assert.IsEmpty(host.FileSystem.Calls);
		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
	}

	[TestMethod]
	public async Task RenameAsync_ToATrackedDestination_ReportsDestinationInUse()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument(PathOf("a.txt"), s_stampA);
		host.AddDocument(PathOf("b.txt"), s_stampB);

		WorkspaceDocumentRenameResult result = await host
			.Relocator.RenameAsync(
				new WorkspaceDocumentRenameRequest(StoreCollaboratorsTestHost.IdentityOf(document), s_stampA, PathOf("b.txt")),
				TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.DestinationInUse, result.Outcome);
		Assert.AreEqual(PathOf("a.txt"), document.DocumentId);
	}

	[TestMethod]
	public async Task RenameAsync_OfADocumentWhoseFileIsMissing_RetargetsWithoutMovingAndKeepsTheMissingStamp()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument(PathOf("draft.txt"));

		WorkspaceDocumentRenameResult result = await host
			.Relocator.RenameAsync(
				new WorkspaceDocumentRenameRequest(StoreCollaboratorsTestHost.IdentityOf(document), FileStamp.Missing, PathOf("final.txt")),
				TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(FileStamp.Missing, document.OnDiskStamp);
		Assert.DoesNotContain(call => call.StartsWith("Move", StringComparison.Ordinal), host.FileSystem.Calls);
	}

	[TestMethod]
	public async Task RenameAsync_WhenTheMoveThrows_ReportsMoveFailedAndReleasesTheReservation()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument(PathOf("old.txt"), s_stampA);
		host.FileSystem.OnMove = (_, _, _, _) => throw new IOException("locked");

		WorkspaceDocumentRenameResult result = await host
			.Relocator.RenameAsync(
				new WorkspaceDocumentRenameRequest(StoreCollaboratorsTestHost.IdentityOf(document), s_stampA, PathOf("new.txt")),
				TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.MoveFailed, result.Outcome);
		Assert.AreEqual(PathOf("old.txt"), document.DocumentId);

		AssertReleased(host, document, PathOf("new.txt"));
	}

	[TestMethod]
	public async Task SaveAsAsync_ToANewPath_WritesTheContentAndRetargetsTheDocument()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument(PathOf("a.txt"), s_stampA);
		host.Edit(document, "changed");
		host.FileSystem.OnCaptureStamp = (path, _) => Task.FromResult(path == PathOf("a.txt") ? s_stampA : FileStamp.Missing);

		host.FileSystem.OnWrite = (_, _, _, _) =>
			Task.FromResult(new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.Replaced, s_stampB));

		WorkspaceDocumentSaveAsResult result = await host
			.Relocator.SaveAsAsync(
				new WorkspaceDocumentSaveAsRequest(StoreCollaboratorsTestHost.IdentityOf(document), s_stampA, PathOf("b.txt")),
				TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, result.Outcome);
		Assert.AreEqual(PathOf("b.txt"), document.DocumentId);
		Assert.AreEqual("changed", document.PersistedContent);
		Assert.AreEqual(s_stampB, document.OnDiskStamp);

		AssertReleased(host, document, PathOf("b.txt"));
	}

	[TestMethod]
	public async Task SaveAsAsync_InPlace_ReservesNoDestinationAndWritesOverTheSourceStamp()
	{
		var host = new StoreCollaboratorsTestHost();

		LogicalDocument document = host.AddDocument(PathOf("a.txt"), s_stampA);
		FileStamp expectedByWrite = FileStamp.Missing;

		host.FileSystem.OnCaptureStamp = (_, _) => Task.FromResult(s_stampA);

		host.FileSystem.OnWrite = (_, _, expected, _) =>
		{
			expectedByWrite = expected;
			return Task.FromResult(new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.Replaced, s_stampB));
		};

		WorkspaceDocumentSaveAsResult result = await host
			.Relocator.SaveAsAsync(
				new WorkspaceDocumentSaveAsRequest(StoreCollaboratorsTestHost.IdentityOf(document), s_stampA, PathOf("a.txt")),
				TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, result.Outcome);
		Assert.AreEqual(expectedByWrite, StoreCollaboratorsTestHost.StampOf("a"));
		Assert.AreEqual(PathOf("a.txt"), document.DocumentId);
	}

	[TestMethod]
	public async Task SaveAsAsync_ToAnExistingFile_ReportsDestinationExistsWithoutWriting()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument(PathOf("a.txt"), s_stampA);
		host.FileSystem.OnCaptureStamp = (path, _) => Task.FromResult(path == PathOf("a.txt") ? s_stampA : s_stampB);

		WorkspaceDocumentSaveAsResult result = await host
			.Relocator.SaveAsAsync(
				new WorkspaceDocumentSaveAsRequest(StoreCollaboratorsTestHost.IdentityOf(document), s_stampA, PathOf("b.txt")),
				TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.DestinationExists, result.Outcome);
		Assert.DoesNotContain(call => call.StartsWith("Write", StringComparison.Ordinal), host.FileSystem.Calls);

		AssertReleased(host, document, PathOf("b.txt"));
	}

	[TestMethod]
	public async Task DeleteAsync_RemovesTheFileAndTheDocumentAndReleasesTheDeleteReservation()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument(PathOf("a.txt"), s_stampA);

		WorkspaceDocumentDeleteResult result = await host
			.Relocator.DeleteAsync(
				new WorkspaceDocumentDeleteRequest(StoreCollaboratorsTestHost.IdentityOf(document), s_stampA),
				TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, result.Outcome);
		Assert.IsFalse(host.Locked(tracker => tracker.TryGetDocument(PathOf("a.txt"), out _)));
		Assert.IsFalse(host.Locked(tracker => tracker.IsDeleteActive(document)));
		Assert.IsEmpty(host.Stop());
	}

	[TestMethod]
	public async Task DeleteAsync_WhenTheFileSystemReportsAConflict_KeepsTheDocumentTrackedAndReleasesTheReservation()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument(PathOf("a.txt"), s_stampA);

		host.FileSystem.OnDelete = (_, _, _) =>
			Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.ExternalFileConflict, s_stampB));

		WorkspaceDocumentDeleteResult result = await host
			.Relocator.DeleteAsync(
				new WorkspaceDocumentDeleteRequest(StoreCollaboratorsTestHost.IdentityOf(document), s_stampA),
				TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.ExternalFileConflict, result.Outcome);
		Assert.IsTrue(host.Locked(tracker => tracker.TryGetDocument(PathOf("a.txt"), out _)));
		Assert.IsFalse(host.Locked(tracker => tracker.IsDeleteActive(document)));
		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
	}

	[TestMethod]
	public async Task DeleteAsync_WhenTheFileSystemThrows_ReportsDeleteFailed()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument(PathOf("a.txt"), s_stampA);
		host.FileSystem.OnDelete = (_, _, _) => throw new IOException("busy");

		WorkspaceDocumentDeleteResult result = await host
			.Relocator.DeleteAsync(
				new WorkspaceDocumentDeleteRequest(StoreCollaboratorsTestHost.IdentityOf(document), s_stampA),
				TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.DeleteFailed, result.Outcome);
		Assert.IsFalse(host.Locked(tracker => tracker.IsDeleteActive(document)));
	}

	// Asserts that an exit released what the operation published: the gate, the destination reservation and the registration.
	private static void AssertReleased(StoreCollaboratorsTestHost host, LogicalDocument document, string destinationId)
	{
		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
		Assert.IsFalse(host.Locked(tracker => tracker.IsDestinationUnavailable(destinationId)));
		Assert.IsEmpty(host.Stop());
	}
}
