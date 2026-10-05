namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>The validation outcome of an <see cref="OperationGate.Begin"/> attempt.</summary>
internal enum OperationBeginOutcome
{
	DocumentNotFound,
	StaleDocumentInstance,
	StaleDocument,
	OperationInProgress,
}

/// <summary>The outcome of an operation's pre-gate check.</summary>
/// <remarks>
/// A non-<see cref="Proceed"/> value stops the operation before the disk gate is taken
/// and is carried on <see cref="OperationBeginResult.PreGateOutcome"/>, so the caller can map it to its own outcome without out-of-band state.
/// </remarks>
internal enum OperationPreGateOutcome
{
	Proceed,
	NoChange,
	DestinationInUse,
	DestinationBusy,
}

// The result of a Begin attempt. A success holds the document, the operation lease and, when requested, the
// captured snapshot. A rejection holds the outcome and a snapshot of the state that failed validation, captured under
// the store lock so it matches the state that was validated. Only a missing document has neither a document nor a
// snapshot.
internal readonly struct OperationBeginResult
{
	private OperationBeginResult(
		LogicalDocument? document,
		WorkspaceDocumentSnapshot? capturedSnapshot,
		WorkspaceDocumentSnapshot? failureSnapshot,
		OperationLease? operation,
		OperationBeginOutcome? outcome,
		OperationPreGateOutcome? preGateOutcome
	)
	{
		Document = document;
		CapturedSnapshot = capturedSnapshot;
		FailureSnapshot = failureSnapshot;
		Operation = operation;
		Outcome = outcome;
		PreGateOutcome = preGateOutcome;
	}

	internal LogicalDocument? Document { get; }
	internal WorkspaceDocumentSnapshot? CapturedSnapshot { get; }
	internal WorkspaceDocumentSnapshot? FailureSnapshot { get; }
	internal OperationLease? Operation { get; }
	internal OperationBeginOutcome? Outcome { get; }
	internal OperationPreGateOutcome? PreGateOutcome { get; }

	internal bool Succeeded => Outcome is null && PreGateOutcome is null;

	internal static OperationBeginResult NotFound()
	{
		return new(
			document: null,
			capturedSnapshot: null,
			failureSnapshot: null,
			operation: null,
			outcome: OperationBeginOutcome.DocumentNotFound,
			preGateOutcome: null
		);
	}

	internal static OperationBeginResult Rejected(
		OperationBeginOutcome outcome,
		LogicalDocument document,
		WorkspaceDocumentSnapshot failureSnapshot
	) => new(document, capturedSnapshot: null, failureSnapshot, operation: null, outcome, preGateOutcome: null);

	internal static OperationBeginResult StoppedBeforeGate(
		OperationPreGateOutcome preGateOutcome,
		LogicalDocument document,
		WorkspaceDocumentSnapshot failureSnapshot
	) => new(document, capturedSnapshot: null, failureSnapshot, operation: null, outcome: null, preGateOutcome);

	internal static OperationBeginResult Gated(LogicalDocument document, WorkspaceDocumentSnapshot? capturedSnapshot, OperationLease operation) =>
		new(document, capturedSnapshot, failureSnapshot: null, operation, outcome: null, preGateOutcome: null);
}
