using System.Globalization;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Replacement;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed class DocumentReplacementChangeSetResultTests
{
	private static DocumentReplacementResult CreateResult(
		IReadOnlyList<string> changedDocumentIds,
		IReadOnlyList<string> unknownDocumentIds,
		WorkspaceOptions? options = null
	)
	{
		return unknownDocumentIds.Count == 0
			? DocumentReplacementResult.Completed(targetResults: [], CreateChangeSet(changedDocumentIds), options)
			: DocumentReplacementResult.PartiallyApplied(
				targetResults: [],
				unknownDocumentIds,
				failure: new WorkspaceOperationFailure(DocumentReplacementFailureCodes.TargetNotChanged, "The target did not change."),
				changeSet: CreateChangeSet(changedDocumentIds),
				options
			);
	}

	// The changed target ids are derived from the change set, so a requested id list is expressed
	// as a change set carrying one change per id.
	private static DocumentReplacementChangeSet CreateChangeSet(IReadOnlyList<string> changedDocumentIds)
	{
		return new([
			.. changedDocumentIds.Select(documentId => new DocumentReplacementChange
			{
				DocumentId = documentId,
				BeforeContent = "before",
				AfterContent = "after",
				BeforeFileFormat = TestSnapshots.FileFormat,
				AfterFileFormat = TestSnapshots.FileFormat,
			}),
		]);
	}

	[TestMethod]
	public void ChangedDocumentIds_DeduplicateWithSuppliedComparerPreservingFirstOccurrence()
	{
		DocumentReplacementResult result = CreateResult(
			["Doc.lua", "doc.lua", "other.lua"],
			[],
			new WorkspaceOptions { PathComparison = LocalPathComparisonPolicy.CaseInsensitive }
		);

		Assert.AreSequenceEqual(["Doc.lua", "other.lua"], [.. result.ChangedDocumentIds]);
	}

	[TestMethod]
	public void ChangedDocumentIds_DefaultComparisonFollowsTheOperatingSystem()
	{
		DocumentReplacementResult result = CreateResult(["Doc.lua", "doc.lua"], []);

		// The default is the current platform's usual file-system semantics: case-insensitive on
		// Windows and macOS, ordinal elsewhere.
		string[] expected = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? ["Doc.lua"] : ["Doc.lua", "doc.lua"];
		Assert.AreSequenceEqual(expected, [.. result.ChangedDocumentIds]);
	}

	[TestMethod]
	public void ChangedDocumentIds_CaseSensitivePolicyKeepsCaseVariantsDistinct()
	{
		DocumentReplacementResult result = CreateResult(
			["Doc.lua", "doc.lua"],
			[],
			new WorkspaceOptions { PathComparison = LocalPathComparisonPolicy.CaseSensitive }
		);

		Assert.AreSequenceEqual(["Doc.lua", "doc.lua"], [.. result.ChangedDocumentIds]);
	}

	[TestMethod]
	[DoNotParallelize]
	public void ChangedDocumentIds_DeduplicateOrdinallyNotCultureSensitively()
	{
		CultureInfo originalCulture = CultureInfo.CurrentCulture;

		try
		{
			CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

			DocumentReplacementResult result = CreateResult(
				["I.lua", "\u0131.lua"],
				[],
				new WorkspaceOptions { PathComparison = LocalPathComparisonPolicy.CaseInsensitive }
			);

			// Ordinal comparison keeps 'I' and 'ı' distinct, unlike Turkish-culture casing rules.
			Assert.AreSequenceEqual(["I.lua", "\u0131.lua"], [.. result.ChangedDocumentIds]);
		}
		finally
		{
			CultureInfo.CurrentCulture = originalCulture;
		}
	}

	[TestMethod]
	public void UnknownDocumentIds_DeduplicateWithSuppliedComparerPreservingFirstOccurrence()
	{
		DocumentReplacementResult result = CreateResult(
			[],
			["Doc.lua", "DOC.LUA"],
			new WorkspaceOptions { PathComparison = LocalPathComparisonPolicy.CaseInsensitive }
		);

		Assert.AreSequenceEqual(["Doc.lua"], [.. result.UnknownDocumentIds]);
	}

	[TestMethod]
	public void ValidationFailedResult_CarriesPreparationIssues()
	{
		// The host edit-preparation layer produces ValidationFailed together with issues; the
		// library's DocumentReplacementApplier never produces ValidationFailed.
		var issue = new TextEditPreparationIssue(
			kind: TextEditPreparationIssueKind.RangeOutOfBounds,
			editIndex: 0,
			relatedEditIndex: null,
			message: "The edit range is outside the snapshot."
		);

		var result = DocumentReplacementResult.ValidationFailed(
			[issue],
			failure: new WorkspaceOperationFailure("PreparationFailed", "Preparation failed.")
		);

		Assert.AreEqual(DocumentReplacementOutcome.ValidationFailed, result.Outcome);
		Assert.HasCount(1, result.Issues);
		Assert.AreEqual(TextEditPreparationIssueKind.RangeOutOfBounds, result.Issues[0].Kind);
		Assert.AreEqual("The edit range is outside the snapshot.", result.Issues[0].Message);
		Assert.AreEqual("PreparationFailed", result.Failure!.Code);
	}

	[TestMethod]
	public void ValidationFailedFactory_WithoutFailure_ExposesTheEmptyMatrix()
	{
		var diagnostic = new TextEditPreparationIssue(
			kind: TextEditPreparationIssueKind.RangeOutOfBounds,
			editIndex: 0,
			relatedEditIndex: null,
			message: "The edit range is outside the snapshot."
		);

		var result = DocumentReplacementResult.ValidationFailed([diagnostic]);

		Assert.AreEqual(DocumentReplacementOutcome.ValidationFailed, result.Outcome);
		Assert.AreEqual(0, result.PreparedOperationCount);
		Assert.IsEmpty(result.TargetResults);
		Assert.IsEmpty(result.ChangedDocumentIds);
		Assert.IsEmpty(result.UnknownDocumentIds);
		Assert.HasCount(1, result.Issues);
		Assert.IsNull(result.Failure);
		Assert.IsFalse(result.ChangeSet.HasChanges);
		Assert.IsEmpty(result.ChangeSet.DocumentChanges);
	}

	[TestMethod]
	public void CompletedFactory_ExposesTheCompletedMatrix()
	{
		var first = new DocumentReplacementTargetResult
		{
			DocumentId = "first.lua",
			ExpectedVersion = 4,
			ActualVersion = 5,
			PreparedOperationCount = 2,
			Outcome = DocumentReplacementTargetOutcome.Applied,
		};

		var second = new DocumentReplacementTargetResult
		{
			DocumentId = "second.lua",
			ExpectedVersion = 4,
			ActualVersion = 6,
			PreparedOperationCount = 1,
			Outcome = DocumentReplacementTargetOutcome.Applied,
		};

		DocumentReplacementChangeSet changeSet = CreateChangeSet(["first.lua", "second.lua"]);

		var result = DocumentReplacementResult.Completed(targetResults: [first, second], changeSet);

		Assert.AreEqual(DocumentReplacementOutcome.Completed, result.Outcome);

		// The total is derived from the per-target results (2 + 1).
		Assert.AreEqual(3, result.PreparedOperationCount);
		Assert.AreSequenceEqual([first, second], [.. result.TargetResults]);
		Assert.AreSequenceEqual(["first.lua", "second.lua"], [.. result.ChangedDocumentIds]);
		Assert.IsEmpty(result.UnknownDocumentIds);
		Assert.IsEmpty(result.Issues);
		Assert.IsNull(result.Failure);
		Assert.AreSame(changeSet, result.ChangeSet);
		Assert.IsTrue(result.ChangeSet.HasChanges);
	}

	[TestMethod]
	public void PartiallyAppliedFactory_ExposesThePartialMatrix()
	{
		var failure = new WorkspaceOperationFailure(DocumentReplacementFailureCodes.TargetApplicationFailed, "The store failed.");

		var target = new DocumentReplacementTargetResult
		{
			DocumentId = "first.lua",
			ExpectedVersion = 4,
			ActualVersion = null,
			PreparedOperationCount = 1,
			Outcome = DocumentReplacementTargetOutcome.Unknown,
			Failure = failure,
		};

		DocumentReplacementChangeSet changeSet = CreateChangeSet(["second.lua"]);

		var result = DocumentReplacementResult.PartiallyApplied(
			targetResults: [target],
			unknownDocumentIds: ["first.lua", "FIRST.LUA"],
			failure,
			changeSet,
			options: new WorkspaceOptions { PathComparison = LocalPathComparisonPolicy.CaseSensitive }
		);

		Assert.AreEqual(DocumentReplacementOutcome.PartiallyApplied, result.Outcome);

		// The total is derived from the single target's own count, so a caller cannot report a total
		// that disagrees with its parts.
		Assert.AreEqual(1, result.PreparedOperationCount);
		Assert.AreSequenceEqual([target], [.. result.TargetResults]);
		Assert.AreSequenceEqual(["second.lua"], [.. result.ChangedDocumentIds]);

		// A case-sensitive policy keeps case variants distinct.
		Assert.AreSequenceEqual(["first.lua", "FIRST.LUA"], [.. result.UnknownDocumentIds]);

		Assert.IsEmpty(result.Issues);
		Assert.AreSame(failure, result.Failure);
		Assert.AreSame(changeSet, result.ChangeSet);
		Assert.IsTrue(result.ChangeSet.HasChanges);
	}

	[TestMethod]
	public void CanceledFactory_ExposesTheCanceledMatrix()
	{
		var target = new DocumentReplacementTargetResult
		{
			DocumentId = "first.lua",
			ExpectedVersion = 4,
			ActualVersion = null,
			PreparedOperationCount = 1,
			Outcome = DocumentReplacementTargetOutcome.NotApplied,
		};

		var result = DocumentReplacementResult.Canceled(targetResults: [target]);

		Assert.AreEqual(DocumentReplacementOutcome.Canceled, result.Outcome);
		Assert.AreEqual(1, result.PreparedOperationCount);
		Assert.AreSequenceEqual([target], [.. result.TargetResults]);
		Assert.IsEmpty(result.ChangedDocumentIds);
		Assert.IsEmpty(result.UnknownDocumentIds);
		Assert.IsEmpty(result.Issues);
		Assert.IsNull(result.Failure);
		Assert.IsFalse(result.ChangeSet.HasChanges);
	}

	[TestMethod]
	public void ChangeSet_HasChangesReflectsCapturedDocumentChanges()
	{
		var change = new DocumentReplacementChange
		{
			DocumentId = "doc.lua",
			BeforeContent = "before",
			AfterContent = "after",
			BeforeFileFormat = TestSnapshots.FileFormat,
			AfterFileFormat = TestSnapshots.FileFormat,
		};

		var emptyTransaction = new DocumentReplacementChangeSet([]);

		Assert.IsFalse(emptyTransaction.HasChanges);

		var result = DocumentReplacementResult.Completed(targetResults: [], new DocumentReplacementChangeSet([change]));

		Assert.IsTrue(result.ChangeSet.HasChanges);
		Assert.HasCount(1, result.ChangeSet.DocumentChanges);
		Assert.AreEqual("doc.lua", result.ChangeSet.DocumentChanges[0].DocumentId);
	}

	[TestMethod]
	public void DocumentReplacementChange_IsValueEqualAndSupportsWithUpdates()
	{
		var change = new DocumentReplacementChange
		{
			DocumentId = "doc.lua",
			BeforeContent = "before",
			AfterContent = "after",
			BeforeFileFormat = TestSnapshots.FileFormat,
			AfterFileFormat = TestSnapshots.FileFormat,
		};

		var same = new DocumentReplacementChange
		{
			DocumentId = "doc.lua",
			BeforeContent = "before",
			AfterContent = "after",
			BeforeFileFormat = TestSnapshots.FileFormat,
			AfterFileFormat = TestSnapshots.FileFormat,
		};

		DocumentReplacementChange updated = change with { AfterContent = "updated" };

		Assert.AreEqual(change, same);
		Assert.AreEqual(change.GetHashCode(), same.GetHashCode());
		Assert.AreEqual("doc.lua", updated.DocumentId);
		Assert.AreEqual("before", updated.BeforeContent);
		Assert.AreEqual("updated", updated.AfterContent);
		Assert.AreEqual(TestSnapshots.FileFormat, updated.AfterFileFormat);
	}

	[TestMethod]
	public void DocumentReplacementTargetResult_IsValueEqual()
	{
		var failure = new WorkspaceOperationFailure(DocumentReplacementFailureCodes.TargetNotChanged, "The target did not change.");

		var result = new DocumentReplacementTargetResult
		{
			DocumentId = "doc.lua",
			ExpectedVersion = 4,
			ActualVersion = 4,
			PreparedOperationCount = 2,
			Outcome = DocumentReplacementTargetOutcome.Applied,
			Failure = failure,
		};

		var same = new DocumentReplacementTargetResult
		{
			DocumentId = "doc.lua",
			ExpectedVersion = 4,
			ActualVersion = 4,
			PreparedOperationCount = 2,
			Outcome = DocumentReplacementTargetOutcome.Applied,
			Failure = failure,
		};

		Assert.AreEqual(result, same);
		Assert.AreEqual(result.GetHashCode(), same.GetHashCode());
	}

	[TestMethod]
	public void DocumentReplacementChange_WithNullDocumentId_Throws()
	{
		var change = new DocumentReplacementChange
		{
			DocumentId = "doc.lua",
			BeforeContent = "before",
			AfterContent = "after",
			BeforeFileFormat = TestSnapshots.FileFormat,
			AfterFileFormat = TestSnapshots.FileFormat,
		};

		Assert.ThrowsExactly<ArgumentNullException>(() => change with { DocumentId = null! });
	}

	[TestMethod]
	public void DocumentReplacementChange_WithNullAfterContent_Throws()
	{
		var change = new DocumentReplacementChange
		{
			DocumentId = "doc.lua",
			BeforeContent = "before",
			AfterContent = "after",
			BeforeFileFormat = TestSnapshots.FileFormat,
			AfterFileFormat = TestSnapshots.FileFormat,
		};

		Assert.ThrowsExactly<ArgumentNullException>(() => change with { AfterContent = null! });
	}

	[TestMethod]
	public void DocumentReplacementTargetResult_WithNullDocumentId_Throws()
	{
		var result = new DocumentReplacementTargetResult
		{
			DocumentId = "doc.lua",
			ExpectedVersion = 4,
			ActualVersion = 4,
			PreparedOperationCount = 2,
			Outcome = DocumentReplacementTargetOutcome.Applied,
		};

		Assert.ThrowsExactly<ArgumentNullException>(() => result with { DocumentId = null! });
	}

	[TestMethod]
	public void DocumentReplacementTargetResult_WithNegativePreparedOperationCount_Throws()
	{
		var result = new DocumentReplacementTargetResult
		{
			DocumentId = "doc.lua",
			ExpectedVersion = 4,
			ActualVersion = 4,
			PreparedOperationCount = 2,
			Outcome = DocumentReplacementTargetOutcome.Applied,
		};

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => result with { PreparedOperationCount = -1 });
	}

	[TestMethod]
	public void DocumentReplacementTarget_WithNullBeforeContent_Throws()
	{
		DocumentReplacementTarget target = CreateTarget();
		Assert.ThrowsExactly<ArgumentNullException>(() => target with { BeforeContent = null! });
	}

	[TestMethod]
	public void DocumentReplacementTarget_WithNullAfterContent_Throws()
	{
		DocumentReplacementTarget target = CreateTarget();
		Assert.ThrowsExactly<ArgumentNullException>(() => target with { AfterContent = null! });
	}

	[TestMethod]
	public void ApplicationResultFactories_NullCollectionsAndFailureAreRejected()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			DocumentReplacementResult.Completed(targetResults: null!, changeSet: new DocumentReplacementChangeSet([]))
		);

		Assert.ThrowsExactly<ArgumentNullException>(() => DocumentReplacementResult.Completed(targetResults: [], changeSet: null!));

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			DocumentReplacementResult.PartiallyApplied(
				targetResults: [],
				unknownDocumentIds: [],
				failure: null!,
				changeSet: new DocumentReplacementChangeSet([])
			)
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			DocumentReplacementResult.PartiallyApplied(
				targetResults: [],
				unknownDocumentIds: null!,
				failure: new WorkspaceOperationFailure(DocumentReplacementFailureCodes.TargetNotChanged, "The target did not change."),
				changeSet: new DocumentReplacementChangeSet([])
			)
		);

		Assert.ThrowsExactly<ArgumentNullException>(() => DocumentReplacementResult.ValidationFailed(null!));
	}

	[TestMethod]
	public void CompletedFactory_DerivesPreparedOperationCountFromTargets()
	{
		var first = new DocumentReplacementTargetResult
		{
			DocumentId = "first.lua",
			ExpectedVersion = 1,
			PreparedOperationCount = 1,
			Outcome = DocumentReplacementTargetOutcome.Applied,
		};

		var second = new DocumentReplacementTargetResult
		{
			DocumentId = "second.lua",
			ExpectedVersion = 1,
			PreparedOperationCount = 0,
			Outcome = DocumentReplacementTargetOutcome.NotApplied,
		};

		var result = DocumentReplacementResult.Completed(targetResults: [first, second], new DocumentReplacementChangeSet([]));

		Assert.AreEqual(1, result.PreparedOperationCount);
	}

	[TestMethod]
	public void ChangeSet_CopiesCallerCollection()
	{
		var changes = new List<DocumentReplacementChange>
		{
			new()
			{
				DocumentId = "doc.lua",
				BeforeContent = "before",
				AfterContent = "after",
				BeforeFileFormat = TestSnapshots.FileFormat,
				AfterFileFormat = TestSnapshots.FileFormat,
			},
		};

		var changeSet = new DocumentReplacementChangeSet(changes);

		changes.Add(
			new DocumentReplacementChange
			{
				DocumentId = "other.lua",
				BeforeContent = "old",
				AfterContent = "new",
				BeforeFileFormat = TestSnapshots.FileFormat,
				AfterFileFormat = TestSnapshots.FileFormat,
			}
		);

		Assert.HasCount(1, changeSet.DocumentChanges);
		Assert.AreEqual("doc.lua", changeSet.DocumentChanges[0].DocumentId);
	}

	private static DocumentReplacementTarget CreateTarget()
	{
		return new DocumentReplacementTarget
		{
			Identity = new(new WorkspaceDocumentKey(Guid.NewGuid()), "doc.lua", 4),
			BeforeContent = "before",
			AfterContent = "after",
			BeforeFileFormat = TestSnapshots.FileFormat,
			AfterFileFormat = TestSnapshots.FileFormat,
		};
	}
}
