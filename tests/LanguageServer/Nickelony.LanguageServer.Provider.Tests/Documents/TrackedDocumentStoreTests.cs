namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class TrackedDocumentStoreTests
{
	[TestMethod]
	public void Synchronize_RepeatedOpenCallsRequireMatchingCloseCalls()
	{
		const string filePath = @"C:\Workspace\Scripts\repeated-open.ext";
		var store = new TestTrackedDocumentStore();

		Assert.IsNotNull(store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open));
		Assert.IsNull(store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open));

		Assert.AreEqual(DocumentCloseOutcome.StillOpen, store.TryClose(filePath, out DocumentSnapshot? firstClose));
		Assert.IsNull(firstClose);
		Assert.AreEqual(DocumentCloseOutcome.Closed, store.TryClose(filePath, out DocumentSnapshot? finalClose));
		Assert.IsNotNull(finalClose);
		Assert.AreEqual(DocumentCloseOutcome.Untracked, store.TryClose(filePath, out DocumentSnapshot? repeatedClose));
		Assert.IsNull(repeatedClose);
	}

	[TestMethod]
	public void Synchronize_WithoutReferencesCreatesTrackedDocument()
	{
		const string filePath = @"C:\Workspace\Scripts\idle.ext";

		var store = new TestTrackedDocumentStore();
		DocumentSynchronizationRequest? request = store.Synchronize(filePath, "return 1");

		Assert.IsNotNull(request);
		Assert.AreEqual(DocumentSynchronizationKind.Open, request.Value.Kind);
		Assert.IsNotNull(store.GetDocumentSnapshot(filePath));
		Assert.AreEqual(DocumentCloseOutcome.Closed, store.TryClose(filePath, out DocumentSnapshot? closingDocument));
		Assert.IsNotNull(closingDocument);
		Assert.IsNull(store.GetDocumentSnapshot(filePath));
	}

	[TestMethod]
	public void Synchronize_AfterCloseCreatesFreshStateWithNewContent()
	{
		const string filePath = @"C:\Workspace\Scripts\after-close.ext";

		var store = new TestTrackedDocumentStore();
		store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open);

		Assert.AreEqual(DocumentCloseOutcome.Closed, store.TryClose(filePath, out _));

		DocumentSynchronizationRequest? reopenRequest = store.Synchronize(filePath, "return 2");

		Assert.IsNotNull(reopenRequest);
		Assert.AreEqual(DocumentSynchronizationKind.Open, reopenRequest.Value.Kind);
		Assert.AreEqual("return 2", reopenRequest.Value.Document.Content);
		Assert.AreEqual(1, reopenRequest.Value.Document.Version);
	}

	[TestMethod]
	public void Synchronize_WithChangedContent_ReportsIncrementalChangeRange()
	{
		const string filePath = @"C:\Workspace\Scripts\incremental.ext";

		var store = new TestTrackedDocumentStore();
		store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open);

		DocumentSynchronizationRequest? changeRequest = store.Synchronize(filePath, "return 2", references: DocumentReferenceAcquisition.Open);

		Assert.IsNotNull(changeRequest);
		Assert.AreEqual(DocumentSynchronizationKind.Change, changeRequest.Value.Kind);
		Assert.IsNotNull(changeRequest.Value.ChangeRange);

		DocumentChangeRange changeRange = changeRequest.Value.ChangeRange!.Value;
		Assert.AreEqual("2", changeRange.Text);
		Assert.AreEqual(0, changeRange.StartLine);
		Assert.AreEqual(7, changeRange.StartCharacter);
		Assert.AreEqual(0, changeRange.EndLine);
		Assert.AreEqual(8, changeRange.EndCharacter);
		Assert.AreEqual("return 2", changeRequest.Value.Document.Content);
	}

	[TestMethod]
	public void Synchronize_WithoutChangeRange_ReportsChangeWithoutRange()
	{
		const string filePath = @"C:\Workspace\Scripts\no-range.ext";

		var store = new TestTrackedDocumentStore();
		store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open);

		DocumentSynchronizationRequest? changeRequest = store.Synchronize(filePath, "return 2", includeChangeRange: false);

		Assert.IsNotNull(changeRequest);
		Assert.AreEqual(DocumentSynchronizationKind.Change, changeRequest.Value.Kind);
		Assert.IsNull(changeRequest.Value.ChangeRange);
		Assert.AreEqual("return 2", changeRequest.Value.Document.Content);
	}

	[TestMethod]
	public void Rename_UnknownSourceDoesNotCreateDestinationState()
	{
		const string oldFilePath = @"C:\Workspace\Scripts\missing.ext";
		const string newFilePath = @"C:\Workspace\Scripts\created.ext";

		var store = new TestTrackedDocumentStore();

		Assert.AreEqual(DocumentRenameOutcome.SourceUntracked, store.Rename(oldFilePath, newFilePath, "return 1").Outcome);
		Assert.IsNull(store.GetDocumentSnapshot(oldFilePath));
		Assert.IsNull(store.GetDocumentSnapshot(newFilePath));
	}

	[TestMethod]
	public void Rename_PreservesTrackedContentAndReferences()
	{
		const string oldFilePath = @"C:\Workspace\Scripts\before.ext";
		const string newFilePath = @"C:\Workspace\Scripts\after.ext";

		var store = new TestTrackedDocumentStore();
		var requestReference = new DocumentRequestReference();

		store.Synchronize(oldFilePath, "return 1", references: DocumentReferenceAcquisition.Open);
		store.Synchronize(
			oldFilePath,
			"return 1",
			references: DocumentReferenceAcquisition.Open | DocumentReferenceAcquisition.Request,
			requestReference: requestReference);

		DocumentRenameResult result = store.Rename(oldFilePath, newFilePath, "return 1");

		Assert.AreEqual(DocumentRenameOutcome.Renamed, result.Outcome);
		Assert.IsNotNull(result.Request);

		DocumentRenameRequest renameRequest = result.Request!.Value;

		Assert.IsTrue(renameRequest.ReopenServerDocument);
		Assert.AreEqual("return 1", renameRequest.RenamedDocument.Content);
		Assert.AreEqual(1, renameRequest.RenamedDocument.Version);
		Assert.IsNull(store.GetDocumentSnapshot(oldFilePath));
		Assert.IsNotNull(store.GetDocumentSnapshot(newFilePath));

		Assert.AreEqual(DocumentCloseOutcome.StillOpen, store.TryClose(newFilePath, out _));
		Assert.AreEqual(DocumentCloseOutcome.BusyWithRequests, store.TryClose(newFilePath, out _));

		// The identity-bound release drains the request reference on the renamed record; the idle record is then
		// removed by the explicit close.
		store.ReleaseRequestByReference(requestReference);

		Assert.AreEqual(DocumentCloseOutcome.Closed, store.TryClose(newFilePath, out DocumentSnapshot? finalDocument));
		Assert.IsNotNull(finalDocument);
		Assert.IsNull(store.GetDocumentSnapshot(newFilePath));
	}

	[TestMethod]
	public void ReleaseRequestByReference_FindsTheRecordAfterARename()
	{
		const string oldFilePath = @"C:\Workspace\Scripts\before-release.ext";
		const string newFilePath = @"C:\Workspace\Scripts\after-release.ext";

		var store = new TestTrackedDocumentStore();
		var requestReference = new DocumentRequestReference();

		store.Synchronize(
			oldFilePath,
			"return 1",
			references: DocumentReferenceAcquisition.Open | DocumentReferenceAcquisition.Request,
			requestReference: requestReference);

		Assert.IsTrue(requestReference.IsAcquired);
		Assert.AreEqual(LanguageServerPaths.NormalizeLocalPath(oldFilePath), requestReference.CurrentFilePath);

		DocumentRenameResult result = store.Rename(oldFilePath, newFilePath, "return 1");

		Assert.AreEqual(DocumentRenameOutcome.Renamed, result.Outcome);
		Assert.IsNotNull(result.Request);
		Assert.AreEqual(LanguageServerPaths.NormalizeLocalPath(newFilePath), requestReference.CurrentFilePath);

		// The identity-bound release drains the reference on the renamed record, so the record becomes closable.
		store.ReleaseRequestByReference(requestReference);

		Assert.AreEqual(DocumentCloseOutcome.Closed, store.TryClose(newFilePath, out DocumentSnapshot? closingDocument));
		Assert.IsNotNull(closingDocument);

		// Releasing the same reference again is a no-op rather than consuming another reference.
		store.ReleaseRequestByReference(requestReference);
		Assert.AreEqual(DocumentCloseOutcome.Untracked, store.TryClose(newFilePath, out _));
	}

	[TestMethod]
	public void Synchronize_WithAReusedRequestReference_ThrowsInsteadOfLosingTheReleaseHandle()
	{
		const string filePath = @"C:\Workspace\Scripts\reused-reference.ext";

		var store = new TestTrackedDocumentStore();
		var requestReference = new DocumentRequestReference();

		store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Request, requestReference: requestReference);
		store.ReleaseRequestByReference(requestReference);

		// The single-use reference cannot acquire a second reference; the failed call must not leak the
		// first acquisition.
		Assert.ThrowsExactly<InvalidOperationException>(() =>
			store.Synchronize(filePath, "return 2", references: DocumentReferenceAcquisition.Request, requestReference: requestReference));

		Assert.AreEqual(DocumentCloseOutcome.Closed, store.TryClose(filePath, out _));
	}

	[TestMethod]
	public void Synchronize_WithAReferenceButNoAcquisition_Throws()
	{
		const string filePath = @"C:\Workspace\Scripts\reference-without-acquisition.ext";

		var store = new TestTrackedDocumentStore();
		var requestReference = new DocumentRequestReference();

		Assert.ThrowsExactly<ArgumentException>(() =>
			store.Synchronize(filePath, "return 1", requestReference: requestReference));
		Assert.IsFalse(requestReference.IsAcquired);
	}

	[TestMethod]
	public void Rename_WhenDestinationIsAlreadyTracked_ReportsDestinationTrackedAndPreservesTrackedDocuments()
	{
		const string oldFilePath = @"C:\Workspace\Scripts\source.ext";
		const string newFilePath = @"C:\Workspace\Scripts\target.ext";

		var store = new TestTrackedDocumentStore();
		store.Synchronize(oldFilePath, "return 1", references: DocumentReferenceAcquisition.Open);
		store.Synchronize(newFilePath, "return 2", references: DocumentReferenceAcquisition.Open);

		DocumentRenameResult result = store.Rename(oldFilePath, newFilePath, "return 1");

		Assert.AreEqual(DocumentRenameOutcome.DestinationTracked, result.Outcome);
		Assert.IsNull(result.Request);
		Assert.IsNotNull(store.GetDocumentSnapshot(oldFilePath));

		DocumentSnapshot? destinationDocument = store.GetDocumentSnapshot(newFilePath);

		Assert.IsNotNull(destinationDocument);
		Assert.AreEqual("return 2", destinationDocument.Content);
		Assert.AreEqual(1, destinationDocument.Version);
		Assert.AreEqual(DocumentCloseOutcome.Closed, store.TryClose(oldFilePath, out _));
		Assert.AreEqual(DocumentCloseOutcome.Closed, store.TryClose(newFilePath, out DocumentSnapshot? closedDestinationDocument));
		Assert.IsNotNull(closedDestinationDocument);
		Assert.AreEqual("return 2", closedDestinationDocument.Content);
	}

	[TestMethod]
	public void Rename_PathCaseOnlyDifference_FollowsPlatformPathSensitivity()
	{
		string directoryPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TrackedDocumentStoreTests"));
		string originalFilePath = LanguageServerPaths.NormalizeLocalPath(Path.Combine(directoryPath, "test.ext"));
		string renamedFilePath = LanguageServerPaths.NormalizeLocalPath(Path.Combine(directoryPath, "TEST.ext"));

		var store = new TestTrackedDocumentStore();
		store.Synchronize(originalFilePath, "return 1", references: DocumentReferenceAcquisition.Open);

		DocumentRenameResult result = store.Rename(originalFilePath, renamedFilePath, "return 1");

		if (LanguageServerPaths.UsesCaseSensitiveLocalPaths)
		{
			Assert.AreEqual(DocumentRenameOutcome.Renamed, result.Outcome);
			Assert.IsNotNull(result.Request);
			Assert.IsNull(store.GetDocumentSnapshot(originalFilePath));
			Assert.IsNotNull(store.GetDocumentSnapshot(renamedFilePath));
		}
		else
		{
			Assert.AreEqual(DocumentRenameOutcome.PathsEquivalent, result.Outcome);
			Assert.IsNull(result.Request);
			Assert.IsNotNull(store.GetDocumentSnapshot(originalFilePath));

			DocumentSnapshot? aliasedDocument = store.GetDocumentSnapshot(renamedFilePath);

			Assert.IsNotNull(aliasedDocument);
			Assert.AreEqual(originalFilePath, aliasedDocument.FilePath);
		}
	}

	[TestMethod]
	public void Synchronize_AndLookup_NormalizeEquivalentPaths()
	{
		string canonicalFilePath = Path.Combine(Path.GetTempPath(), "TrackedDocumentStoreTests", "scripts", "test.ext");

		string directoryPath = Path.GetDirectoryName(canonicalFilePath)
			?? throw new InvalidOperationException("The canonical test file path did not have a directory.");

		string aliasedFilePath = Path.Combine(directoryPath, ".", Path.GetFileName(canonicalFilePath));

		var store = new TestTrackedDocumentStore();
		DocumentSynchronizationRequest? synchronizationRequest = store.Synchronize(canonicalFilePath, "return 1", references: DocumentReferenceAcquisition.Open);

		Assert.IsNotNull(synchronizationRequest);
		Assert.AreEqual(LanguageServerPaths.NormalizeLocalPath(canonicalFilePath), synchronizationRequest.Value.Document.FilePath);

		DocumentSnapshot? snapshot = store.GetDocumentSnapshot(aliasedFilePath);

		Assert.IsNotNull(snapshot);
		Assert.AreEqual(LanguageServerPaths.NormalizeLocalPath(canonicalFilePath), snapshot.FilePath);
		Assert.AreEqual(DocumentCloseOutcome.Closed, store.TryClose(aliasedFilePath, out DocumentSnapshot? closedDocument));
		Assert.IsNotNull(closedDocument);
		Assert.IsNull(store.GetDocumentSnapshot(canonicalFilePath));
	}

	[TestMethod]
	public void TryClose_RemovesTrackedDocumentWhileRestartReplayIsPending()
	{
		const string filePath = @"C:\Workspace\Scripts\pending.ext";

		var store = new TestTrackedDocumentStore();
		store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open);

		IReadOnlyList<DocumentSnapshot> documentsToReopen = store.PrepareForRestart();

		Assert.AreEqual(1, documentsToReopen.Count);
		Assert.AreEqual(DocumentCloseOutcome.Closed, store.TryClose(filePath, out DocumentSnapshot? closingDocument));
		Assert.IsNull(closingDocument);
		Assert.IsNull(store.GetDocumentSnapshot(filePath));
	}

	[TestMethod]
	public void TryClose_PreservesRequestOwnedTrackedDocumentUntilRequestRelease()
	{
		const string filePath = @"C:\Workspace\Scripts\request-owned.ext";

		var store = new TestTrackedDocumentStore();
		var requestReference = new DocumentRequestReference();

		store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open);
		store.Synchronize(
			filePath,
			"return 1",
			references: DocumentReferenceAcquisition.Request,
			requestReference: requestReference);

		Assert.AreEqual(DocumentCloseOutcome.BusyWithRequests, store.TryClose(filePath, out DocumentSnapshot? closingDocument));
		Assert.IsNull(closingDocument);
		Assert.IsNotNull(store.GetDocumentSnapshot(filePath));

		store.ReleaseRequestByReference(requestReference);

		Assert.AreEqual(DocumentCloseOutcome.Closed, store.TryClose(filePath, out DocumentSnapshot? closedDocument));
		Assert.IsNotNull(closedDocument);
		Assert.IsNull(store.GetDocumentSnapshot(filePath));
	}

	[TestMethod]
	public void Synchronize_ReopensTrackedDocumentAfterRestartPreparation()
	{
		const string filePath = @"C:\Workspace\Scripts\reopen.ext";

		var store = new TestTrackedDocumentStore();
		DocumentSynchronizationRequest? initialRequest = store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open);

		Assert.IsNotNull(initialRequest);
		Assert.AreEqual(DocumentSynchronizationKind.Open, initialRequest.Value.Kind);

		IReadOnlyList<DocumentSnapshot> documentsToReopen = store.PrepareForRestart();
		DocumentSynchronizationRequest? reopenRequest = store.Synchronize(filePath, "return 2", references: DocumentReferenceAcquisition.Open);

		Assert.AreEqual(1, documentsToReopen.Count);
		Assert.AreEqual(filePath, documentsToReopen[0].FilePath);
		Assert.IsNotNull(reopenRequest);
		Assert.AreEqual(DocumentSynchronizationKind.Open, reopenRequest.Value.Kind);
		Assert.AreEqual(filePath, reopenRequest.Value.Document.FilePath);
		Assert.AreEqual("return 2", reopenRequest.Value.Document.Content);
		Assert.AreEqual(2, reopenRequest.Value.Document.Version);

		DocumentSnapshot? reopenedDocument = store.GetDocumentSnapshot(filePath);

		Assert.IsNotNull(reopenedDocument);
		Assert.AreEqual("return 2", reopenedDocument.Content);
		Assert.AreEqual(2, reopenedDocument.Version);
		Assert.AreEqual(DocumentCloseOutcome.StillOpen, store.TryClose(filePath, out _));
		Assert.AreEqual(DocumentCloseOutcome.Closed, store.TryClose(filePath, out DocumentSnapshot? finalClosedDocument));
		Assert.IsNotNull(finalClosedDocument);
	}

	[TestMethod]
	public void TrimIdleDocuments_RemovesOldestIdleDocuments()
	{
		const string firstFilePath = @"C:\Workspace\Scripts\first.ext";
		const string secondFilePath = @"C:\Workspace\Scripts\second.ext";
		const string thirdFilePath = @"C:\Workspace\Scripts\third.ext";

		var store = new TestTrackedDocumentStore();

		// A synchronization without references leaves an idle server-open record behind, which is exactly the state
		// the trim evicts once a newer document pushes it out.
		store.Synchronize(firstFilePath, "return 1");
		store.Synchronize(secondFilePath, "return 2");
		store.Synchronize(thirdFilePath, "return 3");

		IReadOnlyList<DocumentSnapshot> trimmedDocuments = store.TrimIdleDocuments(1);

		Assert.AreEqual(2, trimmedDocuments.Count);

		CollectionAssert.AreEquivalent(
			new[] { firstFilePath, secondFilePath },
			new[] { trimmedDocuments[0].FilePath, trimmedDocuments[1].FilePath });

		Assert.IsNull(store.GetDocumentSnapshot(firstFilePath));
		Assert.IsNull(store.GetDocumentSnapshot(secondFilePath));
		Assert.IsNotNull(store.GetDocumentSnapshot(thirdFilePath));
		Assert.AreEqual(0, store.TrimIdleDocuments(1).Count);
	}

	[TestMethod]
	public void TrimIdleDocuments_NegativeMaxCount_ThrowsArgumentOutOfRangeException()
	{
		var store = new TestTrackedDocumentStore();
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => store.TrimIdleDocuments(-1));
	}

	[TestMethod]
	public void Rename_SamePathWithNewContent_LeavesTrackedContentEqualToTheServerCopy()
	{
		string directoryPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TrackedDocumentStoreTests"));
		string originalFilePath = LanguageServerPaths.NormalizeLocalPath(Path.Combine(directoryPath, "test.ext"));
		string renamedFilePath = LanguageServerPaths.NormalizeLocalPath(Path.Combine(directoryPath, "TEST.ext"));

		var store = new TestTrackedDocumentStore();
		store.Synchronize(originalFilePath, "return 1", references: DocumentReferenceAcquisition.Open);

		DocumentRenameResult result = store.Rename(originalFilePath, renamedFilePath, "return 2");

		if (LanguageServerPaths.UsesCaseSensitiveLocalPaths)
		{
			Assert.AreEqual(DocumentRenameOutcome.Renamed, result.Outcome);
			Assert.IsNotNull(result.Request);
			Assert.AreEqual("return 2", result.Request!.Value.RenamedDocument.Content);
			return;
		}

		// A same-file rename mirrors nothing to the server, so the supplied content must not advance the tracked
		// record: the tracked text stays equal to the text the server received.
		Assert.AreEqual(DocumentRenameOutcome.PathsEquivalent, result.Outcome);
		Assert.IsNull(result.Request);

		DocumentSnapshot? trackedDocument = store.GetDocumentSnapshot(originalFilePath);

		Assert.IsNotNull(trackedDocument);
		Assert.AreEqual("return 1", trackedDocument.Content);
		Assert.AreEqual(1, trackedDocument.Version);

		// A later synchronization still computes its incremental range against the content the server actually holds.
		DocumentSynchronizationRequest? changeRequest = store.Synchronize(originalFilePath, "return 2", includeChangeRange: true);

		Assert.IsNotNull(changeRequest);
		Assert.AreEqual(DocumentSynchronizationKind.Change, changeRequest.Value.Kind);
		Assert.IsNotNull(changeRequest.Value.ChangeRange);
		Assert.AreEqual("return 2", changeRequest.Value.Document.Content);
		Assert.AreEqual(2, changeRequest.Value.Document.Version);
	}

	[TestMethod]
	public void TryReopenTrackedDocument_ReopensTrackedDocumentAfterRestartPreparation()
	{
		const string filePath = @"C:\Workspace\Scripts\guarded-reopen.ext";

		var store = new TestTrackedDocumentStore();
		store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open);
		store.PrepareForRestart();

		DocumentSynchronizationRequest? reopenRequest = store.TryReopenTrackedDocument(filePath);

		Assert.IsNotNull(reopenRequest);
		Assert.AreEqual(DocumentSynchronizationKind.Open, reopenRequest.Value.Kind);
		Assert.AreEqual("return 1", reopenRequest.Value.Document.Content);

		DocumentSnapshot? reopenedDocument = store.GetDocumentSnapshot(filePath);

		Assert.IsNotNull(reopenedDocument);
	}

	[TestMethod]
	public void TryReopenTrackedDocument_WhenDocumentWasClosedDuringRestart_ReturnsNullAndKeepsItUntracked()
	{
		const string filePath = @"C:\Workspace\Scripts\closed-during-restart.ext";

		var store = new TestTrackedDocumentStore();
		store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open);
		store.PrepareForRestart();

		Assert.AreEqual(DocumentCloseOutcome.Closed, store.TryClose(filePath, out _));

		Assert.IsNull(store.TryReopenTrackedDocument(filePath));
		Assert.IsNull(store.GetDocumentSnapshot(filePath));
	}

	[TestMethod]
	public void TryReopenTrackedDocument_WhenDocumentWasAlreadyReopenedDuringRestart_ReturnsNull()
	{
		const string filePath = @"C:\Workspace\Scripts\already-reopened.ext";

		var store = new TestTrackedDocumentStore();
		store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open);
		store.PrepareForRestart();

		DocumentSynchronizationRequest? concurrentReopenRequest = store.Synchronize(filePath, "return 2", references: DocumentReferenceAcquisition.Open);

		Assert.IsNotNull(concurrentReopenRequest);
		Assert.AreEqual(DocumentSynchronizationKind.Open, concurrentReopenRequest.Value.Kind);

		Assert.IsNull(store.TryReopenTrackedDocument(filePath));

		DocumentSnapshot? trackedDocument = store.GetDocumentSnapshot(filePath);

		Assert.IsNotNull(trackedDocument);
		Assert.AreEqual("return 2", trackedDocument.Content);
	}

	[TestMethod]
	public void TryReopenTrackedDocument_WhenDocumentIsUntracked_DoesNotCreateRecord()
	{
		const string trackedFilePath = @"C:\Workspace\Scripts\tracked.ext";
		const string untrackedFilePath = @"C:\Workspace\Scripts\untracked.ext";

		var store = new TestTrackedDocumentStore();
		store.Synchronize(trackedFilePath, "return 1", references: DocumentReferenceAcquisition.Open);
		store.PrepareForRestart();

		Assert.IsNull(store.TryReopenTrackedDocument(untrackedFilePath));
		Assert.IsNull(store.GetDocumentSnapshot(untrackedFilePath));
	}
}
