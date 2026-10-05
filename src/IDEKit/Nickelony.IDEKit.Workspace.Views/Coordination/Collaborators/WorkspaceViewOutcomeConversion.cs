using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Maps the outcomes and the open result of the document store onto the coordinator's vocabulary.</summary>
/// <remarks>
/// Every named store outcome is mapped explicitly. The store outcomes are a closed set defined by this library, so a value outside it means
/// the store implementation, or a version mismatch, violates the contract; mapping it to a failure outcome would hide that, and the conversion
/// throws instead.
/// </remarks>
internal static class WorkspaceViewOutcomeConversion
{
	/// <summary>Maps the result of a store open onto the coordinator's open result.</summary>
	/// <param name="openResult">The result the store reported.</param>
	/// <returns>
	/// The result that wraps <paramref name="openResult"/>, with its snapshot for an opened or already-open document and its failure otherwise.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException">The outcome of <paramref name="openResult"/> is not a defined store outcome.</exception>
	internal static WorkspaceViewCoordinatorOpenResult ToOpenResult(WorkspaceDocumentOpenResult openResult)
	{
		return openResult.Outcome switch
		{
			WorkspaceDocumentOpenOutcome.Opened => new(WorkspaceViewCoordinatorOpenOutcome.Opened, openResult.Snapshot, StoreResult: openResult),

			WorkspaceDocumentOpenOutcome.AlreadyOpen => new(
				WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen,
				openResult.Snapshot,
				StoreResult: openResult
			),

			_ => new(Outcome: ToOpenOutcome(openResult.Outcome), Snapshot: null, Failure: openResult.Failure, StoreResult: openResult),
		};
	}

	/// <summary>Maps the outcome of a store open onto the coordinator's open outcome.</summary>
	/// <param name="outcome">The outcome the store reported.</param>
	/// <returns>The coordinator outcome with the same meaning.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="outcome"/> is not a defined store outcome.</exception>
	internal static WorkspaceViewCoordinatorOpenOutcome ToOpenOutcome(WorkspaceDocumentOpenOutcome outcome)
	{
		return outcome switch
		{
			WorkspaceDocumentOpenOutcome.Opened => WorkspaceViewCoordinatorOpenOutcome.Opened,
			WorkspaceDocumentOpenOutcome.AlreadyOpen => WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen,
			WorkspaceDocumentOpenOutcome.InvalidPath => WorkspaceViewCoordinatorOpenOutcome.InvalidPath,
			WorkspaceDocumentOpenOutcome.NotFound => WorkspaceViewCoordinatorOpenOutcome.NotFound,
			WorkspaceDocumentOpenOutcome.IsDirectory => WorkspaceViewCoordinatorOpenOutcome.IsDirectory,
			WorkspaceDocumentOpenOutcome.LoadFailed => WorkspaceViewCoordinatorOpenOutcome.LoadFailed,
			WorkspaceDocumentOpenOutcome.Canceled => WorkspaceViewCoordinatorOpenOutcome.Canceled,
			_ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "The store returned an unmapped open outcome."),
		};
	}
}
