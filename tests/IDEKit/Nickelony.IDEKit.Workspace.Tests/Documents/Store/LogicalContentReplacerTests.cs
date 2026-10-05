using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>Covers the replacer directly: the content swap, the identity checks and the refusals while a file operation holds the document.</summary>
[TestClass]
public sealed class LogicalContentReplacerTests
{
	private static WorkspaceDocumentReplaceRequest ReplaceOf(LogicalDocument document, string content) =>
		new(StoreCollaboratorsTestHost.IdentityOf(document), content, TestSnapshots.FileFormat);

	[TestMethod]
	public void Replace_WithNewContent_ChangesTheDocumentAndAdvancesItsVersion()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc");

		WorkspaceDocumentMutationResult result = host.Replacer.Replace(ReplaceOf(document, "changed"));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, result.Outcome);
		Assert.AreEqual("changed", document.Content);
		Assert.AreEqual(1, document.Version);
		Assert.IsTrue(document.IsDirty);
	}

	[TestMethod]
	public void Replace_WithTheSameContentAndFormat_ReportsNoChange()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc");

		WorkspaceDocumentMutationResult result = host.Replacer.Replace(ReplaceOf(document, document.Content));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.NoChange, result.Outcome);
		Assert.AreEqual(0, document.Version);
	}

	[TestMethod]
	public void Replace_ForAnUnknownDocument_ReportsDocumentNotFound()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc");
		var identity = new WorkspaceDocumentRequestIdentity(document.DocumentKey, "other", 0);

		WorkspaceDocumentMutationResult result = host.Replacer.Replace(new WorkspaceDocumentReplaceRequest(identity, "x", TestSnapshots.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.DocumentNotFound, result.Outcome);
	}

	[TestMethod]
	public void Replace_AgainstAnOutdatedVersion_ReportsStaleDocument()
	{
		var host = new StoreCollaboratorsTestHost();

		LogicalDocument document = host.AddDocument("doc");
		WorkspaceDocumentReplaceRequest request = ReplaceOf(document, "first");

		host.Replacer.Replace(request);

		WorkspaceDocumentMutationResult result = host.Replacer.Replace(request);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.StaleDocument, result.Outcome);
	}

	[TestMethod]
	public void Replace_WhileADeleteIsInFlight_ReportsOperationInProgress()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc");
		host.Locked(tracker => tracker.ReserveDelete(document));

		WorkspaceDocumentMutationResult result = host.Replacer.Replace(ReplaceOf(document, "changed"));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.OperationInProgress, result.Outcome);
		Assert.AreEqual("content", document.Content);
	}

	[TestMethod]
	public void Discard_OfADirtyDocument_RestoresThePersistedContentAsTheNewBaseline()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc");
		host.Edit(document, "changed");

		WorkspaceDocumentMutationResult result = host.Replacer.Discard(
			new WorkspaceDocumentDiscardRequest(StoreCollaboratorsTestHost.IdentityOf(document))
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, result.Outcome);
		Assert.AreEqual("content", document.Content);
		Assert.AreEqual(document.Version, document.PersistedVersion);
		Assert.IsFalse(document.IsDirty);
	}

	[TestMethod]
	public void Discard_OfACleanDocument_ReportsNoChange()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc");

		WorkspaceDocumentMutationResult result = host.Replacer.Discard(
			new WorkspaceDocumentDiscardRequest(StoreCollaboratorsTestHost.IdentityOf(document))
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.NoChange, result.Outcome);
	}

	[TestMethod]
	public void Discard_WhileAFileOperationHoldsTheGate_ReportsOperationInProgress()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc");
		host.Edit(document, "changed");
		Assert.IsTrue(document.DiskOperationGate.Wait(0, CancellationToken.None));

		WorkspaceDocumentMutationResult result = host.Replacer.Discard(
			new WorkspaceDocumentDiscardRequest(StoreCollaboratorsTestHost.IdentityOf(document))
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.OperationInProgress, result.Outcome);
		Assert.AreEqual("changed", document.Content);
	}

	[TestMethod]
	public void Discard_AfterTheStopBegan_ThrowsObjectDisposed()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc");
		host.Stop();

		Assert.ThrowsExactly<ObjectDisposedException>(() =>
			host.Replacer.Discard(new WorkspaceDocumentDiscardRequest(StoreCollaboratorsTestHost.IdentityOf(document)))
		);
	}
}
