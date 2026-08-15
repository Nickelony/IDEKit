using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Pathing;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Editing;

/// <summary>
/// Describes the outcome of applying a prepared multi-file workspace edit.
/// </summary>
/// <remarks>
/// <para>
/// The changed and unknown target ids are de-duplicated with the comparison supplied to the factory,
/// preserving first-occurrence order. Supply the store's <see cref="IWorkspaceDocumentReader.PathComparison"/>
/// value so the comparison cannot drift from document identity; the default follows the operating
/// system.
/// </para>
/// <para>
/// Use the static factories, which produce the four well-formed outcomes so the outcome, failure, and
/// change-set members cannot contradict each other:
/// </para>
/// <list type="bullet">
/// <item><see cref="ValidationFailed"/> describes preparation failures;</item>
/// <item><see cref="Completed"/> lists every confirmed change;</item>
/// <item><see cref="PartiallyApplied"/> pairs the confirmed changes with the failure and the unknown targets;</item>
/// <item><see cref="Canceled"/> reports an application that stopped before any content change was applied.</item>
/// </list>
/// </remarks>
public sealed class WorkspaceEditApplicationResult
{
	private WorkspaceEditApplicationResult(
		WorkspaceEditApplicationOutcome outcome,
		IReadOnlyList<WorkspaceEditTargetResult> targetResults,
		IReadOnlyList<string> unknownTargetIds,
		IReadOnlyList<TextEditPreparationIssue> issues,
		WorkspaceOperationFailure? failure,
		WorkspaceEditChangeSet changeSet,
		LocalPathComparisonPolicy? pathComparison = null)
	{
		ArgumentNullException.ThrowIfNull(targetResults);
		ArgumentNullException.ThrowIfNull(unknownTargetIds);
		ArgumentNullException.ThrowIfNull(issues);
		ArgumentNullException.ThrowIfNull(changeSet);

		StringComparer comparer = (pathComparison ?? LocalPathComparisonPolicy.ForCurrentPlatform).Comparer;

		Outcome = outcome;
		TargetResults = Array.AsReadOnly([.. targetResults]);
		PreparedOperationCount = SumPreparedOperationCounts(TargetResults);
		ChangedTargetIds = Array.AsReadOnly([.. changeSet.DocumentChanges.Select(change => change.TargetId).Distinct(comparer)]);
		UnknownTargetIds = Array.AsReadOnly([.. unknownTargetIds.Distinct(comparer)]);
		Issues = Array.AsReadOnly([.. issues]);
		Failure = failure;
		ChangeSet = changeSet;
	}

	// The result's prepared-operation count is the sum over the per-target results, which every
	// construction path populates in application order, so the total cannot drift from its parts.
	private static int SumPreparedOperationCounts(IReadOnlyList<WorkspaceEditTargetResult> targetResults)
	{
		int total = 0;

		for (int index = 0; index < targetResults.Count; index++)
			total += targetResults[index].PreparedOperationCount;

		return total;
	}

	/// <summary>
	/// Creates the result of an application that completed with every target applied.
	/// </summary>
	/// <param name="targetResults">The per-target outcomes in application order.</param>
	/// <param name="changeSet">The before-and-after content captured for confirmed target transformations.</param>
	/// <param name="pathComparison">
	/// The comparison used to de-duplicate <see cref="ChangedTargetIds"/>; the default follows the
	/// operating system. Supply the store's <see cref="IWorkspaceDocumentReader.PathComparison"/> value
	/// so the comparison cannot drift from document identity.
	/// </param>
	/// <returns>A completed application result.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="targetResults"/> or <paramref name="changeSet"/> is <see langword="null"/>.
	/// </exception>
	public static WorkspaceEditApplicationResult Completed(
		IReadOnlyList<WorkspaceEditTargetResult> targetResults,
		WorkspaceEditChangeSet changeSet,
		LocalPathComparisonPolicy? pathComparison = null)
		=> new(
			WorkspaceEditApplicationOutcome.Completed,
			targetResults,
			[],
			[],
			null,
			changeSet,
			pathComparison);

	/// <summary>
	/// Creates the result of an application in which at least one target was not applied, with the
	/// confirmed changes kept applied.
	/// </summary>
	/// <param name="targetResults">The per-target outcomes in application order.</param>
	/// <param name="unknownTargetIds">The targets whose final content could not be confirmed.</param>
	/// <param name="failure">The failure detail that stopped the application.</param>
	/// <param name="changeSet">The before-and-after content captured for confirmed target transformations.</param>
	/// <param name="pathComparison">
	/// The comparison used to de-duplicate <see cref="ChangedTargetIds"/> and
	/// <see cref="UnknownTargetIds"/>; the default follows the operating system. Supply the store's
	/// <see cref="IWorkspaceDocumentReader.PathComparison"/> value so the comparison cannot drift from
	/// document identity.
	/// </param>
	/// <returns>A partially applied result.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="targetResults"/>, <paramref name="unknownTargetIds"/>, <paramref name="failure"/>, or
	/// <paramref name="changeSet"/> is <see langword="null"/>.
	/// </exception>
	public static WorkspaceEditApplicationResult PartiallyApplied(
		IReadOnlyList<WorkspaceEditTargetResult> targetResults,
		IReadOnlyList<string> unknownTargetIds,
		WorkspaceOperationFailure failure,
		WorkspaceEditChangeSet changeSet,
		LocalPathComparisonPolicy? pathComparison = null)
	{
		ArgumentNullException.ThrowIfNull(failure);

		return new(
			WorkspaceEditApplicationOutcome.PartiallyApplied,
			targetResults,
			unknownTargetIds,
			[],
			failure,
			changeSet,
			pathComparison);
	}

	/// <summary>
	/// Creates the result of an application that was canceled before any content change was applied.
	/// </summary>
	/// <param name="targetResults">The per-target outcomes in application order.</param>
	/// <param name="failure">The optional failure detail that describes the cancellation.</param>
	/// <param name="pathComparison">
	/// The comparison used for the (always empty) target id lists; the default follows the operating
	/// system.
	/// </param>
	/// <returns>A canceled application result without changes.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="targetResults"/> is <see langword="null"/>.</exception>
	public static WorkspaceEditApplicationResult Canceled(
		IReadOnlyList<WorkspaceEditTargetResult> targetResults,
		WorkspaceOperationFailure? failure = null,
		LocalPathComparisonPolicy? pathComparison = null)
		=> new(
			WorkspaceEditApplicationOutcome.Canceled,
			targetResults,
			[],
			[],
			failure,
			new WorkspaceEditChangeSet([]),
			pathComparison);

	/// <summary>
	/// Creates the result of an edit that failed validation before any target was prepared.
	/// </summary>
	/// <remarks>
	/// <see cref="ChangeSet"/>, <see cref="ChangedTargetIds"/>, and <see cref="UnknownTargetIds"/> are
	/// always empty for this outcome.
	/// </remarks>
	/// <param name="issues">The edit-preparation issues that describe the validation failure.</param>
	/// <param name="failure">The optional failure detail.</param>
	/// <param name="pathComparison">
	/// The comparison used for the (always empty) target id lists; the default follows the operating
	/// system.
	/// </param>
	/// <returns>A validation-failure result without targets or changes.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="issues"/> is <see langword="null"/>.</exception>
	public static WorkspaceEditApplicationResult ValidationFailed(
		IReadOnlyList<TextEditPreparationIssue> issues,
		WorkspaceOperationFailure? failure = null,
		LocalPathComparisonPolicy? pathComparison = null)
		=> new(
			WorkspaceEditApplicationOutcome.ValidationFailed,
			targetResults: [],
			unknownTargetIds: [],
			issues,
			failure,
			new WorkspaceEditChangeSet([]),
			pathComparison);

	/// <summary>
	/// Gets the application outcome.
	/// </summary>
	public WorkspaceEditApplicationOutcome Outcome { get; }

	/// <summary>
	/// Gets the total number of prepared replacement operations across all targets: a target
	/// contributes one when its content or format differs from the before state and zero otherwise.
	/// It is the sum of <see cref="WorkspaceEditTargetResult.PreparedOperationCount"/> over
	/// <see cref="TargetResults"/>. A prepared operation is not necessarily an applied one: an
	/// operation that was prepared but rejected before it changed content still counts here, while
	/// <see cref="ChangedTargetIds"/> lists only the targets whose change was confirmed.
	/// </summary>
	public int PreparedOperationCount { get; }

	/// <summary>
	/// Gets the per-target outcomes in application order.
	/// </summary>
	public IReadOnlyList<WorkspaceEditTargetResult> TargetResults { get; }

	/// <summary>
	/// Gets the target ids for which a content change was confirmed. A target that the document
	/// authority accepted as a <see cref="WorkspaceDocumentMutationOutcome.NoChange"/> result
	/// is not listed, because the applier did not transform the document.
	/// </summary>
	/// <remarks>The ids are derived from <see cref="ChangeSet"/> and de-duplicated with the configured comparison.</remarks>
	public IReadOnlyList<string> ChangedTargetIds { get; }

	/// <summary>
	/// Gets the target ids whose final content could not be confirmed.
	/// </summary>
	public IReadOnlyList<string> UnknownTargetIds { get; }

	/// <summary>
	/// Gets the edit-preparation issues produced with <see cref="WorkspaceEditApplicationOutcome.ValidationFailed"/>.
	/// </summary>
	public IReadOnlyList<TextEditPreparationIssue> Issues { get; }

	/// <summary>
	/// Gets the failure detail for a failed or canceled application, when a detail is available.
	/// </summary>
	public WorkspaceOperationFailure? Failure { get; }

	/// <summary>
	/// Gets the non-atomic before-and-after change set for confirmed target transformations.
	/// </summary>
	public WorkspaceEditChangeSet ChangeSet { get; }
}
