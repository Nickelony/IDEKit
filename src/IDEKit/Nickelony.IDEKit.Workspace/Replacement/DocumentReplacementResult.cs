using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Replacement;

/// <summary>Describes the outcome of applying prepared whole-content replacements to several documents.</summary>
/// <remarks>
/// The changed and unknown document ids are de-duplicated with the path comparison of the options supplied
/// to the factory, preserving first-occurrence order; supply the options that the store uses so the comparison cannot drift from document identity.
/// The static factories produce well-formed outcomes, so the outcome, failure, and change-set members cannot contradict each other.
/// </remarks>
public sealed class DocumentReplacementResult
{
	private DocumentReplacementResult(
		DocumentReplacementOutcome outcome,
		IReadOnlyList<DocumentReplacementTargetResult> targetResults,
		IReadOnlyList<string> unknownDocumentIds,
		IReadOnlyList<TextEditPreparationIssue> issues,
		WorkspaceOperationFailure? failure,
		DocumentReplacementChangeSet changeSet,
		WorkspaceOptions? options = null
	)
	{
		StringComparer comparer = (options ?? WorkspaceOptions.Default).PathComparison.Comparer;

		Outcome = outcome;
		TargetResults = Array.AsReadOnly([.. targetResults]);
		PreparedOperationCount = SumPreparedOperationCounts(TargetResults);
		ChangedDocumentIds = Array.AsReadOnly([.. changeSet.DocumentChanges.Select(change => change.DocumentId).Distinct(comparer)]);
		UnknownDocumentIds = Array.AsReadOnly([.. unknownDocumentIds.Distinct(comparer)]);
		Issues = Array.AsReadOnly([.. issues]);
		Failure = failure;
		ChangeSet = changeSet;
	}

	// The result's prepared-operation count is the sum over the per-target results, which every
	// construction path populates in application order, so the total cannot drift from its parts.
	private static int SumPreparedOperationCounts(IReadOnlyList<DocumentReplacementTargetResult> targetResults)
	{
		int total = 0;

		for (int index = 0; index < targetResults.Count; index++)
			total += targetResults[index].PreparedOperationCount;

		return total;
	}

	/// <param name="targetResults">The per-target outcomes in application order.</param>
	/// <param name="changeSet">The before-and-after content captured for confirmed target transformations.</param>
	/// <param name="options">
	/// The options whose path comparison de-duplicates <see cref="ChangedDocumentIds"/>; the default follows the operating system. Supply the options
	/// that the store uses so the comparison cannot drift from document identity.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="targetResults"/> or <paramref name="changeSet"/> is <see langword="null"/>.
	/// </exception>
	public static DocumentReplacementResult Completed(
		IReadOnlyList<DocumentReplacementTargetResult> targetResults,
		DocumentReplacementChangeSet changeSet,
		WorkspaceOptions? options = null
	)
	{
		ArgumentNullException.ThrowIfNull(targetResults);
		ArgumentNullException.ThrowIfNull(changeSet);

		return new(
			outcome: DocumentReplacementOutcome.Completed,
			targetResults,
			unknownDocumentIds: [],
			issues: [],
			failure: null,
			changeSet,
			options
		);
	}

	/// <param name="targetResults">The per-target outcomes in application order.</param>
	/// <param name="unknownDocumentIds">The targets whose final content could not be confirmed.</param>
	/// <param name="failure">The failure detail that stopped the application.</param>
	/// <param name="changeSet">The before-and-after content captured for confirmed target transformations.</param>
	/// <param name="options">
	/// The options whose path comparison de-duplicates <see cref="ChangedDocumentIds"/> and <see cref="UnknownDocumentIds"/>; the default follows
	/// the operating system. Supply the options that the store uses so the comparison cannot drift from document identity.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="targetResults"/>, <paramref name="unknownDocumentIds"/>, <paramref name="failure"/>,
	/// or <paramref name="changeSet"/> is <see langword="null"/>.
	/// </exception>
	public static DocumentReplacementResult PartiallyApplied(
		IReadOnlyList<DocumentReplacementTargetResult> targetResults,
		IReadOnlyList<string> unknownDocumentIds,
		WorkspaceOperationFailure failure,
		DocumentReplacementChangeSet changeSet,
		WorkspaceOptions? options = null
	)
	{
		ArgumentNullException.ThrowIfNull(targetResults);
		ArgumentNullException.ThrowIfNull(unknownDocumentIds);
		ArgumentNullException.ThrowIfNull(failure);
		ArgumentNullException.ThrowIfNull(changeSet);

		return new(outcome: DocumentReplacementOutcome.PartiallyApplied, targetResults, unknownDocumentIds, issues: [], failure, changeSet, options);
	}

	/// <param name="targetResults">The per-target outcomes in application order.</param>
	/// <param name="failure">The optional failure detail that describes the cancellation.</param>
	/// <param name="options">The options whose path comparison is used for the document id lists; the default follows the operating system.</param>
	/// <exception cref="ArgumentNullException"><paramref name="targetResults"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The change set and the document id lists are always empty for this outcome.
	/// </remarks>
	public static DocumentReplacementResult Canceled(
		IReadOnlyList<DocumentReplacementTargetResult> targetResults,
		WorkspaceOperationFailure? failure = null,
		WorkspaceOptions? options = null
	)
	{
		ArgumentNullException.ThrowIfNull(targetResults);

		return new(
			outcome: DocumentReplacementOutcome.Canceled,
			targetResults,
			unknownDocumentIds: [],
			issues: [],
			failure,
			changeSet: new DocumentReplacementChangeSet([]),
			options
		);
	}

	/// <param name="issues">The edit-preparation issues that describe the validation failure.</param>
	/// <param name="failure">The optional failure detail.</param>
	/// <param name="options">The options whose path comparison is used for the document id lists; the default follows the operating system.</param>
	/// <exception cref="ArgumentNullException"><paramref name="issues"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The change set and the document id lists are always empty for this outcome.
	/// </remarks>
	public static DocumentReplacementResult ValidationFailed(
		IReadOnlyList<TextEditPreparationIssue> issues,
		WorkspaceOperationFailure? failure = null,
		WorkspaceOptions? options = null
	)
	{
		ArgumentNullException.ThrowIfNull(issues);

		return new(
			outcome: DocumentReplacementOutcome.ValidationFailed,
			targetResults: [],
			unknownDocumentIds: [],
			issues,
			failure,
			changeSet: new DocumentReplacementChangeSet([]),
			options
		);
	}

	public DocumentReplacementOutcome Outcome { get; }

	/// <summary>
	/// The total number of prepared replacement operations across all targets:
	/// the sum of <see cref="DocumentReplacementTargetResult.PreparedOperationCount"/> over <see cref="TargetResults"/>.
	/// </summary>
	/// <remarks>
	/// A prepared operation is not necessarily an applied one: an operation rejected before it changed content
	/// still counts here, while <see cref="ChangedDocumentIds"/> lists only the targets whose change was confirmed.
	/// </remarks>
	public int PreparedOperationCount { get; }

	/// <summary>The per-target outcomes in application order.</summary>
	public IReadOnlyList<DocumentReplacementTargetResult> TargetResults { get; }

	/// <summary>The document ids for which a content change was confirmed.</summary>
	/// <remarks>
	/// A target the document authority accepted as <see cref="WorkspaceDocumentMutationOutcome.NoChange"/> is not listed, because the applier
	/// did not transform the document. The ids are derived from <see cref="ChangeSet"/> and de-duplicated with the configured comparison.
	/// </remarks>
	public IReadOnlyList<string> ChangedDocumentIds { get; }

	/// <summary>The document ids whose final content could not be confirmed.</summary>
	public IReadOnlyList<string> UnknownDocumentIds { get; }

	/// <summary>The edit-preparation issues produced with <see cref="DocumentReplacementOutcome.ValidationFailed"/>.</summary>
	public IReadOnlyList<TextEditPreparationIssue> Issues { get; }

	/// <summary>The failure detail for a failed or canceled application, when a detail is available.</summary>
	public WorkspaceOperationFailure? Failure { get; }

	/// <summary>The non-atomic before-and-after change set for confirmed target transformations.</summary>
	public DocumentReplacementChangeSet ChangeSet { get; }
}
