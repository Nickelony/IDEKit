using Nickelony.IDEKit.Core.Pathing;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Editing;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed class WorkspaceEditApplierTests
{
	[TestMethod]
	public void Apply_EmptyTargets_CompletesWithoutChanges()
	{
		var applier = new WorkspaceEditApplier(_ => throw new InvalidOperationException("No replacement expected."));

		WorkspaceEditApplicationResult result = applier.Apply([]);

		Assert.AreEqual(WorkspaceEditApplicationOutcome.Completed, result.Outcome);
		Assert.IsFalse(result.ChangeSet.HasChanges);
		Assert.AreEqual(0, result.TargetResults.Count);
		Assert.AreEqual(0, result.PreparedOperationCount);
	}

	[TestMethod]
	public void Apply_NoOpTargets_CompletesWithoutReplacement()
	{
		int replaceCount = 0;
		var applier = new WorkspaceEditApplier(_ =>
		{
			replaceCount++;
			throw new InvalidOperationException("No replacement expected.");
		});

		var target = CreateTarget("same.txt", "content", "content");
		WorkspaceEditApplicationResult result = applier.Apply([target]);

		Assert.AreEqual(WorkspaceEditApplicationOutcome.Completed, result.Outcome);
		Assert.IsFalse(result.ChangeSet.HasChanges);
		Assert.AreEqual(0, replaceCount);
		Assert.AreEqual(WorkspaceEditTargetOutcome.Applied, result.TargetResults[0].Outcome);

		// The store is not consulted for a no-op target and no earlier target of the document
		// recorded a version, so the actual version is unknown instead of assumed.
		Assert.IsNull(result.TargetResults[0].ActualVersion);
	}

	[TestMethod]
	public void Apply_NoOpAndChangedTargets_CountsOnlyChangedPreparations()
	{
		var requests = new List<WorkspaceDocumentReplaceRequest>();
		var applier = new WorkspaceEditApplier(request =>
		{
			requests.Add(request);
			return new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationOutcome.Changed,
				request.Identity,
				CreateSnapshot(request.Content, 1, request.Identity.DocumentKey, request.Identity.DocumentId));
		});

		WorkspaceEditTargetPreparation noOp = CreateTarget("noop.txt", "same", "same");
		WorkspaceEditTargetPreparation changed = CreateTarget("changed.txt", "before", "after");
		WorkspaceEditApplicationResult result = applier.Apply([noOp, changed]);

		// A no-op target prepares no replacement operations, so the aggregate count reflects only
		// the targets whose content actually differs.
		Assert.AreEqual(WorkspaceEditApplicationOutcome.Completed, result.Outcome);
		Assert.AreEqual(1, result.PreparedOperationCount);
		Assert.AreEqual(0, result.TargetResults[0].PreparedOperationCount);
		Assert.AreEqual(1, result.TargetResults[1].PreparedOperationCount);
		Assert.AreEqual(1, result.ChangeSet.DocumentChanges.Count);
		Assert.HasCount(1, requests);
	}

	[TestMethod]
	public void Apply_AllTargetsApplied_CompletesWithChangeSet()
	{
		var requests = new List<WorkspaceDocumentReplaceRequest>();
		long version = 0;
		var applier = new WorkspaceEditApplier(request =>
		{
			requests.Add(request);
			return new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationOutcome.Changed,
				request.Identity,
				CreateSnapshot(request.Content, ++version, request.Identity.DocumentKey, request.Identity.DocumentId));
		});

		WorkspaceEditTargetPreparation first = CreateTarget("first.txt", "before", "after");
		WorkspaceEditTargetPreparation second = CreateTarget("second.txt", "old", "new");
		WorkspaceEditApplicationResult result = applier.Apply([first, second]);

		Assert.AreEqual(WorkspaceEditApplicationOutcome.Completed, result.Outcome);
		Assert.IsTrue(result.ChangeSet.HasChanges);
		Assert.AreEqual(2, result.TargetResults.Count);
		CollectionAssert.AreEquivalent(
			new[] { "first.txt", "second.txt" },
			result.ChangedTargetIds.ToArray());
		Assert.AreEqual(2, requests.Count);
		Assert.AreEqual("after", requests[0].Content);
		Assert.AreEqual("new", requests[1].Content);
		Assert.AreEqual(2, result.ChangeSet.DocumentChanges.Count);

		// The replacement delegate returns bumped snapshot versions, so the post-mutation version is
		// reported instead of the caller's pre-mutation expected version (4).
		Assert.AreEqual(4, result.TargetResults[0].ExpectedVersion);
		Assert.AreEqual(1, result.TargetResults[0].ActualVersion);
		Assert.AreEqual(4, result.TargetResults[1].ExpectedVersion);
		Assert.AreEqual(2, result.TargetResults[1].ActualVersion);
	}

	[TestMethod]
	public void Apply_StoreNoChange_MarksAppliedWithoutChangeRecord()
	{
		var applier = new WorkspaceEditApplier(request => new WorkspaceDocumentMutationResult(
			WorkspaceDocumentMutationOutcome.NoChange,
			request.Identity,
			CreateSnapshot(request.Content, 7, request.Identity.DocumentKey, request.Identity.DocumentId)));

		WorkspaceEditApplicationResult result = applier.Apply([CreateTarget("first.txt", "before", "after")]);

		// The store already held the requested content, so the desired end state holds and the target
		// is applied; no change record is produced because the applier did not transform the document.
		Assert.AreEqual(WorkspaceEditApplicationOutcome.Completed, result.Outcome);
		Assert.AreEqual(WorkspaceEditTargetOutcome.Applied, result.TargetResults[0].Outcome);
		Assert.AreEqual(7, result.TargetResults[0].ActualVersion);
		Assert.AreEqual(0, result.ChangedTargetIds.Count);
		Assert.AreEqual(0, result.UnknownTargetIds.Count);
		Assert.IsFalse(result.ChangeSet.HasChanges);
		Assert.AreEqual(0, result.ChangeSet.DocumentChanges.Count);
	}

	[TestMethod]
	[DataRow(WorkspaceDocumentMutationOutcome.Changed, DisplayName = "Changed")]
	[DataRow(WorkspaceDocumentMutationOutcome.NoChange, DisplayName = "NoChange")]
	public void Apply_ResultWithoutSnapshotFallsBackToRequestedVersion(WorkspaceDocumentMutationOutcome outcome)
	{
		var applier = new WorkspaceEditApplier(request => new WorkspaceDocumentMutationResult(
			outcome,
			new(request.Identity.DocumentKey, request.Identity.DocumentId, 42),
			Snapshot: null));

		WorkspaceEditApplicationResult result = applier.Apply([CreateTarget("first.txt", "before", "after")]);

		// No snapshot reports the post-mutation version, so the applier falls back to the version the
		// store echoed as requested instead of the caller's pre-mutation expected version (4).
		Assert.AreEqual(WorkspaceEditTargetOutcome.Applied, result.TargetResults[0].Outcome);
		Assert.AreEqual(4, result.TargetResults[0].ExpectedVersion);
		Assert.AreEqual(42, result.TargetResults[0].ActualVersion);
	}

	[TestMethod]
	[DataRow(WorkspaceDocumentMutationOutcome.StaleDocument, DisplayName = "StaleDocument")]
	[DataRow(WorkspaceDocumentMutationOutcome.StaleDocumentInstance, DisplayName = "StaleDocumentInstance")]
	[DataRow(WorkspaceDocumentMutationOutcome.DocumentNotFound, DisplayName = "DocumentNotFound")]
	[DataRow(WorkspaceDocumentMutationOutcome.OperationInProgress, DisplayName = "OperationInProgress")]
	public void Apply_DeterministicRejection_MarksNotApplied(WorkspaceDocumentMutationOutcome outcome)
	{
		var applier = new WorkspaceEditApplier(request => new WorkspaceDocumentMutationResult(
			outcome,
			request.Identity,
			null));

		WorkspaceEditApplicationResult result = applier.Apply([CreateTarget("first.txt", "before", "after")]);

		Assert.AreEqual(WorkspaceEditTargetOutcome.NotApplied, result.TargetResults[0].Outcome);
		Assert.AreEqual(0, result.UnknownTargetIds.Count);
		Assert.AreEqual(0, result.ChangedTargetIds.Count);
		Assert.AreEqual(WorkspaceOperationFailureCodes.TargetNotChanged, result.TargetResults[0].Failure?.Code);
	}

	[TestMethod]
	public void Apply_RejectedMutation_PartialResultMarksNotAppliedAndSkipsRemainder()
	{
		var applier = new WorkspaceEditApplier(request => new WorkspaceDocumentMutationResult(
			WorkspaceDocumentMutationOutcome.StaleDocument,
			request.Identity,
			null));

		WorkspaceEditTargetPreparation first = CreateTarget("first.txt", "before", "after");
		WorkspaceEditTargetPreparation second = CreateTarget("second.txt", "old", "new");
		WorkspaceEditApplicationResult result = applier.Apply([first, second]);

		Assert.AreEqual(WorkspaceEditApplicationOutcome.PartiallyApplied, result.Outcome);
		Assert.AreEqual(WorkspaceEditTargetOutcome.NotApplied, result.TargetResults[0].Outcome);
		Assert.AreEqual(WorkspaceEditTargetOutcome.NotApplied, result.TargetResults[1].Outcome);
		Assert.AreEqual(0, result.UnknownTargetIds.Count);
		Assert.AreEqual(0, result.ChangedTargetIds.Count);
		Assert.AreEqual(WorkspaceOperationFailureCodes.TargetNotChanged, result.Failure?.Code);

		// Both replacements were prepared; the unreached target still reports its prepared operation.
		Assert.AreEqual(2, result.PreparedOperationCount);
		Assert.AreEqual(1, result.TargetResults[1].PreparedOperationCount);
	}

	[TestMethod]
	public void Apply_ReplaceThrows_PartialResultMarksUnknown()
	{
		var applier = new WorkspaceEditApplier(_ => throw new InvalidOperationException("Store unavailable."));

		WorkspaceEditTargetPreparation first = CreateTarget("first.txt", "before", "after");
		WorkspaceEditTargetPreparation second = CreateTarget("second.txt", "old", "new");
		WorkspaceEditApplicationResult result = applier.Apply([first, second]);

		Assert.AreEqual(WorkspaceEditApplicationOutcome.PartiallyApplied, result.Outcome);
		Assert.AreEqual(WorkspaceEditTargetOutcome.Unknown, result.TargetResults[0].Outcome);
		Assert.AreEqual(WorkspaceEditTargetOutcome.NotApplied, result.TargetResults[1].Outcome);
		CollectionAssert.AreEqual(new[] { "first.txt" }, result.UnknownTargetIds.ToArray());
		Assert.AreEqual(WorkspaceOperationFailureCodes.TargetApplicationFailed, result.Failure?.Code);
		Assert.AreEqual("Store unavailable.", result.Failure?.Message);
	}

	[TestMethod]
	public void Apply_FailureAfterAppliedTarget_RetainsConfirmedChanges()
	{
		var requests = new List<WorkspaceDocumentReplaceRequest>();
		var applier = new WorkspaceEditApplier(request =>
		{
			requests.Add(request);
			if (request.Identity.DocumentId == "second.txt")
				return new WorkspaceDocumentMutationResult(
					WorkspaceDocumentMutationOutcome.OperationInProgress,
					request.Identity,
					null);

			return new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationOutcome.Changed,
				request.Identity,
				CreateSnapshot(request.Content, 1, request.Identity.DocumentKey, request.Identity.DocumentId));
		});

		WorkspaceEditTargetPreparation first = CreateTarget("first.txt", "before", "after");
		WorkspaceEditTargetPreparation second = CreateTarget("second.txt", "old", "new");
		WorkspaceEditApplicationResult result = applier.Apply([first, second]);

		Assert.AreEqual(WorkspaceEditApplicationOutcome.PartiallyApplied, result.Outcome);
		Assert.AreEqual(WorkspaceEditTargetOutcome.Applied, result.TargetResults[0].Outcome);
		Assert.AreEqual(WorkspaceEditTargetOutcome.NotApplied, result.TargetResults[1].Outcome);
		CollectionAssert.Contains(result.ChangedTargetIds.ToArray(), "first.txt");
		Assert.AreEqual(0, result.UnknownTargetIds.Count);
		Assert.AreEqual(1, result.ChangeSet.DocumentChanges.Count);
		Assert.AreEqual("before", result.ChangeSet.DocumentChanges[0].BeforeContent);
		Assert.AreEqual("after", result.ChangeSet.DocumentChanges[0].AfterContent);
	}

	[TestMethod]
	public void Apply_NullTargetsIsAnArgumentError()
	{
		var applier = new WorkspaceEditApplier(_ => throw new InvalidOperationException("No replacement expected."));

		Assert.ThrowsExactly<ArgumentNullException>(() => applier.Apply(null!));
	}

	[TestMethod]
	public void Constructor_NullReplacementDelegateIsAnArgumentError()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceEditApplier(null!));
	}

	[TestMethod]
	public void Apply_NullTargetEntryIsAnArgumentError()
	{
		var applier = new WorkspaceEditApplier(_ => throw new InvalidOperationException("No replacement expected."));

		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => applier.Apply(
			[CreateTarget("first.txt", "before", "after"), null!]));

		Assert.AreEqual("targets", exception.ParamName);
	}

	[TestMethod]
	public void Apply_UsesConfiguredPathComparisonForChangedTargetIds()
	{
		long version = 0;
		var caseInsensitive = new WorkspaceEditApplier(
			request => new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationOutcome.Changed,
				request.Identity,
				CreateSnapshot(request.Content, ++version, request.Identity.DocumentKey, request.Identity.DocumentId)),
			LocalPathComparisonPolicy.CaseInsensitive);
		var caseSensitive = new WorkspaceEditApplier(
			request => new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationOutcome.Changed,
				request.Identity,
				CreateSnapshot(request.Content, ++version, request.Identity.DocumentKey, request.Identity.DocumentId)),
			LocalPathComparisonPolicy.CaseSensitive);
		WorkspaceEditTargetPreparation first = CreateTarget("Doc.lua", "before", "after");
		WorkspaceEditTargetPreparation second = CreateTarget("doc.lua", "old", "new");

		WorkspaceEditApplicationResult insensitive = caseInsensitive.Apply([first, second]);
		WorkspaceEditApplicationResult sensitive = caseSensitive.Apply([first, second]);

		CollectionAssert.AreEqual(new[] { "Doc.lua" }, insensitive.ChangedTargetIds.ToArray());
		CollectionAssert.AreEqual(new[] { "Doc.lua", "doc.lua" }, sensitive.ChangedTargetIds.ToArray());
	}

	[TestMethod]
	public void Apply_TwoTargetsForSameDocument_AppliesBothWithRefreshedExpectedVersion()
	{
		var requests = new List<WorkspaceDocumentReplaceRequest>();
		long version = 0;
		var applier = new WorkspaceEditApplier(request =>
		{
			requests.Add(request);
			return new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationOutcome.Changed,
				request.Identity,
				CreateSnapshot(request.Content, ++version, request.Identity.DocumentKey, request.Identity.DocumentId));
		});
		WorkspaceEditTargetPreparation first = CreateTarget("same.txt", "one", "two");
		WorkspaceEditTargetPreparation second = CreateTarget("same.txt", "two", "three");

		WorkspaceEditApplicationResult result = applier.Apply([first, second]);

		// Both targets belong to the same document; the applier refreshes the expected version from
		// the first result, so the second replacement is not rejected with a stale version.
		Assert.AreEqual(WorkspaceEditApplicationOutcome.Completed, result.Outcome);
		Assert.AreEqual(WorkspaceEditTargetOutcome.Applied, result.TargetResults[0].Outcome);
		Assert.AreEqual(WorkspaceEditTargetOutcome.Applied, result.TargetResults[1].Outcome);
		Assert.AreEqual(2, requests.Count);
		Assert.AreEqual(4, requests[0].Identity.Version);
		Assert.AreEqual(1, requests[1].Identity.Version);
		Assert.AreEqual(2, result.ChangeSet.DocumentChanges.Count);
	}

	[TestMethod]
	public void Apply_NoOpTargetAfterAppliedTargetOfSameDocument_ReportsRecordedVersion()
	{
		long version = 0;
		var applier = new WorkspaceEditApplier(request => new WorkspaceDocumentMutationResult(
			WorkspaceDocumentMutationOutcome.Changed,
			request.Identity,
			CreateSnapshot(request.Content, ++version, request.Identity.DocumentKey, request.Identity.DocumentId)));
		WorkspaceEditTargetPreparation applied = CreateTarget("same.txt", "before", "after");
		WorkspaceEditTargetPreparation noOp = CreateTarget("same.txt", "after", "after");

		WorkspaceEditApplicationResult result = applier.Apply([applied, noOp]);

		// The no-op target is not sent to the store, but the earlier target of the same document
		// recorded a version, so that version is reported instead of an unknown one.
		Assert.AreEqual(WorkspaceEditApplicationOutcome.Completed, result.Outcome);
		Assert.AreEqual(1, result.TargetResults[0].ActualVersion);
		Assert.AreEqual(1, result.TargetResults[1].ActualVersion);
		Assert.IsNull(result.TargetResults[1].Failure);
		Assert.AreEqual(1, result.ChangeSet.DocumentChanges.Count);
	}

	[TestMethod]
	public void Apply_CanceledBeforeNextTarget_PartialResultMarksRemainderNotApplied()
	{
		using var cancellation = new CancellationTokenSource();
		long version = 0;
		var applier = new WorkspaceEditApplier(request =>
		{
			cancellation.Cancel();
			return new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationOutcome.Changed,
				request.Identity,
				CreateSnapshot(request.Content, ++version, request.Identity.DocumentKey, request.Identity.DocumentId));
		});
		WorkspaceEditTargetPreparation first = CreateTarget("first.txt", "before", "after");
		WorkspaceEditTargetPreparation second = CreateTarget("second.txt", "old", "new");

		WorkspaceEditApplicationResult result = applier.Apply([first, second], cancellation.Token);

		// The confirmed change is retained; the remaining targets are not attempted and the result
		// reports the cancellation as its failure.
		Assert.AreEqual(WorkspaceEditApplicationOutcome.PartiallyApplied, result.Outcome);
		Assert.AreEqual(WorkspaceEditTargetOutcome.Applied, result.TargetResults[0].Outcome);
		Assert.AreEqual(WorkspaceEditTargetOutcome.NotApplied, result.TargetResults[1].Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.Canceled, result.Failure?.Code);
		Assert.AreEqual(1, result.ChangeSet.DocumentChanges.Count);
	}

	[TestMethod]
	public void Apply_ContinueOnFailure_AttemptsEveryTargetAndReportsFirstFailure()
	{
		var requests = new List<WorkspaceDocumentReplaceRequest>();
		var applier = new WorkspaceEditApplier(
			request =>
			{
				requests.Add(request);
				return request.Identity.DocumentId.EndsWith("first.txt", StringComparison.Ordinal)
					? new WorkspaceDocumentMutationResult(WorkspaceDocumentMutationOutcome.StaleDocument, request.Identity, null)
					: new WorkspaceDocumentMutationResult(
						WorkspaceDocumentMutationOutcome.Changed,
						request.Identity,
						CreateSnapshot(request.Content, 1, request.Identity.DocumentKey, request.Identity.DocumentId));
			},
			continueOnFailure: true);
		WorkspaceEditTargetPreparation first = CreateTarget("first.txt", "before", "after");
		WorkspaceEditTargetPreparation second = CreateTarget("second.txt", "old", "new");

		WorkspaceEditApplicationResult result = applier.Apply([first, second]);

		// Every target is attempted; the first failure is reported while the confirmed change and
		// the later applied target remain in the result.
		Assert.AreEqual(WorkspaceEditApplicationOutcome.PartiallyApplied, result.Outcome);
		Assert.AreEqual(2, requests.Count);
		Assert.AreEqual(WorkspaceEditTargetOutcome.NotApplied, result.TargetResults[0].Outcome);
		Assert.AreEqual(WorkspaceEditTargetOutcome.Applied, result.TargetResults[1].Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.TargetNotChanged, result.Failure?.Code);
		Assert.AreEqual(1, result.ChangeSet.DocumentChanges.Count);
	}

	[TestMethod]
	public void Apply_TargetRejectedWithNewerSnapshot_LaterTargetOfSameDocumentIsNotApplied()
	{
		var requests = new List<WorkspaceDocumentReplaceRequest>();
		var applier = new WorkspaceEditApplier(
			request =>
			{
				requests.Add(request);

				// The store returns a rejection together with a current-state snapshot whose version is
				// newer than the request: the applier did not observe that state.
				return new WorkspaceDocumentMutationResult(
					WorkspaceDocumentMutationOutcome.StaleDocument,
					request.Identity,
					CreateSnapshot("other writer", 9, request.Identity.DocumentKey, request.Identity.DocumentId));
			},
			continueOnFailure: true);
		WorkspaceEditTargetPreparation first = CreateTarget("same.txt", "one", "two");
		WorkspaceEditTargetPreparation second = CreateTarget("same.txt", "two", "three");

		WorkspaceEditApplicationResult result = applier.Apply([first, second]);

		// The rejection snapshot must not authorize the second write: the second target is prepared
		// against state the applier did not confirm, so only the first request reaches the store.
		Assert.AreEqual(WorkspaceEditApplicationOutcome.PartiallyApplied, result.Outcome);
		Assert.AreEqual(1, requests.Count);
		Assert.AreEqual(4, requests[0].Identity.Version);
		Assert.AreEqual(WorkspaceEditTargetOutcome.NotApplied, result.TargetResults[0].Outcome);
		Assert.AreEqual(WorkspaceEditTargetOutcome.NotApplied, result.TargetResults[1].Outcome);
		Assert.IsNull(result.TargetResults[1].ActualVersion);
		Assert.AreEqual(WorkspaceOperationFailureCodes.TargetSkipped, result.TargetResults[1].Failure?.Code);
		Assert.IsFalse(result.ChangeSet.HasChanges);
		Assert.AreEqual(0, result.ChangeSet.DocumentChanges.Count);
	}

	[TestMethod]
	public void Apply_NoOpTargetAfterRejectedTargetOfSameDocument_IsStillAppliedWithoutTheStore()
	{
		int requestCount = 0;
		var applier = new WorkspaceEditApplier(
			request =>
			{
				requestCount++;
				return new WorkspaceDocumentMutationResult(WorkspaceDocumentMutationOutcome.StaleDocument, request.Identity, null);
			},
			continueOnFailure: true);
		WorkspaceEditTargetPreparation rejected = CreateTarget("same.txt", "one", "two");
		WorkspaceEditTargetPreparation noOp = CreateTarget("same.txt", "two", "two");

		WorkspaceEditApplicationResult result = applier.Apply([rejected, noOp]);

		// A target that would write nothing is applied without consulting the store, so an earlier rejection
		// of a different target for the same document does not turn the no-op into a skipped target.
		Assert.AreEqual(1, requestCount);
		Assert.AreEqual(WorkspaceEditTargetOutcome.NotApplied, result.TargetResults[0].Outcome);
		Assert.AreEqual(WorkspaceEditTargetOutcome.Applied, result.TargetResults[1].Outcome);
		Assert.IsNull(result.TargetResults[1].Failure);
	}

	[TestMethod]
	[DataRow("", DisplayName = "Empty")]
	[DataRow("   ", DisplayName = "Whitespace")]
	public void Apply_BlankTargetId_IsAnArgumentError(string targetId)
	{
		var applier = new WorkspaceEditApplier(_ => throw new InvalidOperationException("No replacement expected."));
		WorkspaceEditTargetPreparation target = CreateTarget("first.txt", "before", "after") with
		{
			TargetId = targetId
		};

		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => applier.Apply([target]));

		Assert.AreEqual("targets", exception.ParamName);
	}

	[TestMethod]
	public void Apply_CanceledBeforeFirstTarget_ReportsCanceledWithoutChanges()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		int replaceCount = 0;
		var applier = new WorkspaceEditApplier(_ =>
		{
			replaceCount++;
			throw new InvalidOperationException("No replacement expected.");
		});
		WorkspaceEditTargetPreparation target = CreateTarget("first.txt", "before", "after");

		WorkspaceEditApplicationResult result = applier.Apply([target], cancellation.Token);

		// Nothing was applied and no earlier failure exists, so the outcome is distinct from a partial
		// application: the outcome is Canceled and the target was not attempted.
		Assert.AreEqual(WorkspaceEditApplicationOutcome.Canceled, result.Outcome);
		Assert.AreEqual(0, replaceCount);
		Assert.AreEqual(WorkspaceEditTargetOutcome.NotApplied, result.TargetResults[0].Outcome);
		Assert.IsFalse(result.ChangeSet.HasChanges);
		Assert.IsEmpty(result.ChangedTargetIds);
		Assert.AreEqual(WorkspaceOperationFailureCodes.Canceled, result.Failure?.Code);
	}

	[TestMethod]
	public void Apply_CanceledTokenBeforeAnyTarget_LeavesEvenNoOpTargetsNotAttempted()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		int replaceCount = 0;
		var applier = new WorkspaceEditApplier(_ =>
		{
			replaceCount++;
			throw new InvalidOperationException("No replacement expected.");
		});
		WorkspaceEditTargetPreparation noOp = CreateTarget("same.txt", "content", "content");
		WorkspaceEditTargetPreparation changed = CreateTarget("other.txt", "before", "after");

		WorkspaceEditApplicationResult result = applier.Apply([noOp, changed], cancellation.Token);

		// The cancellation is observed before the first target is processed, so even the satisfied
		// no-op is reported as not attempted and the outcome stays Canceled without a change set.
		Assert.AreEqual(WorkspaceEditApplicationOutcome.Canceled, result.Outcome);
		Assert.AreEqual(0, replaceCount);
		Assert.AreEqual(WorkspaceEditTargetOutcome.NotApplied, result.TargetResults[0].Outcome);
		Assert.AreEqual(WorkspaceEditTargetOutcome.NotApplied, result.TargetResults[1].Outcome);
		Assert.IsFalse(result.ChangeSet.HasChanges);
	}

	[TestMethod]
	public void Apply_CancellationAfterNoChangeTarget_ReportsCanceledWithProcessedTarget()
	{
		using var cancellation = new CancellationTokenSource();
		var applier = new WorkspaceEditApplier(request =>
		{
			// The document already holds the requested content and the host cancels while the
			// replacement call is in flight, so no target transformed the document.
			cancellation.Cancel();
			return new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationOutcome.NoChange,
				request.Identity,
				CreateSnapshot(request.Content, 1, request.Identity.DocumentKey, request.Identity.DocumentId));
		});

		WorkspaceEditTargetPreparation first = CreateTarget("first.txt", "before", "after");
		WorkspaceEditTargetPreparation second = CreateTarget("second.txt", "old", "new");
		WorkspaceEditApplicationResult result = applier.Apply([first, second], cancellation.Token);

		// No failure and no recorded change: the processed target stays applied while the canceled
		// outcome carries no change set.
		Assert.AreEqual(WorkspaceEditApplicationOutcome.Canceled, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.Canceled, result.Failure!.Code);
		Assert.AreEqual(WorkspaceEditTargetOutcome.Applied, result.TargetResults[0].Outcome);
		Assert.AreEqual(WorkspaceEditTargetOutcome.NotApplied, result.TargetResults[1].Outcome);
		Assert.IsFalse(result.ChangeSet.HasChanges);
	}

	[TestMethod]
	public void Apply_FormatOnlyChange_ReachesStoreAndRecordsFormats()
	{
		var requests = new List<WorkspaceDocumentReplaceRequest>();
		var applier = new WorkspaceEditApplier(request =>
		{
			requests.Add(request);
			return new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationOutcome.Changed,
				request.Identity,
				CreateSnapshot(request.Content, 1, request.Identity.DocumentKey, request.Identity.DocumentId));
		});
		TextFileFormat bomFormat = new(TextEncodingKind.Utf8, true, TextNewlineStyle.Lf);
		WorkspaceEditTargetPreparation target = CreateTarget("same.txt", "content", "content", TestSnapshots.FileFormat, bomFormat);

		WorkspaceEditApplicationResult result = applier.Apply([target]);

		// A format-only change is a real change: the store is consulted and the change record carries
		// both formats even though the content is unchanged.
		Assert.AreEqual(WorkspaceEditApplicationOutcome.Completed, result.Outcome);
		Assert.AreEqual(1, requests.Count);
		Assert.AreEqual(1, result.PreparedOperationCount);
		Assert.AreEqual(1, result.ChangeSet.DocumentChanges.Count);
		Assert.AreEqual(TestSnapshots.FileFormat, result.ChangeSet.DocumentChanges[0].BeforeFileFormat);
		Assert.AreEqual(bomFormat, result.ChangeSet.DocumentChanges[0].AfterFileFormat);
	}

	[TestMethod]
	public void Apply_DelegateThrowsOperationCanceled_Propagates()
	{
		var applier = new WorkspaceEditApplier(_ => throw new OperationCanceledException("Host canceled."));

		Assert.ThrowsExactly<OperationCanceledException>(() => applier.Apply([CreateTarget("first.txt", "before", "after")]));
	}

	[TestMethod]
	[DataRow("", DisplayName = "Empty")]
	[DataRow("   ", DisplayName = "Whitespace")]
	public void Apply_BlankDocumentId_IsAnArgumentError(string documentId)
	{
		var applier = new WorkspaceEditApplier(_ => throw new InvalidOperationException("No replacement expected."));
		WorkspaceEditTargetPreparation target = CreateTarget("first.txt", "before", "after") with
		{
			Identity = new(new WorkspaceDocumentKey(Guid.NewGuid()), documentId, 4)
		};

		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => applier.Apply([target]));

		Assert.AreEqual("targets", exception.ParamName);
	}

	[TestMethod]
	public void Apply_UnencodableFormat_IsAnArgumentError()
	{
		var applier = new WorkspaceEditApplier(_ => throw new InvalidOperationException("No replacement expected."));
		WorkspaceEditTargetPreparation target = CreateTarget(
			"first.txt",
			"before",
			"after",
			afterFileFormat: new TextFileFormat(TextEncodingKind.Windows1252, true, TextNewlineStyle.Lf));

		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => applier.Apply([target]));

		Assert.AreEqual("targets[0].AfterFileFormat", exception.ParamName);
	}

	[TestMethod]
	public void Apply_UnrecognizedStoreOutcome_MarksUnknownWithOutcomeUnknownCode()
	{
		var applier = new WorkspaceEditApplier(request => new WorkspaceDocumentMutationResult(
			(WorkspaceDocumentMutationOutcome)42,
			request.Identity,
			null));

		WorkspaceEditApplicationResult result = applier.Apply([CreateTarget("first.txt", "before", "after")]);

		Assert.AreEqual(WorkspaceEditApplicationOutcome.PartiallyApplied, result.Outcome);
		Assert.AreEqual(WorkspaceEditTargetOutcome.Unknown, result.TargetResults[0].Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.TargetOutcomeUnknown, result.Failure?.Code);
		CollectionAssert.AreEqual(new[] { "first.txt" }, result.UnknownTargetIds.ToArray());
	}

	private static WorkspaceEditTargetPreparation CreateTarget(
		string targetId,
		string beforeContent,
		string afterContent,
		TextFileFormat? beforeFileFormat = null,
		TextFileFormat? afterFileFormat = null)
	{
		var documentKey = new WorkspaceDocumentKey(Guid.NewGuid());
		return new WorkspaceEditTargetPreparation
		{
			TargetId = targetId,
			Identity = new(documentKey, targetId, 4),
			BeforeContent = beforeContent,
			AfterContent = afterContent,
			BeforeFileFormat = beforeFileFormat ?? TestSnapshots.FileFormat,
			AfterFileFormat = afterFileFormat ?? TestSnapshots.FileFormat
		};
	}

	private static WorkspaceDocumentSnapshot CreateSnapshot(
		string content,
		long version,
		WorkspaceDocumentKey documentKey,
		string documentId)
		=> TestSnapshots.Create(documentId, content, version, documentKey);
}
