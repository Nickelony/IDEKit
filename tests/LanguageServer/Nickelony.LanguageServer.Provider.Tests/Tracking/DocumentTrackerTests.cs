namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed partial class DocumentTrackerTests
{
	[TestMethod]
	public void Synchronize_RepeatedOpenCallsRequireMatchingCloseCalls()
	{
		const string filePath = @"C:\Workspace\Scripts\repeated-open.ext";

		var tracker = new DocumentTracker();

		Assert.IsNotNull(tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open));
		Assert.IsNull(tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open));

		DocumentCloseResult firstCloseResult = tracker.Close(filePath);

		Assert.AreEqual(DocumentCloseOutcome.StillOpen, firstCloseResult.Outcome);
		Assert.IsNull(firstCloseResult.ClosingDocument);

		DocumentCloseResult finalCloseResult = tracker.Close(filePath);

		Assert.AreEqual(DocumentCloseOutcome.Closed, finalCloseResult.Outcome);
		Assert.IsNotNull(finalCloseResult.ClosingDocument);

		DocumentCloseResult repeatedCloseResult = tracker.Close(filePath);

		Assert.AreEqual(DocumentCloseOutcome.Untracked, repeatedCloseResult.Outcome);
		Assert.IsNull(repeatedCloseResult.ClosingDocument);
	}

	[TestMethod]
	public void Synchronize_CaseSensitivePathComparison_TracksPathsThatDifferOnlyByCaseSeparately()
	{
		var tracker = new DocumentTracker(TestOptions.CaseSensitive);

		tracker.Synchronize(@"C:\Workspace\Scripts\Case.ext", "return 1");
		tracker.Synchronize(@"C:\Workspace\Scripts\CASE.ext", "return 2");

		Assert.AreEqual("return 1", tracker.GetDocumentSnapshot(@"C:\Workspace\Scripts\Case.ext")?.Content);
		Assert.AreEqual("return 2", tracker.GetDocumentSnapshot(@"C:\Workspace\Scripts\CASE.ext")?.Content);
	}

	[TestMethod]
	public void Synchronize_CaseInsensitivePathComparison_TreatsPathsThatDifferOnlyByCaseAsOneDocument()
	{
		var tracker = new DocumentTracker(TestOptions.CaseInsensitive);

		tracker.Synchronize(@"C:\Workspace\Scripts\Case.ext", "return 1");
		tracker.Synchronize(@"C:\Workspace\Scripts\CASE.ext", "return 2");

		Assert.AreEqual("return 2", tracker.GetDocumentSnapshot(@"C:\Workspace\Scripts\case.ext")?.Content);
	}

	[TestMethod]
	public void Synchronize_WithoutLeasesCreatesTrackedDocument()
	{
		const string filePath = @"C:\Workspace\Scripts\idle.ext";

		var tracker = new DocumentTracker();

		DocumentSynchronizationDirective? request = tracker.Synchronize(filePath, "return 1");

		Assert.IsNotNull(request);
		Assert.AreEqual(DocumentSynchronizationKind.Open, request.Value.Kind);
		Assert.IsNotNull(tracker.GetDocumentSnapshot(filePath));

		DocumentCloseResult closingResult = tracker.Close(filePath);

		Assert.AreEqual(DocumentCloseOutcome.Closed, closingResult.Outcome);
		Assert.IsNotNull(closingResult.ClosingDocument);
		Assert.IsNull(tracker.GetDocumentSnapshot(filePath));
	}

	[TestMethod]
	public void Synchronize_AfterCloseCreatesFreshStateWithNewContent()
	{
		const string filePath = @"C:\Workspace\Scripts\after-close.ext";

		var tracker = new DocumentTracker();
		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);

		Assert.AreEqual(DocumentCloseOutcome.Closed, tracker.Close(filePath).Outcome);

		DocumentSynchronizationDirective? reopenRequest = tracker.Synchronize(filePath, "return 2");

		Assert.IsNotNull(reopenRequest);
		Assert.AreEqual(DocumentSynchronizationKind.Open, reopenRequest.Value.Kind);
		Assert.AreEqual("return 2", reopenRequest.Value.Document.Content);
		Assert.AreEqual(1, reopenRequest.Value.Document.Version);
	}

	[TestMethod]
	public void Synchronize_WithChangedContent_ReportsIncrementalChangeRange()
	{
		const string filePath = @"C:\Workspace\Scripts\incremental.ext";

		var tracker = new DocumentTracker();
		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);

		DocumentSynchronizationDirective? changeRequest = tracker.Synchronize(filePath, "return 2", leases: DocumentLeaseKinds.Open);

		Assert.IsNotNull(changeRequest);
		Assert.AreEqual(DocumentSynchronizationKind.Change, changeRequest.Value.Kind);
		Assert.IsNotNull(changeRequest.Value.ChangeRange);

		DocumentChangeRange changeRange = changeRequest.Value.ChangeRange!.Value;

		Assert.AreEqual("2", changeRange.Text);
		Assert.AreEqual(new TextPositionRange(new TextPosition(0, 7), new TextPosition(0, 8)), changeRange.Range);
		Assert.AreEqual("return 2", changeRequest.Value.Document.Content);
	}

	[TestMethod]
	[DataRow("one\r\ntwo\r\nthree", "one\r\ntwo\r\nthreeX", 2, 5, 2, 5, "X", DisplayName = "InsertionOnALaterLine")]
	[DataRow("a\r\nb", "ab", 0, 1, 1, 0, "", DisplayName = "DeletionOfACrLfTerminator")]
	[DataRow("a\rb\nc", "a\rX\nc", 1, 0, 1, 1, "X", DisplayName = "ReplacementAfterALoneCr")]
	public void Synchronize_WithChangedContent_ReportsTheChangeRangeAsLineAndCharacterPositions(
		string previousContent,
		string content,
		int startLine,
		int startCharacter,
		int endLine,
		int endCharacter,
		string expectedText
	)
	{
		const string filePath = @"C:\Workspace\Scripts\incremental-positions.ext";

		var tracker = new DocumentTracker();
		tracker.Synchronize(filePath, previousContent, leases: DocumentLeaseKinds.Open);

		DocumentSynchronizationDirective? changeRequest = tracker.Synchronize(filePath, content, leases: DocumentLeaseKinds.Open);

		Assert.IsNotNull(changeRequest);
		Assert.IsNotNull(changeRequest.Value.ChangeRange);

		DocumentChangeRange changeRange = changeRequest.Value.ChangeRange!.Value;

		Assert.AreEqual(expectedText, changeRange.Text);

		Assert.AreEqual(
			new TextPositionRange(new TextPosition(startLine, startCharacter), new TextPosition(endLine, endCharacter)),
			changeRange.Range
		);
	}

	[TestMethod]
	public void Synchronize_WithoutChangeRange_ReportsChangeWithoutRange()
	{
		const string filePath = @"C:\Workspace\Scripts\no-range.ext";

		var tracker = new DocumentTracker();
		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);

		DocumentSynchronizationDirective? changeRequest = tracker.Synchronize(filePath, "return 2", includeChangeRange: false);

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

		var tracker = new DocumentTracker();

		Assert.AreEqual(DocumentRenameOutcome.SourceUntracked, tracker.Rename(oldFilePath, newFilePath, "return 1").Outcome);
		Assert.IsNull(tracker.GetDocumentSnapshot(oldFilePath));
		Assert.IsNull(tracker.GetDocumentSnapshot(newFilePath));
	}

	[TestMethod]
	public void Rename_PreservesTrackedContentAndLeases()
	{
		const string oldFilePath = @"C:\Workspace\Scripts\before.ext";
		const string newFilePath = @"C:\Workspace\Scripts\after.ext";

		var tracker = new DocumentTracker();
		var requestLease = new DocumentRequestLease();

		tracker.Synchronize(oldFilePath, "return 1", leases: DocumentLeaseKinds.Open);

		tracker.Synchronize(
			filePath: oldFilePath,
			content: "return 1",
			leases: DocumentLeaseKinds.Open | DocumentLeaseKinds.Request,
			requestLease: requestLease
		);

		DocumentRenameResult result = tracker.Rename(oldFilePath, newFilePath, "return 1");

		Assert.AreEqual(DocumentRenameOutcome.Renamed, result.Outcome);
		Assert.IsNotNull(result.Directive);

		DocumentRenameDirective renameRequest = result.Directive!.Value;

		Assert.IsTrue(renameRequest.ReopenServerDocument);
		Assert.AreEqual("return 1", renameRequest.RenamedDocument.Content);
		Assert.AreEqual(1, renameRequest.RenamedDocument.Version);
		Assert.IsNull(tracker.GetDocumentSnapshot(oldFilePath));
		Assert.IsNotNull(tracker.GetDocumentSnapshot(newFilePath));

		Assert.AreEqual(DocumentCloseOutcome.StillOpen, tracker.Close(newFilePath).Outcome);
		Assert.AreEqual(DocumentCloseOutcome.BusyWithRequests, tracker.Close(newFilePath).Outcome);

		// The identity-bound release drains the request lease on the renamed record; the idle record is then
		// removed by the explicit close.
		tracker.ReleaseRequestByLease(requestLease);

		DocumentCloseResult finalResult = tracker.Close(newFilePath);

		Assert.AreEqual(DocumentCloseOutcome.Closed, finalResult.Outcome);
		Assert.IsNotNull(finalResult.ClosingDocument);
		Assert.IsNull(tracker.GetDocumentSnapshot(newFilePath));
	}

	[TestMethod]
	public void ReleaseRequestByLease_FindsTheRecordAfterAMove()
	{
		const string oldFilePath = @"C:\Workspace\Scripts\before-release.ext";
		const string newFilePath = @"C:\Workspace\Scripts\after-release.ext";

		var tracker = new DocumentTracker();
		var requestLease = new DocumentRequestLease();

		tracker.Synchronize(
			filePath: oldFilePath,
			content: "return 1",
			leases: DocumentLeaseKinds.Open | DocumentLeaseKinds.Request,
			requestLease: requestLease
		);

		Assert.IsTrue(requestLease.IsAcquired);
		Assert.AreEqual(LocalPath.Normalize(oldFilePath), requestLease.CurrentFilePath);

		DocumentRenameResult result = tracker.Rename(oldFilePath, newFilePath, "return 1");

		Assert.AreEqual(DocumentRenameOutcome.Renamed, result.Outcome);
		Assert.IsNotNull(result.Directive);
		Assert.AreEqual(LocalPath.Normalize(newFilePath), requestLease.CurrentFilePath);

		// The identity-bound release drains the lease on the renamed record, so the record becomes closable.
		tracker.ReleaseRequestByLease(requestLease);

		DocumentCloseResult closingResult = tracker.Close(newFilePath);

		Assert.AreEqual(DocumentCloseOutcome.Closed, closingResult.Outcome);
		Assert.IsNotNull(closingResult.ClosingDocument);

		// Releasing the same lease again is a no-op rather than consuming another lease.
		tracker.ReleaseRequestByLease(requestLease);
		Assert.AreEqual(DocumentCloseOutcome.Untracked, tracker.Close(newFilePath).Outcome);
	}

	[TestMethod]
	public void Synchronize_WithAReusedRequestLease_ThrowsInsteadOfLosingTheReleaseHandle()
	{
		const string filePath = @"C:\Workspace\Scripts\reused-lease.ext";

		var tracker = new DocumentTracker();
		var requestLease = new DocumentRequestLease();

		tracker.Synchronize(filePath, content: "return 1", leases: DocumentLeaseKinds.Request, requestLease: requestLease);
		tracker.ReleaseRequestByLease(requestLease);

		// The single-use lease cannot acquire a second lease; the failed call must not leak the
		// first acquisition.
		Assert.ThrowsExactly<InvalidOperationException>(() =>
			tracker.Synchronize(filePath, content: "return 2", leases: DocumentLeaseKinds.Request, requestLease: requestLease)
		);

		Assert.AreEqual(DocumentCloseOutcome.Closed, tracker.Close(filePath).Outcome);
	}

	[TestMethod]
	public void Synchronize_WithALeaseButNoAcquisition_Throws()
	{
		const string filePath = @"C:\Workspace\Scripts\lease-without-acquisition.ext";

		var tracker = new DocumentTracker();
		var requestLease = new DocumentRequestLease();

		Assert.ThrowsExactly<ArgumentException>(() => tracker.Synchronize(filePath, "return 1", requestLease: requestLease));
		Assert.IsFalse(requestLease.IsAcquired);
	}

	[TestMethod]
	public void Synchronize_WithRequestLeasePolicyButNoLeaseObject_Throws()
	{
		const string filePath = @"C:\Workspace\Scripts\request-lease-without-object.ext";

		var tracker = new DocumentTracker();

		// A request lease acquired without a lease object could never be released, so the tracker rejects it instead
		// of leaving the record permanently request-busy.
		Assert.ThrowsExactly<ArgumentException>(() => tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Request));
	}

	[TestMethod]
	public void Rename_WhenDestinationIsAlreadyTracked_ReportsDestinationTrackedAndPreservesTrackedDocuments()
	{
		const string oldFilePath = @"C:\Workspace\Scripts\source.ext";
		const string newFilePath = @"C:\Workspace\Scripts\target.ext";

		var tracker = new DocumentTracker();

		tracker.Synchronize(oldFilePath, "return 1", leases: DocumentLeaseKinds.Open);
		tracker.Synchronize(newFilePath, "return 2", leases: DocumentLeaseKinds.Open);

		DocumentRenameResult result = tracker.Rename(oldFilePath, newFilePath, "return 1");

		Assert.AreEqual(DocumentRenameOutcome.DestinationTracked, result.Outcome);
		Assert.IsNull(result.Directive);
		Assert.IsNotNull(tracker.GetDocumentSnapshot(oldFilePath));

		DocumentSnapshot? destinationDocument = tracker.GetDocumentSnapshot(newFilePath);

		Assert.IsNotNull(destinationDocument);
		Assert.AreEqual("return 2", destinationDocument.Content);
		Assert.AreEqual(1, destinationDocument.Version);
		Assert.AreEqual(DocumentCloseOutcome.Closed, tracker.Close(oldFilePath).Outcome);

		DocumentCloseResult closedDestinationResult = tracker.Close(newFilePath);

		Assert.AreEqual(DocumentCloseOutcome.Closed, closedDestinationResult.Outcome);
		Assert.IsNotNull(closedDestinationResult.ClosingDocument);
		Assert.AreEqual("return 2", closedDestinationResult.ClosingDocument.Content);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void Rename_WhenTheDestinationCannotBecomeAFileUri_ThrowsAndKeepsTheRecordUnderItsOldPath()
	{
		const string oldFilePath = @"C:\Workspace\Scripts\uri-before.ext";

		// A UNC host with a space is a valid local path that has no file URI.
		const string newFilePath = @"\\bad host\share\uri-after.ext";

		var tracker = new DocumentTracker();
		var requestLease = new DocumentRequestLease();

		tracker.Synchronize(
			filePath: oldFilePath,
			content: "return 1",
			leases: DocumentLeaseKinds.Open | DocumentLeaseKinds.Request,
			requestLease: requestLease
		);

		Assert.ThrowsExactly<UriFormatException>(() => tracker.Rename(oldFilePath, newFilePath, "return 1"));

		// The failed rename left the record reachable under its old path, so its lease still releases and its document still closes.
		Assert.IsNotNull(tracker.GetDocumentSnapshot(oldFilePath));
		Assert.AreEqual(LocalPath.Normalize(oldFilePath), requestLease.CurrentFilePath);

		tracker.ReleaseRequestByLease(requestLease);

		Assert.AreEqual(DocumentCloseOutcome.Closed, tracker.Close(oldFilePath).Outcome);
		Assert.IsNull(tracker.GetDocumentSnapshot(oldFilePath));
	}

	[TestMethod]
	public void Rename_CaseOnlyDifferenceUnderACaseSensitiveComparison_RenamesTheDocument()
	{
		string directoryPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DocumentTrackerTests"));
		string originalFilePath = LocalPath.Normalize(Path.Combine(directoryPath, "test.ext"));
		string renamedFilePath = LocalPath.Normalize(Path.Combine(directoryPath, "TEST.ext"));

		var tracker = new DocumentTracker(TestOptions.CaseSensitive);
		tracker.Synchronize(originalFilePath, "return 1", leases: DocumentLeaseKinds.Open);

		DocumentRenameResult result = tracker.Rename(originalFilePath, renamedFilePath, "return 2");

		Assert.AreEqual(DocumentRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual("return 2", result.Directive?.RenamedDocument.Content);
		Assert.IsNull(tracker.GetDocumentSnapshot(originalFilePath));
		Assert.IsNotNull(tracker.GetDocumentSnapshot(renamedFilePath));
	}

	[TestMethod]
	public void Rename_CaseOnlyDifferenceUnderACaseInsensitiveComparison_LeavesTheSameDocumentInPlace()
	{
		string directoryPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DocumentTrackerTests"));
		string originalFilePath = LocalPath.Normalize(Path.Combine(directoryPath, "test.ext"));
		string renamedFilePath = LocalPath.Normalize(Path.Combine(directoryPath, "TEST.ext"));

		var tracker = new DocumentTracker(TestOptions.CaseInsensitive);
		tracker.Synchronize(originalFilePath, "return 1", leases: DocumentLeaseKinds.Open);

		DocumentRenameResult result = tracker.Rename(originalFilePath, renamedFilePath, "return 1");

		Assert.AreEqual(DocumentRenameOutcome.PathsEquivalent, result.Outcome);
		Assert.IsNull(result.Directive);
		Assert.IsNotNull(tracker.GetDocumentSnapshot(originalFilePath));

		DocumentSnapshot? aliasedDocument = tracker.GetDocumentSnapshot(renamedFilePath);

		Assert.IsNotNull(aliasedDocument);
		Assert.AreEqual(originalFilePath, aliasedDocument.FilePath);
	}

	[TestMethod]
	public void Synchronize_AndLookup_NormalizeEquivalentPaths()
	{
		string canonicalFilePath = Path.Combine(path1: Path.GetTempPath(), path2: "DocumentTrackerTests", path3: "scripts", path4: "test.ext");

		string directoryPath =
			Path.GetDirectoryName(canonicalFilePath) ?? throw new InvalidOperationException("The canonical test file path did not have a directory.");

		string aliasedFilePath = Path.Combine(directoryPath, ".", Path.GetFileName(canonicalFilePath));

		var tracker = new DocumentTracker();

		DocumentSynchronizationDirective? synchronizationRequest = tracker.Synchronize(
			canonicalFilePath,
			"return 1",
			leases: DocumentLeaseKinds.Open
		);

		Assert.IsNotNull(synchronizationRequest);
		Assert.AreEqual(LocalPath.Normalize(canonicalFilePath), synchronizationRequest.Value.Document.FilePath);

		DocumentSnapshot? snapshot = tracker.GetDocumentSnapshot(aliasedFilePath);

		Assert.IsNotNull(snapshot);
		Assert.AreEqual(LocalPath.Normalize(canonicalFilePath), snapshot.FilePath);

		DocumentCloseResult closedResult = tracker.Close(aliasedFilePath);

		Assert.AreEqual(DocumentCloseOutcome.Closed, closedResult.Outcome);
		Assert.IsNotNull(closedResult.ClosingDocument);
		Assert.IsNull(tracker.GetDocumentSnapshot(canonicalFilePath));
	}

	[TestMethod]
	public void Close_RemovesTrackedDocumentWhileRestartReplayIsPending()
	{
		const string filePath = @"C:\Workspace\Scripts\pending.ext";

		var tracker = new DocumentTracker();
		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);

		IReadOnlyList<DocumentSnapshot> documentsToReopen = tracker.PrepareForRestart();

		Assert.HasCount(1, documentsToReopen);

		DocumentCloseResult closingResult = tracker.Close(filePath);

		Assert.AreEqual(DocumentCloseOutcome.Closed, closingResult.Outcome);
		Assert.IsNull(closingResult.ClosingDocument);
		Assert.IsNull(tracker.GetDocumentSnapshot(filePath));
	}

	[TestMethod]
	public void Close_PreservesRequestOwnedTrackedDocumentUntilRequestRelease()
	{
		const string filePath = @"C:\Workspace\Scripts\request-owned.ext";

		var tracker = new DocumentTracker();
		var requestLease = new DocumentRequestLease();

		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);
		tracker.Synchronize(filePath, content: "return 1", leases: DocumentLeaseKinds.Request, requestLease: requestLease);

		DocumentCloseResult closingResult = tracker.Close(filePath);

		Assert.AreEqual(DocumentCloseOutcome.BusyWithRequests, closingResult.Outcome);
		Assert.IsNull(closingResult.ClosingDocument);
		Assert.IsNotNull(tracker.GetDocumentSnapshot(filePath));

		tracker.ReleaseRequestByLease(requestLease);

		DocumentCloseResult closedResult = tracker.Close(filePath);

		Assert.AreEqual(DocumentCloseOutcome.Closed, closedResult.Outcome);
		Assert.IsNotNull(closedResult.ClosingDocument);
		Assert.IsNull(tracker.GetDocumentSnapshot(filePath));
	}

	[TestMethod]
	public void Synchronize_ReopensTrackedDocumentAfterRestartPreparation()
	{
		const string filePath = @"C:\Workspace\Scripts\reopen.ext";

		var tracker = new DocumentTracker();

		DocumentSynchronizationDirective? initialRequest = tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);

		Assert.IsNotNull(initialRequest);
		Assert.AreEqual(DocumentSynchronizationKind.Open, initialRequest.Value.Kind);

		IReadOnlyList<DocumentSnapshot> documentsToReopen = tracker.PrepareForRestart();
		DocumentSynchronizationDirective? reopenRequest = tracker.Synchronize(filePath, "return 2", leases: DocumentLeaseKinds.Open);

		Assert.HasCount(1, documentsToReopen);
		Assert.AreEqual(filePath, documentsToReopen[0].FilePath);
		Assert.IsNotNull(reopenRequest);
		Assert.AreEqual(DocumentSynchronizationKind.Open, reopenRequest.Value.Kind);
		Assert.AreEqual(filePath, reopenRequest.Value.Document.FilePath);
		Assert.AreEqual("return 2", reopenRequest.Value.Document.Content);
		Assert.AreEqual(2, reopenRequest.Value.Document.Version);

		DocumentSnapshot? reopenedDocument = tracker.GetDocumentSnapshot(filePath);

		Assert.IsNotNull(reopenedDocument);
		Assert.AreEqual("return 2", reopenedDocument.Content);
		Assert.AreEqual(2, reopenedDocument.Version);
		Assert.AreEqual(DocumentCloseOutcome.StillOpen, tracker.Close(filePath).Outcome);

		DocumentCloseResult finalClosedResult = tracker.Close(filePath);

		Assert.AreEqual(DocumentCloseOutcome.Closed, finalClosedResult.Outcome);
		Assert.IsNotNull(finalClosedResult.ClosingDocument);
	}

	[TestMethod]
	public void TrimIdleDocuments_RemovesOldestIdleDocuments()
	{
		const string firstFilePath = @"C:\Workspace\Scripts\first.ext";
		const string secondFilePath = @"C:\Workspace\Scripts\second.ext";
		const string thirdFilePath = @"C:\Workspace\Scripts\third.ext";

		var tracker = new DocumentTracker();

		// A synchronization without leases leaves an idle server-open record behind, which is exactly the state
		// the trim evicts once a newer document pushes it out.
		tracker.Synchronize(firstFilePath, "return 1");
		tracker.Synchronize(secondFilePath, "return 2");
		tracker.Synchronize(thirdFilePath, "return 3");

		IReadOnlyList<DocumentSnapshot> trimmedDocuments = tracker.TrimIdleDocuments(1);

		Assert.HasCount(2, trimmedDocuments);

		Assert.AreSequenceEqual(
			[firstFilePath, secondFilePath],
			[trimmedDocuments[0].FilePath, trimmedDocuments[1].FilePath],
			Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder
		);

		Assert.IsNull(tracker.GetDocumentSnapshot(firstFilePath));
		Assert.IsNull(tracker.GetDocumentSnapshot(secondFilePath));
		Assert.IsNotNull(tracker.GetDocumentSnapshot(thirdFilePath));
		Assert.IsEmpty(tracker.TrimIdleDocuments(1));
	}

	[TestMethod]
	public void TrimIdleDocuments_KeepsTheDocumentThatWasSynchronizedMostRecently()
	{
		const string firstFilePath = @"C:\Workspace\Scripts\first-recent.ext";
		const string secondFilePath = @"C:\Workspace\Scripts\second-recent.ext";
		const string thirdFilePath = @"C:\Workspace\Scripts\third-recent.ext";

		var tracker = new DocumentTracker();

		tracker.Synchronize(firstFilePath, "return 1");
		tracker.Synchronize(secondFilePath, "return 2");
		tracker.Synchronize(thirdFilePath, "return 3");

		// A synchronization that changes nothing still counts as use, so the oldest record becomes the newest.
		Assert.IsNull(tracker.Synchronize(firstFilePath, "return 1"));

		Assert.HasCount(2, tracker.TrimIdleDocuments(1));
		Assert.IsNotNull(tracker.GetDocumentSnapshot(firstFilePath));
		Assert.IsNull(tracker.GetDocumentSnapshot(secondFilePath));
		Assert.IsNull(tracker.GetDocumentSnapshot(thirdFilePath));
	}

	[TestMethod]
	public void TrimIdleDocuments_TreatsAReleasedRequestLeaseAsRecentUse()
	{
		const string firstFilePath = @"C:\Workspace\Scripts\first-released.ext";
		const string secondFilePath = @"C:\Workspace\Scripts\second-released.ext";
		const string thirdFilePath = @"C:\Workspace\Scripts\third-released.ext";

		var tracker = new DocumentTracker();
		var requestLease = new DocumentRequestLease();

		tracker.Synchronize(filePath: firstFilePath, content: "return 1", leases: DocumentLeaseKinds.Request, requestLease: requestLease);
		tracker.Synchronize(secondFilePath, "return 2");
		tracker.Synchronize(thirdFilePath, "return 3");

		tracker.ReleaseRequestByLease(requestLease);

		Assert.HasCount(2, tracker.TrimIdleDocuments(1));
		Assert.IsNotNull(tracker.GetDocumentSnapshot(firstFilePath));
		Assert.IsNull(tracker.GetDocumentSnapshot(secondFilePath));
		Assert.IsNull(tracker.GetDocumentSnapshot(thirdFilePath));
	}

	[TestMethod]
	public void TrimIdleDocuments_RemovesAClosedRecordWithoutReturningItsSnapshot()
	{
		const string firstFilePath = @"C:\Workspace\Scripts\first-closed.ext";
		const string secondFilePath = @"C:\Workspace\Scripts\second-closed.ext";

		var tracker = new DocumentTracker();

		tracker.Synchronize(firstFilePath, "return 1");
		tracker.Synchronize(secondFilePath, "return 2");

		// The restart marks both records closed locally and only the first one is opened again, so the server holds a copy of the first only.
		tracker.PrepareForRestart();
		Assert.IsNotNull(tracker.Synchronize(firstFilePath, "return 1"));

		IReadOnlyList<DocumentSnapshot> trimmedDocuments = tracker.TrimIdleDocuments(0);

		Assert.HasCount(1, trimmedDocuments);
		Assert.AreEqual(firstFilePath, trimmedDocuments[0].FilePath);
		Assert.IsNull(tracker.GetDocumentSnapshot(firstFilePath));
		Assert.IsNull(tracker.GetDocumentSnapshot(secondFilePath));
	}

	[TestMethod]
	public void TrimIdleDocuments_NegativeMaxCount_ThrowsArgumentOutOfRangeException()
	{
		var tracker = new DocumentTracker();
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => tracker.TrimIdleDocuments(-1));
	}

	[TestMethod]
	public void Rename_SamePathWithNewContent_LeavesTrackedContentEqualToTheServerCopy()
	{
		string directoryPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DocumentTrackerTests"));
		string originalFilePath = LocalPath.Normalize(Path.Combine(directoryPath, "test.ext"));
		string renamedFilePath = LocalPath.Normalize(Path.Combine(directoryPath, "TEST.ext"));

		var tracker = new DocumentTracker(TestOptions.CaseInsensitive);
		tracker.Synchronize(originalFilePath, "return 1", leases: DocumentLeaseKinds.Open);

		DocumentRenameResult result = tracker.Rename(originalFilePath, renamedFilePath, "return 2");

		// A same-file rename mirrors nothing to the server, so the supplied content must not advance the tracked
		// record: the tracked text stays equal to the text the server received.
		Assert.AreEqual(DocumentRenameOutcome.PathsEquivalent, result.Outcome);
		Assert.IsNull(result.Directive);

		DocumentSnapshot? trackedDocument = tracker.GetDocumentSnapshot(originalFilePath);

		Assert.IsNotNull(trackedDocument);
		Assert.AreEqual("return 1", trackedDocument.Content);
		Assert.AreEqual(1, trackedDocument.Version);

		// A later synchronization still computes its incremental range against the content the server actually holds.
		DocumentSynchronizationDirective? changeRequest = tracker.Synchronize(originalFilePath, "return 2", includeChangeRange: true);

		Assert.IsNotNull(changeRequest);
		Assert.AreEqual(DocumentSynchronizationKind.Change, changeRequest.Value.Kind);
		Assert.IsNotNull(changeRequest.Value.ChangeRange);
		Assert.AreEqual("return 2", changeRequest.Value.Document.Content);
		Assert.AreEqual(2, changeRequest.Value.Document.Version);
	}

	[TestMethod]
	public void ReopenAfterRestart_ReopensTrackedDocumentAfterRestartPreparation()
	{
		const string filePath = @"C:\Workspace\Scripts\guarded-reopen.ext";

		var tracker = new DocumentTracker();
		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);
		tracker.PrepareForRestart();

		DocumentSynchronizationDirective? reopenRequest = tracker.ReopenAfterRestart(filePath);

		Assert.IsNotNull(reopenRequest);
		Assert.AreEqual(DocumentSynchronizationKind.Open, reopenRequest.Value.Kind);
		Assert.AreEqual("return 1", reopenRequest.Value.Document.Content);

		DocumentSnapshot? reopenedDocument = tracker.GetDocumentSnapshot(filePath);

		Assert.IsNotNull(reopenedDocument);
	}

	[TestMethod]
	public void ReopenAfterRestart_WhenDocumentWasClosedDuringRestart_ReturnsNullAndKeepsItUntracked()
	{
		const string filePath = @"C:\Workspace\Scripts\closed-during-restart.ext";

		var tracker = new DocumentTracker();
		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);
		tracker.PrepareForRestart();

		Assert.AreEqual(DocumentCloseOutcome.Closed, tracker.Close(filePath).Outcome);

		Assert.IsNull(tracker.ReopenAfterRestart(filePath));
		Assert.IsNull(tracker.GetDocumentSnapshot(filePath));
	}

	[TestMethod]
	public void ReopenAfterRestart_WhenDocumentWasAlreadyReopenedDuringRestart_ReturnsNull()
	{
		const string filePath = @"C:\Workspace\Scripts\already-reopened.ext";

		var tracker = new DocumentTracker();
		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);
		tracker.PrepareForRestart();

		DocumentSynchronizationDirective? concurrentReopenRequest = tracker.Synchronize(filePath, "return 2", leases: DocumentLeaseKinds.Open);

		Assert.IsNotNull(concurrentReopenRequest);
		Assert.AreEqual(DocumentSynchronizationKind.Open, concurrentReopenRequest.Value.Kind);

		Assert.IsNull(tracker.ReopenAfterRestart(filePath));

		DocumentSnapshot? trackedDocument = tracker.GetDocumentSnapshot(filePath);

		Assert.IsNotNull(trackedDocument);
		Assert.AreEqual("return 2", trackedDocument.Content);
	}

	[TestMethod]
	public void ReopenAfterRestart_WhenOnlyRequestLeasesRemain_ReturnsNullAndKeepsTheRecordClosed()
	{
		const string filePath = @"C:\Workspace\Scripts\request-only.ext";

		var tracker = new DocumentTracker();
		var requestLease = new DocumentRequestLease();

		tracker.Synchronize(filePath, content: "return 1", leases: DocumentLeaseKinds.Request, requestLease: requestLease);

		// A record held only by a request lease is not replayed; its next synchronization reopens it.
		Assert.IsEmpty(tracker.PrepareForRestart());
		Assert.IsNull(tracker.ReopenAfterRestart(filePath));

		DocumentSynchronizationDirective? reopenRequest = tracker.Synchronize(filePath, "return 1");

		Assert.IsNotNull(reopenRequest);
		Assert.AreEqual(DocumentSynchronizationKind.Open, reopenRequest.Value.Kind);
	}

	[TestMethod]
	public void ReopenAfterRestart_WhenDocumentIsUntracked_DoesNotCreateRecord()
	{
		const string trackedFilePath = @"C:\Workspace\Scripts\tracked.ext";
		const string untrackedFilePath = @"C:\Workspace\Scripts\untracked.ext";

		var tracker = new DocumentTracker();
		tracker.Synchronize(trackedFilePath, "return 1", leases: DocumentLeaseKinds.Open);
		tracker.PrepareForRestart();

		Assert.IsNull(tracker.ReopenAfterRestart(untrackedFilePath));
		Assert.IsNull(tracker.GetDocumentSnapshot(untrackedFilePath));
	}
}
