using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Converts the outcome of a lower layer onto the outcome vocabulary of a document operation.</summary>
/// <remarks>
/// A file system reports a move or a delete in its own vocabulary, and the admission reports why an operation stopped before it took its disk gate.
/// Each document operation names its own success and rejection states, so every conversion is an explicit switch and none is a cast.
/// </remarks>
internal static class WorkspaceOutcomeConversion
{
	/// <summary>Converts the outcome of a file move onto the outcome of a document rename.</summary>
	/// <param name="outcome">The outcome the file system reported.</param>
	/// <returns>
	/// The matching rename outcome; <see cref="WorkspaceDocumentRenameOutcome.MoveFailed"/> for a failure the rename does not name.
	/// </returns>
	internal static WorkspaceDocumentRenameOutcome ToRenameOutcome(WorkspaceFileMoveOutcome outcome)
	{
		return outcome switch
		{
			WorkspaceFileMoveOutcome.DestinationExists => WorkspaceDocumentRenameOutcome.DestinationExists,
			WorkspaceFileMoveOutcome.ExternalFileConflict => WorkspaceDocumentRenameOutcome.ExternalFileConflict,
			WorkspaceFileMoveOutcome.Canceled => WorkspaceDocumentRenameOutcome.Canceled,
			WorkspaceFileMoveOutcome.MoveStateUnknown => WorkspaceDocumentRenameOutcome.MoveStateUnknown,
			_ => WorkspaceDocumentRenameOutcome.MoveFailed,
		};
	}

	/// <summary>Converts the outcome of a directory move onto the outcome of a directory rename.</summary>
	/// <param name="outcome">The outcome the file system reported.</param>
	/// <returns>
	/// The matching directory rename outcome; <see cref="WorkspaceDocumentDirectoryRenameOutcome.MoveFailed"/> for a failure the rename
	/// does not name.
	/// </returns>
	internal static WorkspaceDocumentDirectoryRenameOutcome ToDirectoryRenameOutcome(WorkspaceFileMoveOutcome outcome)
	{
		return outcome switch
		{
			WorkspaceFileMoveOutcome.DestinationExists => WorkspaceDocumentDirectoryRenameOutcome.DestinationExists,
			WorkspaceFileMoveOutcome.ExternalFileConflict => WorkspaceDocumentDirectoryRenameOutcome.ExternalFileConflict,
			WorkspaceFileMoveOutcome.Canceled => WorkspaceDocumentDirectoryRenameOutcome.Canceled,
			WorkspaceFileMoveOutcome.MoveStateUnknown => WorkspaceDocumentDirectoryRenameOutcome.MoveStateUnknown,
			_ => WorkspaceDocumentDirectoryRenameOutcome.MoveFailed,
		};
	}

	/// <summary>Converts the outcome of a directory delete onto the outcome of a recursive document delete.</summary>
	/// <param name="outcome">The outcome the file system reported.</param>
	/// <returns>
	/// The matching directory delete outcome; <see cref="WorkspaceDocumentDirectoryDeleteOutcome.DeleteFailed"/> for a failure the delete
	/// does not name.
	/// </returns>
	internal static WorkspaceDocumentDirectoryDeleteOutcome ToDirectoryDeleteOutcome(WorkspaceFileDeleteOutcome outcome)
	{
		return outcome switch
		{
			WorkspaceFileDeleteOutcome.ExternalFileConflict => WorkspaceDocumentDirectoryDeleteOutcome.ExternalFileConflict,
			WorkspaceFileDeleteOutcome.Canceled => WorkspaceDocumentDirectoryDeleteOutcome.Canceled,
			_ => WorkspaceDocumentDirectoryDeleteOutcome.DeleteFailed,
		};
	}

	/// <summary>Converts the pre-gate outcome of a rename onto the outcome of a document rename.</summary>
	/// <param name="outcome">The outcome that stopped the rename before it took the disk gate.</param>
	/// <returns>
	/// The matching rename outcome; <see cref="WorkspaceDocumentRenameOutcome.DestinationBusy"/> for an outcome the rename does not name.
	/// </returns>
	internal static WorkspaceDocumentRenameOutcome ToRenameOutcome(OperationPreGateOutcome outcome)
	{
		return outcome switch
		{
			OperationPreGateOutcome.NoChange => WorkspaceDocumentRenameOutcome.NoChange,
			OperationPreGateOutcome.DestinationInUse => WorkspaceDocumentRenameOutcome.DestinationInUse,
			_ => WorkspaceDocumentRenameOutcome.DestinationBusy,
		};
	}

	/// <summary>Converts the pre-gate outcome of a save-as onto the outcome of a document save-as.</summary>
	/// <param name="outcome">The outcome that stopped the save-as before it took the disk gate.</param>
	/// <returns>
	/// The matching save-as outcome; <see cref="WorkspaceDocumentSaveAsOutcome.DestinationBusy"/> for an outcome the save-as does not name.
	/// </returns>
	internal static WorkspaceDocumentSaveAsOutcome ToSaveAsOutcome(OperationPreGateOutcome outcome)
	{
		return outcome switch
		{
			OperationPreGateOutcome.DestinationInUse => WorkspaceDocumentSaveAsOutcome.DestinationInUse,
			_ => WorkspaceDocumentSaveAsOutcome.DestinationBusy,
		};
	}

	/// <summary>Converts a failed admission onto the result of the operation that was refused.</summary>
	/// <typeparam name="TOutcome">The outcome type of the operation.</typeparam>
	/// <typeparam name="TResult">The result type of the operation.</typeparam>
	/// <param name="begin">The failed admission.</param>
	/// <param name="documentNotFound">The outcome for a request that names no tracked document.</param>
	/// <param name="staleDocumentInstance">The outcome for a request made against another instance of the document.</param>
	/// <param name="staleDocument">The outcome for a request made against an outdated version.</param>
	/// <param name="operationInProgress">The outcome for every other refusal, such as a held disk gate or a delete in flight.</param>
	/// <param name="factory">Builds the result from the outcome and the snapshot of the state that failed validation.</param>
	/// <returns>The result the factory built.</returns>
	/// <remarks>
	/// The snapshot is <see langword="null"/> for a missing document.
	/// </remarks>
	internal static TResult ToFailureResult<TOutcome, TResult>(
		OperationBeginResult begin,
		TOutcome documentNotFound,
		TOutcome staleDocumentInstance,
		TOutcome staleDocument,
		TOutcome operationInProgress,
		Func<TOutcome, WorkspaceDocumentSnapshot?, TResult> factory
	)
	{
		return begin.Outcome switch
		{
			OperationBeginOutcome.DocumentNotFound => factory(documentNotFound, null),
			OperationBeginOutcome.StaleDocumentInstance => factory(staleDocumentInstance, begin.FailureSnapshot),
			OperationBeginOutcome.StaleDocument => factory(staleDocument, begin.FailureSnapshot),
			_ => factory(operationInProgress, begin.FailureSnapshot),
		};
	}
}
