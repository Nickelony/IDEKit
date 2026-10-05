using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;
using Nickelony.IDEKit.Workspace.Replacement;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed class DocumentReplacementApplierTests
{
	[TestMethod]
	public async Task ApplyAsync_EmptyTargets_CompletesWithoutChanges()
	{
		DocumentReplacementApplier applier = CreateApplier(_ => throw new InvalidOperationException("No replacement expected."));

		DocumentReplacementResult result = await applier.ApplyAsync([], TestContext.CancellationToken);

		Assert.AreEqual(DocumentReplacementOutcome.Completed, result.Outcome);
		Assert.IsFalse(result.ChangeSet.HasChanges);
		Assert.IsEmpty(result.TargetResults);
		Assert.AreEqual(0, result.PreparedOperationCount);
	}

	[TestMethod]
	public async Task ApplyAsync_NoOpTargets_CompletesWithoutReplacement()
	{
		int replaceCount = 0;

		DocumentReplacementApplier applier = CreateApplier(_ =>
		{
			replaceCount++;
			throw new InvalidOperationException("No replacement expected.");
		});

		DocumentReplacementTarget target = CreateTarget("same.txt", "content", "content");
		DocumentReplacementResult result = await applier.ApplyAsync([target], TestContext.CancellationToken);

		Assert.AreEqual(DocumentReplacementOutcome.Completed, result.Outcome);
		Assert.IsFalse(result.ChangeSet.HasChanges);
		Assert.AreEqual(0, replaceCount);
		Assert.AreEqual(DocumentReplacementTargetOutcome.Applied, result.TargetResults[0].Outcome);

		// The store is not consulted for a no-op target and no earlier target of the document
		// recorded a version, so the actual version is unknown instead of assumed.
		Assert.IsNull(result.TargetResults[0].ActualVersion);
	}

	[TestMethod]
	public async Task ApplyAsync_NoOpAndChangedTargets_CountsOnlyChangedPreparations()
	{
		var requests = new List<WorkspaceDocumentReplaceRequest>();

		DocumentReplacementApplier applier = CreateApplier(request =>
		{
			requests.Add(request);

			return new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationOutcome.Changed,
				request.Identity,

				CreateSnapshot(
					content: request.Content,
					version: 1,
					documentKey: request.Identity.DocumentKey,
					documentId: request.Identity.DocumentId
				)
			);
		});

		DocumentReplacementTarget noOp = CreateTarget("noop.txt", "same", "same");
		DocumentReplacementTarget changed = CreateTarget("changed.txt", "before", "after");

		DocumentReplacementResult result = await applier.ApplyAsync([noOp, changed], TestContext.CancellationToken);

		// A no-op target prepares no replacement operations, so the aggregate count reflects only
		// the targets whose content actually differs.
		Assert.AreEqual(DocumentReplacementOutcome.Completed, result.Outcome);
		Assert.AreEqual(1, result.PreparedOperationCount);
		Assert.AreEqual(0, result.TargetResults[0].PreparedOperationCount);
		Assert.AreEqual(1, result.TargetResults[1].PreparedOperationCount);
		Assert.HasCount(1, result.ChangeSet.DocumentChanges);
		Assert.HasCount(1, requests);
	}

	[TestMethod]
	public async Task ApplyAsync_AllTargetsApplied_CompletesWithChangeSet()
	{
		var requests = new List<WorkspaceDocumentReplaceRequest>();
		long version = 0;

		DocumentReplacementApplier applier = CreateApplier(request =>
		{
			requests.Add(request);

			return new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationOutcome.Changed,
				request.Identity,

				CreateSnapshot(
					content: request.Content,
					version: ++version,
					documentKey: request.Identity.DocumentKey,
					documentId: request.Identity.DocumentId
				)
			);
		});

		DocumentReplacementTarget first = CreateTarget("first.txt", "before", "after");
		DocumentReplacementTarget second = CreateTarget("second.txt", "old", "new");

		DocumentReplacementResult result = await applier.ApplyAsync([first, second], TestContext.CancellationToken);

		Assert.AreEqual(DocumentReplacementOutcome.Completed, result.Outcome);
		Assert.IsTrue(result.ChangeSet.HasChanges);
		Assert.HasCount(2, result.TargetResults);

		Assert.AreSequenceEqual(
			["first.txt", "second.txt"],
			[.. result.ChangedDocumentIds],
			Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder
		);

		Assert.HasCount(2, requests);
		Assert.AreEqual("after", requests[0].Content);
		Assert.AreEqual("new", requests[1].Content);
		Assert.HasCount(2, result.ChangeSet.DocumentChanges);

		// The replacement delegate returns bumped snapshot versions, so the post-mutation version is
		// reported instead of the caller's pre-mutation expected version (4).
		Assert.AreEqual(4, result.TargetResults[0].ExpectedVersion);
		Assert.AreEqual(1, result.TargetResults[0].ActualVersion);
		Assert.AreEqual(4, result.TargetResults[1].ExpectedVersion);
		Assert.AreEqual(2, result.TargetResults[1].ActualVersion);
	}

	[TestMethod]
	public async Task ApplyAsync_StoreNoChange_MarksAppliedWithoutChangeRecord()
	{
		DocumentReplacementApplier applier = CreateApplier(request => new WorkspaceDocumentMutationResult(
			WorkspaceDocumentMutationOutcome.NoChange,
			request.Identity,
			CreateSnapshot(content: request.Content, version: 7, documentKey: request.Identity.DocumentKey, documentId: request.Identity.DocumentId)
		));

		DocumentReplacementResult result = await applier.ApplyAsync([CreateTarget("first.txt", "before", "after")], TestContext.CancellationToken);

		// The store already held the requested content, so the desired end state holds and the target
		// is applied; no change record is produced because the applier did not transform the document.
		Assert.AreEqual(DocumentReplacementOutcome.Completed, result.Outcome);
		Assert.AreEqual(DocumentReplacementTargetOutcome.Applied, result.TargetResults[0].Outcome);
		Assert.AreEqual(7, result.TargetResults[0].ActualVersion);
		Assert.IsEmpty(result.ChangedDocumentIds);
		Assert.IsEmpty(result.UnknownDocumentIds);
		Assert.IsFalse(result.ChangeSet.HasChanges);
		Assert.IsEmpty(result.ChangeSet.DocumentChanges);
	}

	[TestMethod]
	[DataRow(WorkspaceDocumentMutationOutcome.Changed, DisplayName = "Changed")]
	[DataRow(WorkspaceDocumentMutationOutcome.NoChange, DisplayName = "NoChange")]
	public async Task ApplyAsync_ResultWithoutSnapshotFallsBackToRequestedVersion(WorkspaceDocumentMutationOutcome outcome)
	{
		DocumentReplacementApplier applier = CreateApplier(request => new WorkspaceDocumentMutationResult(
			outcome,
			new(request.Identity.DocumentKey, request.Identity.DocumentId, 42),
			Snapshot: null
		));

		DocumentReplacementResult result = await applier.ApplyAsync([CreateTarget("first.txt", "before", "after")], TestContext.CancellationToken);

		// No snapshot reports the post-mutation version, so the applier falls back to the version the
		// store echoed as requested instead of the caller's pre-mutation expected version (4).
		Assert.AreEqual(DocumentReplacementTargetOutcome.Applied, result.TargetResults[0].Outcome);
		Assert.AreEqual(4, result.TargetResults[0].ExpectedVersion);
		Assert.AreEqual(42, result.TargetResults[0].ActualVersion);
	}

	[TestMethod]
	[DataRow(WorkspaceDocumentMutationOutcome.StaleDocument, DisplayName = "StaleDocument")]
	[DataRow(WorkspaceDocumentMutationOutcome.StaleDocumentInstance, DisplayName = "StaleDocumentInstance")]
	[DataRow(WorkspaceDocumentMutationOutcome.DocumentNotFound, DisplayName = "DocumentNotFound")]
	[DataRow(WorkspaceDocumentMutationOutcome.OperationInProgress, DisplayName = "OperationInProgress")]
	public async Task ApplyAsync_DeterministicRejection_MarksNotApplied(WorkspaceDocumentMutationOutcome outcome)
	{
		DocumentReplacementApplier applier = CreateApplier(request => new WorkspaceDocumentMutationResult(outcome, request.Identity, null));

		DocumentReplacementResult result = await applier.ApplyAsync([CreateTarget("first.txt", "before", "after")], TestContext.CancellationToken);

		Assert.AreEqual(DocumentReplacementTargetOutcome.NotApplied, result.TargetResults[0].Outcome);
		Assert.IsEmpty(result.UnknownDocumentIds);
		Assert.IsEmpty(result.ChangedDocumentIds);
		Assert.AreEqual(DocumentReplacementFailureCodes.TargetNotChanged, result.TargetResults[0].Failure?.Code);
	}

	[TestMethod]
	public async Task ApplyAsync_RejectedMutation_PartialResultMarksNotAppliedAndSkipsRemainder()
	{
		DocumentReplacementApplier applier = CreateApplier(request => new WorkspaceDocumentMutationResult(
			WorkspaceDocumentMutationOutcome.StaleDocument,
			request.Identity,
			null
		));

		DocumentReplacementTarget first = CreateTarget("first.txt", "before", "after");
		DocumentReplacementTarget second = CreateTarget("second.txt", "old", "new");

		DocumentReplacementResult result = await applier.ApplyAsync([first, second], TestContext.CancellationToken);

		Assert.AreEqual(DocumentReplacementOutcome.PartiallyApplied, result.Outcome);
		Assert.AreEqual(DocumentReplacementTargetOutcome.NotApplied, result.TargetResults[0].Outcome);
		Assert.AreEqual(DocumentReplacementTargetOutcome.NotApplied, result.TargetResults[1].Outcome);
		Assert.IsEmpty(result.UnknownDocumentIds);
		Assert.IsEmpty(result.ChangedDocumentIds);
		Assert.AreEqual(DocumentReplacementFailureCodes.TargetNotChanged, result.Failure?.Code);

		// Both replacements were prepared; the unreached target still reports its prepared operation.
		Assert.AreEqual(2, result.PreparedOperationCount);
		Assert.AreEqual(1, result.TargetResults[1].PreparedOperationCount);
	}

	[TestMethod]
	public async Task ApplyAsync_ReplaceThrows_PartialResultMarksUnknown()
	{
		DocumentReplacementApplier applier = CreateApplier(_ => throw new InvalidOperationException("Store unavailable."));

		DocumentReplacementTarget first = CreateTarget("first.txt", "before", "after");
		DocumentReplacementTarget second = CreateTarget("second.txt", "old", "new");

		DocumentReplacementResult result = await applier.ApplyAsync([first, second], TestContext.CancellationToken);

		Assert.AreEqual(DocumentReplacementOutcome.PartiallyApplied, result.Outcome);
		Assert.AreEqual(DocumentReplacementTargetOutcome.Unknown, result.TargetResults[0].Outcome);
		Assert.AreEqual(DocumentReplacementTargetOutcome.NotApplied, result.TargetResults[1].Outcome);
		Assert.AreSequenceEqual(["first.txt"], [.. result.UnknownDocumentIds]);
		Assert.AreEqual(DocumentReplacementFailureCodes.TargetApplicationFailed, result.Failure?.Code);
		Assert.AreEqual("Store unavailable.", result.Failure?.Message);
	}

	[TestMethod]
	public async Task ApplyAsync_FailureAfterAppliedTarget_RetainsConfirmedChanges()
	{
		var requests = new List<WorkspaceDocumentReplaceRequest>();

		DocumentReplacementApplier applier = CreateApplier(request =>
		{
			requests.Add(request);

			if (request.Identity.DocumentId == "second.txt")
				return new WorkspaceDocumentMutationResult(WorkspaceDocumentMutationOutcome.OperationInProgress, request.Identity, null);

			return new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationOutcome.Changed,
				request.Identity,

				CreateSnapshot(
					content: request.Content,
					version: 1,
					documentKey: request.Identity.DocumentKey,
					documentId: request.Identity.DocumentId
				)
			);
		});

		DocumentReplacementTarget first = CreateTarget("first.txt", "before", "after");
		DocumentReplacementTarget second = CreateTarget("second.txt", "old", "new");

		DocumentReplacementResult result = await applier.ApplyAsync([first, second], TestContext.CancellationToken);

		Assert.AreEqual(DocumentReplacementOutcome.PartiallyApplied, result.Outcome);
		Assert.AreEqual(DocumentReplacementTargetOutcome.Applied, result.TargetResults[0].Outcome);
		Assert.AreEqual(DocumentReplacementTargetOutcome.NotApplied, result.TargetResults[1].Outcome);
		Assert.Contains("first.txt", [.. result.ChangedDocumentIds]);
		Assert.IsEmpty(result.UnknownDocumentIds);
		Assert.HasCount(1, result.ChangeSet.DocumentChanges);
		Assert.AreEqual("before", result.ChangeSet.DocumentChanges[0].BeforeContent);
		Assert.AreEqual("after", result.ChangeSet.DocumentChanges[0].AfterContent);
	}

	[TestMethod]
	public void ApplyAsync_NullTargetsIsAnArgumentError()
	{
		DocumentReplacementApplier applier = CreateApplier(_ => throw new InvalidOperationException("No replacement expected."));
		Assert.ThrowsExactly<ArgumentNullException>(() => applier.ApplyAsync(null!, TestContext.CancellationToken));
	}

	[TestMethod]
	public void Constructor_NullReplacerIsAnArgumentError() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new DocumentReplacementApplier(null!));

	[TestMethod]
	public void ApplyAsync_NullTargetEntryIsAnArgumentError()
	{
		DocumentReplacementApplier applier = CreateApplier(_ => throw new InvalidOperationException("No replacement expected."));

		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
			applier.ApplyAsync([CreateTarget("first.txt", "before", "after"), null!], TestContext.CancellationToken)
		);

		Assert.AreEqual("targets", exception.ParamName);
	}

	[TestMethod]
	public async Task ApplyAsync_UsesConfiguredPathComparisonForChangedDocumentIds()
	{
		long version = 0;

		DocumentReplacementApplier caseInsensitive = CreateApplier(
			request => new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationOutcome.Changed,
				request.Identity,

				CreateSnapshot(
					content: request.Content,
					version: ++version,
					documentKey: request.Identity.DocumentKey,
					documentId: request.Identity.DocumentId
				)
			),

			new WorkspaceOptions { PathComparison = LocalPathComparisonPolicy.CaseInsensitive }
		);

		DocumentReplacementApplier caseSensitive = CreateApplier(
			request => new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationOutcome.Changed,
				request.Identity,

				CreateSnapshot(
					content: request.Content,
					version: ++version,
					documentKey: request.Identity.DocumentKey,
					documentId: request.Identity.DocumentId
				)
			),

			new WorkspaceOptions { PathComparison = LocalPathComparisonPolicy.CaseSensitive }
		);

		DocumentReplacementTarget first = CreateTarget("Doc.lua", "before", "after");
		DocumentReplacementTarget second = CreateTarget("doc.lua", "old", "new");

		DocumentReplacementResult insensitive = await caseInsensitive.ApplyAsync([first, second], TestContext.CancellationToken);
		DocumentReplacementResult sensitive = await caseSensitive.ApplyAsync([first, second], TestContext.CancellationToken);

		Assert.AreSequenceEqual(["Doc.lua"], [.. insensitive.ChangedDocumentIds]);
		Assert.AreSequenceEqual(["Doc.lua", "doc.lua"], [.. sensitive.ChangedDocumentIds]);
	}

	[TestMethod]
	public async Task ApplyAsync_TwoTargetsForSameDocument_AppliesBothWithRefreshedExpectedVersion()
	{
		var requests = new List<WorkspaceDocumentReplaceRequest>();
		long version = 0;

		DocumentReplacementApplier applier = CreateApplier(request =>
		{
			requests.Add(request);

			return new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationOutcome.Changed,
				request.Identity,

				CreateSnapshot(
					content: request.Content,
					version: ++version,
					documentKey: request.Identity.DocumentKey,
					documentId: request.Identity.DocumentId
				)
			);
		});

		DocumentReplacementTarget first = CreateTarget("same.txt", "one", "two");
		DocumentReplacementTarget second = CreateTarget("same.txt", "two", "three");

		DocumentReplacementResult result = await applier.ApplyAsync([first, second], TestContext.CancellationToken);

		// Both targets belong to the same document; the applier refreshes the expected version from
		// the first result, so the second replacement is not rejected with a stale version.
		Assert.AreEqual(DocumentReplacementOutcome.Completed, result.Outcome);
		Assert.AreEqual(DocumentReplacementTargetOutcome.Applied, result.TargetResults[0].Outcome);
		Assert.AreEqual(DocumentReplacementTargetOutcome.Applied, result.TargetResults[1].Outcome);
		Assert.HasCount(2, requests);
		Assert.AreEqual(4, requests[0].Identity.Version);
		Assert.AreEqual(1, requests[1].Identity.Version);
		Assert.HasCount(2, result.ChangeSet.DocumentChanges);
	}

	[TestMethod]
	public async Task ApplyAsync_NoOpTargetAfterAppliedTargetOfSameDocument_ReportsRecordedVersion()
	{
		long version = 0;

		DocumentReplacementApplier applier = CreateApplier(request => new WorkspaceDocumentMutationResult(
			WorkspaceDocumentMutationOutcome.Changed,
			request.Identity,

			CreateSnapshot(
				content: request.Content,
				version: ++version,
				documentKey: request.Identity.DocumentKey,
				documentId: request.Identity.DocumentId
			)
		));

		DocumentReplacementTarget applied = CreateTarget("same.txt", "before", "after");
		DocumentReplacementTarget noOp = CreateTarget("same.txt", "after", "after");

		DocumentReplacementResult result = await applier.ApplyAsync([applied, noOp], TestContext.CancellationToken);

		// The no-op target is not sent to the store, but the earlier target of the same document
		// recorded a version, so that version is reported instead of an unknown one.
		Assert.AreEqual(DocumentReplacementOutcome.Completed, result.Outcome);
		Assert.AreEqual(1, result.TargetResults[0].ActualVersion);
		Assert.AreEqual(1, result.TargetResults[1].ActualVersion);
		Assert.IsNull(result.TargetResults[1].Failure);
		Assert.HasCount(1, result.ChangeSet.DocumentChanges);
	}

	[TestMethod]
	public async Task ApplyAsync_CanceledBeforeNextTarget_PartialResultMarksRemainderNotApplied()
	{
		using var cancellation = new CancellationTokenSource();
		long version = 0;

		DocumentReplacementApplier applier = CreateApplier(request =>
		{
			cancellation.Cancel();

			return new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationOutcome.Changed,
				request.Identity,

				CreateSnapshot(
					content: request.Content,
					version: ++version,
					documentKey: request.Identity.DocumentKey,
					documentId: request.Identity.DocumentId
				)
			);
		});

		DocumentReplacementTarget first = CreateTarget("first.txt", "before", "after");
		DocumentReplacementTarget second = CreateTarget("second.txt", "old", "new");

		DocumentReplacementResult result = await applier.ApplyAsync([first, second], cancellation.Token);

		// The confirmed change is retained; the remaining targets are not attempted and the result
		// reports the cancellation as its failure.
		Assert.AreEqual(DocumentReplacementOutcome.PartiallyApplied, result.Outcome);
		Assert.AreEqual(DocumentReplacementTargetOutcome.Applied, result.TargetResults[0].Outcome);
		Assert.AreEqual(DocumentReplacementTargetOutcome.NotApplied, result.TargetResults[1].Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.Canceled, result.Failure?.Code);
		Assert.HasCount(1, result.ChangeSet.DocumentChanges);
	}

	[TestMethod]
	public async Task ApplyAsync_ContinueOnFailure_AttemptsEveryTargetAndReportsFirstFailure()
	{
		var requests = new List<WorkspaceDocumentReplaceRequest>();

		DocumentReplacementApplier applier = CreateApplier(
			request =>
			{
				requests.Add(request);

				return request.Identity.DocumentId.EndsWith("first.txt", StringComparison.Ordinal)
					? new WorkspaceDocumentMutationResult(WorkspaceDocumentMutationOutcome.StaleDocument, request.Identity, null)
					: new WorkspaceDocumentMutationResult(
						WorkspaceDocumentMutationOutcome.Changed,
						request.Identity,

						CreateSnapshot(
							content: request.Content,
							version: 1,
							documentKey: request.Identity.DocumentKey,
							documentId: request.Identity.DocumentId
						)
					);
			},

			continueOnFailure: true
		);

		DocumentReplacementTarget first = CreateTarget("first.txt", "before", "after");
		DocumentReplacementTarget second = CreateTarget("second.txt", "old", "new");

		DocumentReplacementResult result = await applier.ApplyAsync([first, second], TestContext.CancellationToken);

		// Every target is attempted; the first failure is reported while the confirmed change and
		// the later applied target remain in the result.
		Assert.AreEqual(DocumentReplacementOutcome.PartiallyApplied, result.Outcome);
		Assert.HasCount(2, requests);
		Assert.AreEqual(DocumentReplacementTargetOutcome.NotApplied, result.TargetResults[0].Outcome);
		Assert.AreEqual(DocumentReplacementTargetOutcome.Applied, result.TargetResults[1].Outcome);
		Assert.AreEqual(DocumentReplacementFailureCodes.TargetNotChanged, result.Failure?.Code);
		Assert.HasCount(1, result.ChangeSet.DocumentChanges);
	}

	[TestMethod]
	public async Task ApplyAsync_TargetRejectedWithNewerSnapshot_LaterTargetOfSameDocumentIsNotApplied()
	{
		var requests = new List<WorkspaceDocumentReplaceRequest>();

		DocumentReplacementApplier applier = CreateApplier(
			request =>
			{
				requests.Add(request);

				// The store returns a rejection together with a current-state snapshot whose version is
				// newer than the request: the applier did not observe that state.
				return new WorkspaceDocumentMutationResult(
					WorkspaceDocumentMutationOutcome.StaleDocument,
					request.Identity,

					CreateSnapshot(
						content: "other writer",
						version: 9,
						documentKey: request.Identity.DocumentKey,
						documentId: request.Identity.DocumentId
					)
				);
			},

			continueOnFailure: true
		);

		DocumentReplacementTarget first = CreateTarget("same.txt", "one", "two");
		DocumentReplacementTarget second = CreateTarget("same.txt", "two", "three");

		DocumentReplacementResult result = await applier.ApplyAsync([first, second], TestContext.CancellationToken);

		// The rejection snapshot must not authorize the second write: the second target is prepared
		// against state the applier did not confirm, so only the first request reaches the store.
		Assert.AreEqual(DocumentReplacementOutcome.PartiallyApplied, result.Outcome);
		Assert.HasCount(1, requests);
		Assert.AreEqual(4, requests[0].Identity.Version);
		Assert.AreEqual(DocumentReplacementTargetOutcome.NotApplied, result.TargetResults[0].Outcome);
		Assert.AreEqual(DocumentReplacementTargetOutcome.NotApplied, result.TargetResults[1].Outcome);
		Assert.IsNull(result.TargetResults[1].ActualVersion);
		Assert.AreEqual(DocumentReplacementFailureCodes.TargetSkipped, result.TargetResults[1].Failure?.Code);
		Assert.IsFalse(result.ChangeSet.HasChanges);
		Assert.IsEmpty(result.ChangeSet.DocumentChanges);
	}

	[TestMethod]
	public async Task ApplyAsync_NoOpTargetAfterRejectedTargetOfSameDocument_IsStillAppliedWithoutTheStore()
	{
		int requestCount = 0;

		DocumentReplacementApplier applier = CreateApplier(
			request =>
			{
				requestCount++;
				return new WorkspaceDocumentMutationResult(WorkspaceDocumentMutationOutcome.StaleDocument, request.Identity, null);
			},

			continueOnFailure: true
		);

		DocumentReplacementTarget rejected = CreateTarget("same.txt", "one", "two");
		DocumentReplacementTarget noOp = CreateTarget("same.txt", "two", "two");

		DocumentReplacementResult result = await applier.ApplyAsync([rejected, noOp], TestContext.CancellationToken);

		// A target that would write nothing is applied without consulting the store, so an earlier rejection
		// of a different target for the same document does not turn the no-op into a skipped target.
		Assert.AreEqual(1, requestCount);
		Assert.AreEqual(DocumentReplacementTargetOutcome.NotApplied, result.TargetResults[0].Outcome);
		Assert.AreEqual(DocumentReplacementTargetOutcome.Applied, result.TargetResults[1].Outcome);
		Assert.IsNull(result.TargetResults[1].Failure);
	}

	[TestMethod]
	public async Task ApplyAsync_CanceledBeforeFirstTarget_ReportsCanceledWithoutChanges()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		int replaceCount = 0;

		DocumentReplacementApplier applier = CreateApplier(_ =>
		{
			replaceCount++;
			throw new InvalidOperationException("No replacement expected.");
		});

		DocumentReplacementTarget target = CreateTarget("first.txt", "before", "after");

		DocumentReplacementResult result = await applier.ApplyAsync([target], cancellation.Token);

		// Nothing was applied and no earlier failure exists, so the outcome is distinct from a partial
		// application: the outcome is Canceled and the target was not attempted.
		Assert.AreEqual(DocumentReplacementOutcome.Canceled, result.Outcome);
		Assert.AreEqual(0, replaceCount);
		Assert.AreEqual(DocumentReplacementTargetOutcome.NotApplied, result.TargetResults[0].Outcome);
		Assert.IsFalse(result.ChangeSet.HasChanges);
		Assert.IsEmpty(result.ChangedDocumentIds);
		Assert.AreEqual(WorkspaceOperationFailureCodes.Canceled, result.Failure?.Code);
	}

	[TestMethod]
	public async Task ApplyAsync_CanceledTokenBeforeAnyTarget_LeavesEvenNoOpTargetsNotAttempted()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		int replaceCount = 0;

		DocumentReplacementApplier applier = CreateApplier(_ =>
		{
			replaceCount++;
			throw new InvalidOperationException("No replacement expected.");
		});

		DocumentReplacementTarget noOp = CreateTarget("same.txt", "content", "content");
		DocumentReplacementTarget changed = CreateTarget("other.txt", "before", "after");

		DocumentReplacementResult result = await applier.ApplyAsync([noOp, changed], cancellation.Token);

		// The cancellation is observed before the first target is processed, so even the satisfied
		// no-op is reported as not attempted and the outcome stays Canceled without a change set.
		Assert.AreEqual(DocumentReplacementOutcome.Canceled, result.Outcome);
		Assert.AreEqual(0, replaceCount);
		Assert.AreEqual(DocumentReplacementTargetOutcome.NotApplied, result.TargetResults[0].Outcome);
		Assert.AreEqual(DocumentReplacementTargetOutcome.NotApplied, result.TargetResults[1].Outcome);
		Assert.IsFalse(result.ChangeSet.HasChanges);
	}

	[TestMethod]
	public async Task ApplyAsync_CancellationAfterNoChangeTarget_ReportsCanceledWithProcessedTarget()
	{
		using var cancellation = new CancellationTokenSource();

		DocumentReplacementApplier applier = CreateApplier(request =>
		{
			// The document already holds the requested content and the host cancels while the
			// replacement call is in flight, so no target transformed the document.
			cancellation.Cancel();

			return new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationOutcome.NoChange,
				request.Identity,

				CreateSnapshot(
					content: request.Content,
					version: 1,
					documentKey: request.Identity.DocumentKey,
					documentId: request.Identity.DocumentId
				)
			);
		});

		DocumentReplacementTarget first = CreateTarget("first.txt", "before", "after");
		DocumentReplacementTarget second = CreateTarget("second.txt", "old", "new");

		DocumentReplacementResult result = await applier.ApplyAsync([first, second], cancellation.Token);

		// No target failed and no change was recorded: the processed target stays applied while the canceled
		// outcome carries no change set.
		Assert.AreEqual(DocumentReplacementOutcome.Canceled, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.Canceled, result.Failure!.Code);
		Assert.AreEqual(DocumentReplacementTargetOutcome.Applied, result.TargetResults[0].Outcome);
		Assert.AreEqual(DocumentReplacementTargetOutcome.NotApplied, result.TargetResults[1].Outcome);
		Assert.IsFalse(result.ChangeSet.HasChanges);
	}

	[TestMethod]
	public async Task ApplyAsync_FormatOnlyChange_ReachesStoreAndRecordsFormats()
	{
		var requests = new List<WorkspaceDocumentReplaceRequest>();

		DocumentReplacementApplier applier = CreateApplier(request =>
		{
			requests.Add(request);

			return new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationOutcome.Changed,
				request.Identity,

				CreateSnapshot(
					content: request.Content,
					version: 1,
					documentKey: request.Identity.DocumentKey,
					documentId: request.Identity.DocumentId
				)
			);
		});

		TextFileFormat bomFormat = new(TextEncoding.Utf8, true, LineTerminatorStyle.Lf);

		DocumentReplacementTarget target = CreateTarget(
			documentId: "same.txt",
			beforeContent: "content",
			afterContent: "content",
			beforeFileFormat: TestSnapshots.FileFormat,
			afterFileFormat: bomFormat
		);

		DocumentReplacementResult result = await applier.ApplyAsync([target], TestContext.CancellationToken);

		// A format-only change is a real change: the store is consulted and the change record carries
		// both formats even though the content is unchanged.
		Assert.AreEqual(DocumentReplacementOutcome.Completed, result.Outcome);
		Assert.HasCount(1, requests);
		Assert.AreEqual(1, result.PreparedOperationCount);
		Assert.HasCount(1, result.ChangeSet.DocumentChanges);
		Assert.AreEqual(TestSnapshots.FileFormat, result.ChangeSet.DocumentChanges[0].BeforeFileFormat);
		Assert.AreEqual(bomFormat, result.ChangeSet.DocumentChanges[0].AfterFileFormat);
	}

	[TestMethod]
	public async Task ApplyAsync_DelegateThrowsOperationCanceled_Propagates()
	{
		DocumentReplacementApplier applier = CreateApplier(_ => throw new OperationCanceledException("Host canceled."));

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
			applier.ApplyAsync([CreateTarget("first.txt", "before", "after")], TestContext.CancellationToken)
		);
	}

	[TestMethod]
	[DataRow("", DisplayName = "Empty")]
	[DataRow("   ", DisplayName = "Whitespace")]
	public void ApplyAsync_BlankDocumentId_IsAnArgumentError(string documentId)
	{
		DocumentReplacementApplier applier = CreateApplier(_ => throw new InvalidOperationException("No replacement expected."));

		DocumentReplacementTarget target = CreateTarget("first.txt", "before", "after") with
		{
			Identity = new(new WorkspaceDocumentKey(Guid.NewGuid()), documentId, 4),
		};

		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => applier.ApplyAsync([target], TestContext.CancellationToken));

		Assert.AreEqual("targets", exception.ParamName);
	}

	[TestMethod]
	public void ApplyAsync_UnencodableFormat_IsAnArgumentError()
	{
		DocumentReplacementApplier applier = CreateApplier(_ => throw new InvalidOperationException("No replacement expected."));

		DocumentReplacementTarget target = CreateTarget(
			documentId: "first.txt",
			beforeContent: "before",
			afterContent: "after",
			afterFileFormat: new TextFileFormat(TextEncoding.Windows1252, true, LineTerminatorStyle.Lf)
		);

		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => applier.ApplyAsync([target], TestContext.CancellationToken));

		Assert.AreEqual("targets[0].AfterFileFormat", exception.ParamName);
	}

	[TestMethod]
	public async Task ApplyAsync_UnrecognizedStoreOutcome_MarksUnknownWithOutcomeUnknownCode()
	{
		DocumentReplacementApplier applier = CreateApplier(request => new WorkspaceDocumentMutationResult(
			(WorkspaceDocumentMutationOutcome)42,
			request.Identity,
			null
		));

		DocumentReplacementResult result = await applier.ApplyAsync([CreateTarget("first.txt", "before", "after")], TestContext.CancellationToken);

		Assert.AreEqual(DocumentReplacementOutcome.PartiallyApplied, result.Outcome);
		Assert.AreEqual(DocumentReplacementTargetOutcome.Unknown, result.TargetResults[0].Outcome);
		Assert.AreEqual(DocumentReplacementFailureCodes.TargetOutcomeUnknown, result.Failure?.Code);
		Assert.AreSequenceEqual(["first.txt"], [.. result.UnknownDocumentIds]);
	}

	[TestMethod]
	public async Task ApplyAsync_PassesItsTokenToEachReplacement()
	{
		using var cancellation = new CancellationTokenSource();
		List<CancellationToken> tokens = [];

		var applier = new DocumentReplacementApplier(
			(request, cancellationToken) =>
			{
				tokens.Add(cancellationToken);

				return Task.FromResult(
					Reached(new WorkspaceDocumentMutationResult(WorkspaceDocumentMutationOutcome.NoChange, request.Identity, null))
				);
			}
		);

		await applier.ApplyAsync([CreateTarget("first.txt", "before", "after"), CreateTarget("second.txt", "old", "new")], cancellation.Token);

		Assert.AreSequenceEqual([cancellation.Token, cancellation.Token], tokens);
	}

	[TestMethod]
	public async Task ApplyAsync_ReplacementThatThrowsBeforeReturningATask_MarksUnknown()
	{
		var applier = new DocumentReplacementApplier((_, _) => throw new InvalidOperationException("Store unavailable."));

		DocumentReplacementResult result = await applier.ApplyAsync([CreateTarget("first.txt", "before", "after")], TestContext.CancellationToken);

		Assert.AreEqual(DocumentReplacementTargetOutcome.Unknown, result.TargetResults[0].Outcome);
		Assert.AreEqual(DocumentReplacementFailureCodes.TargetApplicationFailed, result.Failure?.Code);
	}

	[TestMethod]
	public async Task ApplyAsync_SecondTargetOfADocument_WaitsForTheFirstReplacementAndUsesItsVersion()
	{
		var firstReplacement = new TaskCompletionSource<DocumentReplacementAttemptResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		List<WorkspaceDocumentReplaceRequest> requests = [];

		var applier = new DocumentReplacementApplier(
			(request, _) =>
			{
				requests.Add(request);

				return requests.Count == 1
					? firstReplacement.Task
					: Task.FromResult(Reached(new WorkspaceDocumentMutationResult(WorkspaceDocumentMutationOutcome.Changed, request.Identity, null)));
			}
		);

		DocumentReplacementTarget first = CreateTarget("same.txt", "one", "two");

		Task<DocumentReplacementResult> applying = applier.ApplyAsync(
			[first, CreateTarget("same.txt", "two", "three")],
			TestContext.CancellationToken
		);

		Assert.HasCount(1, requests);

		firstReplacement.SetResult(
			Reached(
				new WorkspaceDocumentMutationResult(
					WorkspaceDocumentMutationOutcome.Changed,
					first.Identity,
					CreateSnapshot(content: "two", version: 5, documentKey: first.Identity.DocumentKey, documentId: "same.txt")
				)
			)
		);

		DocumentReplacementResult result = await applying;

		Assert.AreEqual(DocumentReplacementOutcome.Completed, result.Outcome);
		Assert.HasCount(2, requests);
		Assert.AreEqual(5, requests[1].Identity.Version);
	}

	[TestMethod]
	public async Task ApplyAsync_ARefusedTarget_IsNotAppliedWithTheRefusalAndStopsTheApplication()
	{
		var refusal = new WorkspaceOperationFailure("Refused", "The views hold edits.");
		int requestCount = 0;

		var applier = new DocumentReplacementApplier(
			(_, _) =>
			{
				requestCount++;
				return Task.FromResult(DocumentReplacementAttemptResult.Refused(refusal));
			}
		);

		DocumentReplacementResult result = await applier.ApplyAsync(
			[CreateTarget("first.txt", "before", "after"), CreateTarget("second.txt", "old", "new")],
			TestContext.CancellationToken
		);

		Assert.AreEqual(1, requestCount);
		Assert.AreEqual(DocumentReplacementOutcome.PartiallyApplied, result.Outcome);

		Assert.AreSequenceEqual(
			[DocumentReplacementTargetOutcome.NotApplied, DocumentReplacementTargetOutcome.NotApplied],
			result.TargetResults.Select(target => target.Outcome)
		);

		Assert.AreSame(refusal, result.TargetResults[0].Failure);
		Assert.AreSame(refusal, result.Failure);
		Assert.IsEmpty(result.UnknownDocumentIds);
	}

	[TestMethod]
	public async Task ApplyAsync_TheDelegateObservesTheCanceledTokenOfTheApplier_TheTargetIsNotAppliedAndTheResultIsPartial()
	{
		using var cancellation = new CancellationTokenSource();
		int requestCount = 0;

		var applier = new DocumentReplacementApplier(
			async (request, cancellationToken) =>
			{
				if (++requestCount == 2)
				{
					await cancellation.CancelAsync();
					cancellationToken.ThrowIfCancellationRequested();
				}

				return Reached(new WorkspaceDocumentMutationResult(WorkspaceDocumentMutationOutcome.Changed, request.Identity, null));
			}
		);

		DocumentReplacementResult result = await applier.ApplyAsync(
			[CreateTarget("first.txt", "before", "after"), CreateTarget("second.txt", "old", "new"), CreateTarget("third.txt", "a", "b")],
			cancellation.Token
		);

		Assert.AreEqual(DocumentReplacementOutcome.PartiallyApplied, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.Canceled, result.Failure?.Code);

		Assert.AreSequenceEqual(
			[DocumentReplacementTargetOutcome.Applied, DocumentReplacementTargetOutcome.NotApplied, DocumentReplacementTargetOutcome.NotApplied],
			result.TargetResults.Select(target => target.Outcome)
		);

		Assert.AreSequenceEqual(["first.txt"], result.ChangedDocumentIds);
	}

	[TestMethod]
	public async Task ApplyAsync_TheDelegateObservesTheCanceledTokenBeforeAnyChange_TheResultIsCanceled()
	{
		using var cancellation = new CancellationTokenSource();

		var applier = new DocumentReplacementApplier(
			async (_, cancellationToken) =>
			{
				await cancellation.CancelAsync();
				cancellationToken.ThrowIfCancellationRequested();
				throw new InvalidOperationException("Not reached.");
			}
		);

		DocumentReplacementResult result = await applier.ApplyAsync([CreateTarget("first.txt", "before", "after")], cancellation.Token);

		Assert.AreEqual(DocumentReplacementOutcome.Canceled, result.Outcome);
		Assert.AreEqual(DocumentReplacementTargetOutcome.NotApplied, result.TargetResults[0].Outcome);
	}

	[TestMethod]
	public async Task ApplyAsync_AnOperationCanceledExceptionOfAnotherCanceledToken_Propagates()
	{
		using var other = new CancellationTokenSource();
		await other.CancelAsync();

		var applier = new DocumentReplacementApplier(
			(_, _) => Task.FromException<DocumentReplacementAttemptResult>(new OperationCanceledException(other.Token))
		);

		await Assert.ThrowsAsync<OperationCanceledException>(() =>
			applier.ApplyAsync([CreateTarget("first.txt", "before", "after")], TestContext.CancellationToken)
		);
	}

	private static DocumentReplacementAttemptResult Reached(WorkspaceDocumentMutationResult result) =>
		DocumentReplacementAttemptResult.Reached(result);

	private static DocumentReplacementTarget CreateTarget(
		string documentId,
		string beforeContent,
		string afterContent,
		TextFileFormat? beforeFileFormat = null,
		TextFileFormat? afterFileFormat = null
	)
	{
		var documentKey = new WorkspaceDocumentKey(Guid.NewGuid());

		return new DocumentReplacementTarget
		{
			Identity = new(documentKey, documentId, 4),
			BeforeContent = beforeContent,
			AfterContent = afterContent,
			BeforeFileFormat = beforeFileFormat ?? TestSnapshots.FileFormat,
			AfterFileFormat = afterFileFormat ?? TestSnapshots.FileFormat,
		};
	}

	private static WorkspaceDocumentSnapshot CreateSnapshot(string content, long version, WorkspaceDocumentKey documentKey, string documentId) =>
		TestSnapshots.Create(documentId, content, version, documentKey);

	[TestMethod]
	public void ApplierOptions_Default_StopsAtTheFirstFailure()
	{
		Assert.IsFalse(DocumentReplacementApplierOptions.Default.ContinueOnFailure);
		Assert.IsTrue(new DocumentReplacementApplierOptions { ContinueOnFailure = true }.ContinueOnFailure);
	}

	private static DocumentReplacementApplier CreateApplier(
		Func<WorkspaceDocumentReplaceRequest, WorkspaceDocumentMutationResult> replace,
		WorkspaceOptions? options = null,
		bool continueOnFailure = false
	)
	{
		// Each replacement completes after a yield, so the applier never depends on a replacement that completes synchronously.
		return new DocumentReplacementApplier(
			async (request, _) =>
			{
				await Task.Yield();
				return Reached(replace(request));
			},

			options,
			new DocumentReplacementApplierOptions { ContinueOnFailure = continueOnFailure }
		);
	}

	public TestContext? TestContext { get; set; }
}
