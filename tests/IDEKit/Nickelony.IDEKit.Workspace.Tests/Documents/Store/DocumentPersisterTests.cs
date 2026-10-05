using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>Covers the persister directly: what a commit writes and installs, what a reload adopts, and how a conflict resolves.</summary>
[TestClass]
public sealed class DocumentPersisterTests
{
	private static readonly FileStamp s_stampA = StoreCollaboratorsTestHost.StampOf("a");
	private static readonly FileStamp s_stampB = StoreCollaboratorsTestHost.StampOf("b");

	public required TestContext TestContext { get; set; }

	private static WorkspaceDocumentCommitRequest CommitOf(LogicalDocument document, FileStamp expected) =>
		new(StoreCollaboratorsTestHost.IdentityOf(document), expected);

	[TestMethod]
	public async Task CommitAsync_ForADirtyDocument_WritesTheContentAndInstallsTheBaseline()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc", s_stampA);
		host.Edit(document, "changed");

		host.FileSystem.OnWrite = (_, _, _, _) =>
			Task.FromResult(new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.Replaced, s_stampB));

		WorkspaceDocumentCommitResult result = await host
			.Persister.CommitAsync(CommitOf(document, s_stampA), forceWrite: false, TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, result.Outcome);
		Assert.AreEqual("changed", document.PersistedContent);
		Assert.AreEqual(s_stampB, document.OnDiskStamp);
		Assert.IsFalse(document.IsDirty);
		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
	}

	[TestMethod]
	public async Task CommitAsync_ForACleanDocumentWhoseFileExists_WritesNothing()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc", s_stampA);

		WorkspaceDocumentCommitResult result = await host
			.Persister.CommitAsync(CommitOf(document, s_stampA), forceWrite: false, TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, result.Outcome);
		Assert.IsEmpty(host.FileSystem.Calls);
	}

	[TestMethod]
	public async Task CommitAsync_ForACleanDocument_ReportsAConflictWhenTheExpectedStampDiffers()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc", s_stampA);

		WorkspaceDocumentCommitResult result = await host
			.Persister.CommitAsync(CommitOf(document, s_stampB), forceWrite: false, TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(s_stampA, result.ObservedOnDiskStamp);
		Assert.IsEmpty(host.FileSystem.Calls);
	}

	[TestMethod]
	public async Task CommitAsync_WithForceWrite_WritesACleanDocument()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc", s_stampA);

		host.FileSystem.OnWrite = (_, _, _, _) =>
			Task.FromResult(new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.Replaced, s_stampB));

		WorkspaceDocumentCommitResult result = await host
			.Persister.CommitAsync(CommitOf(document, s_stampA), forceWrite: true, TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, result.Outcome);
		Assert.HasCount(1, host.FileSystem.Calls);
	}

	[TestMethod]
	public async Task CommitAsync_WhenTheWriteThrows_ReportsWriteFailedAndReleasesTheGate()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc", s_stampA);
		host.Edit(document, "changed");
		host.FileSystem.OnWrite = (_, _, _, _) => throw new IOException("disk full");

		WorkspaceDocumentCommitResult result = await host
			.Persister.CommitAsync(CommitOf(document, s_stampA), forceWrite: false, TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.WriteFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.WriteFailed, result.Failure?.Code);
		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
		Assert.IsEmpty(host.Stop());
	}

	[TestMethod]
	public async Task ReloadAsync_ForACleanDocument_AdoptsTheDiskContent()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc", s_stampA);
		host.FileSystem.OnRead = (_, _) => Task.FromResult(WorkspaceFileReadResult.FromContent("disk", TestSnapshots.FileFormat, s_stampB));

		WorkspaceDocumentReloadResult result = await host
			.Persister.ReloadAsync(new WorkspaceDocumentReloadRequest(StoreCollaboratorsTestHost.IdentityOf(document)), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, result.Outcome);
		Assert.AreEqual("disk", document.Content);
		Assert.AreEqual("disk", document.PersistedContent);
		Assert.AreEqual(s_stampB, document.OnDiskStamp);
		Assert.AreEqual(1, document.Version);
		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
	}

	[TestMethod]
	public async Task ReloadAsync_ForACleanDocumentWithTheTrackedStamp_ReportsUnchanged()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc", s_stampA);
		host.FileSystem.OnRead = (_, _) => Task.FromResult(WorkspaceFileReadResult.FromContent("text", TestSnapshots.FileFormat, s_stampA));

		WorkspaceDocumentReloadResult result = await host
			.Persister.ReloadAsync(new WorkspaceDocumentReloadRequest(StoreCollaboratorsTestHost.IdentityOf(document)), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Unchanged, result.Outcome);
		Assert.AreEqual(0, document.Version);
	}

	[TestMethod]
	public async Task ReloadAsync_ForADirtyDocument_HoldsTheGateDuringTheCaptureAndReportsTheConflictWithoutAdopting()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc", s_stampA);
		host.Edit(document, "changed");
		int gateCountDuringCapture = -1;

		host.FileSystem.OnCaptureStamp = (_, _) =>
		{
			gateCountDuringCapture = document.DiskOperationGate.CurrentCount;
			return Task.FromResult(s_stampB);
		};

		WorkspaceDocumentReloadResult result = await host
			.Persister.ReloadAsync(new WorkspaceDocumentReloadRequest(StoreCollaboratorsTestHost.IdentityOf(document)), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(s_stampB, result.ObservedOnDiskStamp);
		Assert.AreEqual(0, gateCountDuringCapture);
		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
		Assert.AreEqual("changed", document.Content);
		Assert.AreEqual(s_stampA, document.OnDiskStamp);
		Assert.IsEmpty(host.Stop());
	}

	[TestMethod]
	public async Task ReloadAsync_ForADirtyDocumentWhoseGateIsHeld_ReportsOperationInProgressWithoutCapturing()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc", s_stampA);
		host.Edit(document, "changed");

		Assert.IsTrue(document.DiskOperationGate.Wait(0));

		WorkspaceDocumentReloadResult result = await host
			.Persister.ReloadAsync(new WorkspaceDocumentReloadRequest(StoreCollaboratorsTestHost.IdentityOf(document)), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.OperationInProgress, result.Outcome);
		Assert.IsEmpty(host.FileSystem.Calls);
		Assert.AreEqual(0, document.DiskOperationGate.CurrentCount);
	}

	[TestMethod]
	public async Task ResolveExternalConflictAsync_WithDisk_AdoptsTheContentOfTheObservedStamp()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc", s_stampA);
		host.Edit(document, "changed");
		host.FileSystem.OnRead = (_, _) => Task.FromResult(WorkspaceFileReadResult.FromContent("disk", TestSnapshots.FileFormat, s_stampB));

		var request = new WorkspaceDocumentConflictResolutionRequest(
			StoreCollaboratorsTestHost.IdentityOf(document),
			s_stampB,
			WorkspaceDocumentConflictResolutionMode.ResolveWithDisk
		);

		WorkspaceDocumentConflictResolutionResult result = await host
			.Persister.ResolveExternalConflictAsync(request, TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk, result.Outcome);
		Assert.AreEqual("disk", document.Content);
		Assert.IsFalse(document.IsDirty);
	}

	[TestMethod]
	public async Task ResolveExternalConflictAsync_WithDiskThatChangedAgain_ReportsTheConflictAndKeepsTheContent()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc", s_stampA);
		host.Edit(document, "changed");
		host.FileSystem.OnRead = (_, _) => Task.FromResult(WorkspaceFileReadResult.FromContent("disk", TestSnapshots.FileFormat, s_stampB));

		var request = new WorkspaceDocumentConflictResolutionRequest(
			StoreCollaboratorsTestHost.IdentityOf(document),
			s_stampA,
			WorkspaceDocumentConflictResolutionMode.ResolveWithDisk
		);

		WorkspaceDocumentConflictResolutionResult result = await host
			.Persister.ResolveExternalConflictAsync(request, TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual("changed", document.Content);
	}

	[TestMethod]
	public async Task ResolveExternalConflictAsync_WithLogical_WritesTheContentOverTheDisk()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc", s_stampA);
		host.Edit(document, "changed");

		host.FileSystem.OnWrite = (_, _, _, _) =>
			Task.FromResult(new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.Replaced, s_stampB));

		var request = new WorkspaceDocumentConflictResolutionRequest(
			StoreCollaboratorsTestHost.IdentityOf(document),
			s_stampA,
			WorkspaceDocumentConflictResolutionMode.ResolveWithLogical
		);

		WorkspaceDocumentConflictResolutionResult result = await host
			.Persister.ResolveExternalConflictAsync(request, TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical, result.Outcome);
		Assert.AreEqual(s_stampB, document.OnDiskStamp);
	}

	[TestMethod]
	public async Task ResolveExternalConflictAsync_WithAnUndefinedChoice_ThrowsBeforeTouchingTheDocument()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc", s_stampA);
		host.Edit(document, "changed");

		var request = new WorkspaceDocumentConflictResolutionRequest(
			StoreCollaboratorsTestHost.IdentityOf(document),
			s_stampA,
			(WorkspaceDocumentConflictResolutionMode)99
		);

		await Assert
			.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
				host.Persister.ResolveExternalConflictAsync(request, TestContext.CancellationToken)
			)
			.ConfigureAwait(false);

		Assert.AreEqual("changed", document.Content);
		Assert.IsEmpty(host.FileSystem.Calls);
	}
}
