using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Constructs workspace document operation results and document snapshots.
/// </summary>
internal static class WorkspaceDocumentResultFactory
{
	public static WorkspaceDocumentSnapshot CreateSnapshot(LogicalDocument document)
	{
		return new WorkspaceDocumentSnapshot(
			document.DocumentKey,
			document.DocumentId,
			document.DisplayPath,
			document.Version,
			document.PersistedVersion,
			document.IsDirty,
			new DeferredTextSnapshot(document.Content, document.DisplayPath),
			document.FileFormat,
			document.OnDiskStamp);
	}

	public static IReadOnlyList<WorkspaceDocumentSnapshot> CreateSnapshots(IEnumerable<LogicalDocument> documents)
	{
		// Snapshots are created under the store state lock; the explicit loop keeps the
		// state-sensitive paths free of the intermediate LINQ buffers a filter-order-project chain
		// would allocate.
		List<WorkspaceDocumentSnapshot> snapshots = [];
		foreach (LogicalDocument document in documents)
			snapshots.Add(CreateSnapshot(document));

		return snapshots;
	}

	public static WorkspaceDocumentCommitResult CreateCommitResult(
		WorkspaceDocumentCommitRequest request,
		WorkspaceDocumentCommitOutcome outcome,
		WorkspaceDocumentSnapshot? snapshot,
		FileStamp? observedOnDiskStamp = null,
		WorkspaceOperationFailure? failure = null)
	{
		return new WorkspaceDocumentCommitResult(
			outcome,
			request.Identity,
			snapshot,
			observedOnDiskStamp,
			failure);
	}

	public static WorkspaceDocumentReloadResult CreateReloadResult(
		WorkspaceDocumentReloadRequest request,
		WorkspaceDocumentReloadOutcome outcome,
		WorkspaceDocumentSnapshot? snapshot,
		FileStamp? observedOnDiskStamp = null,
		WorkspaceOperationFailure? failure = null)
	{
		return new WorkspaceDocumentReloadResult(
			outcome,
			request.Identity,
			snapshot,
			observedOnDiskStamp,
			failure);
	}

	public static WorkspaceDocumentConflictResolutionResult CreateConflictResolutionResult(
		WorkspaceDocumentConflictResolutionRequest request,
		WorkspaceDocumentConflictResolutionOutcome outcome,
		WorkspaceDocumentSnapshot? snapshot,
		FileStamp? observedOnDiskStamp = null,
		WorkspaceOperationFailure? failure = null)
	{
		return new WorkspaceDocumentConflictResolutionResult(
			outcome,
			request.Choice,
			request.Identity,
			snapshot,
			observedOnDiskStamp,
			failure);
	}

	public static WorkspaceDocumentConflictResolutionResult CreateConflictResolutionFromCommit(
		WorkspaceDocumentConflictResolutionRequest request,
		WorkspaceDocumentCommitResult commitResult)
	{
		WorkspaceDocumentConflictResolutionOutcome outcome = commitResult.Outcome switch
		{
			WorkspaceDocumentCommitOutcome.Committed => WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical,
			WorkspaceDocumentCommitOutcome.StaleDocument => WorkspaceDocumentConflictResolutionOutcome.StaleDocument,
			WorkspaceDocumentCommitOutcome.StaleDocumentInstance => WorkspaceDocumentConflictResolutionOutcome.StaleDocumentInstance,
			WorkspaceDocumentCommitOutcome.DocumentNotFound => WorkspaceDocumentConflictResolutionOutcome.DocumentNotFound,
			WorkspaceDocumentCommitOutcome.OperationInProgress => WorkspaceDocumentConflictResolutionOutcome.OperationInProgress,
			WorkspaceDocumentCommitOutcome.ExternalFileConflict => WorkspaceDocumentConflictResolutionOutcome.ExternalFileConflict,
			WorkspaceDocumentCommitOutcome.WriteFailed => WorkspaceDocumentConflictResolutionOutcome.WriteFailed,
			WorkspaceDocumentCommitOutcome.ReplacementStateUnknown => WorkspaceDocumentConflictResolutionOutcome.ReplacementStateUnknown,
			WorkspaceDocumentCommitOutcome.Canceled => WorkspaceDocumentConflictResolutionOutcome.Canceled,
			_ => WorkspaceDocumentConflictResolutionOutcome.WriteFailed
		};

		return new WorkspaceDocumentConflictResolutionResult(
			outcome,
			request.Choice,
			commitResult.RequestedIdentity,
			commitResult.Snapshot,
			commitResult.ObservedOnDiskStamp,
			commitResult.Failure);
	}

	public static WorkspaceDocumentMutationResult CreateMutationResult(
		WorkspaceDocumentReplaceRequest request,
		WorkspaceDocumentMutationOutcome outcome,
		WorkspaceDocumentSnapshot? snapshot)
	{
		return CreateMutationResultCore(
			request.Identity,
			outcome,
			snapshot);
	}

	public static WorkspaceDocumentRenameResult CreateRenameResult(
		WorkspaceDocumentRenameRequest request,
		WorkspaceDocumentRenameOutcome outcome,
		WorkspaceDocumentSnapshot? snapshot,
		FileStamp? observedOnDiskStamp = null,
		WorkspaceOperationFailure? failure = null)
	{
		return new WorkspaceDocumentRenameResult(
			outcome,
			request.Identity,
			snapshot,
			observedOnDiskStamp,
			failure);
	}

	public static WorkspaceDocumentSaveAsResult CreateSaveAsResult(
		WorkspaceDocumentSaveAsRequest request,
		WorkspaceDocumentSaveAsOutcome outcome,
		WorkspaceDocumentSnapshot? snapshot,
		FileStamp? observedOnDiskStamp = null,
		WorkspaceOperationFailure? failure = null)
	{
		return new WorkspaceDocumentSaveAsResult(
			outcome,
			request.Identity,
			snapshot,
			observedOnDiskStamp,
			failure);
	}

	public static WorkspaceDocumentDeleteResult CreateDeleteResult(
		WorkspaceDocumentDeleteRequest request,
		WorkspaceDocumentDeleteOutcome outcome,
		WorkspaceDocumentSnapshot? snapshot,
		FileStamp? observedOnDiskStamp = null,
		WorkspaceOperationFailure? failure = null)
	{
		return new WorkspaceDocumentDeleteResult(
			outcome,
			request.Identity,
			snapshot,
			observedOnDiskStamp,
			failure);
	}

	public static WorkspaceDocumentDirectoryRenameResult CreateDirectoryRenameResult(
		WorkspaceDocumentDirectoryRenameRequest request,
		WorkspaceDocumentDirectoryRenameOutcome outcome,
		IReadOnlyList<WorkspaceDocumentSnapshot> snapshots,
		WorkspaceOperationFailure? failure = null)
	{
		return new WorkspaceDocumentDirectoryRenameResult(
			outcome,
			request.SourceDirectoryPath,
			request.DestinationDirectoryPath,
			snapshots,
			failure);
	}

	public static WorkspaceDocumentDirectoryDeleteResult CreateDirectoryDeleteResult(
		WorkspaceDocumentDirectoryDeleteRequest request,
		WorkspaceDocumentDirectoryDeleteOutcome outcome,
		IReadOnlyList<WorkspaceDocumentSnapshot> snapshots,
		WorkspaceOperationFailure? failure = null)
	{
		return new WorkspaceDocumentDirectoryDeleteResult(
			outcome,
			request.DirectoryPath,
			snapshots,
			failure);
	}

	public static WorkspaceDocumentMutationResult CreateMutationResult(
		WorkspaceDocumentDiscardRequest request,
		WorkspaceDocumentMutationOutcome outcome,
		WorkspaceDocumentSnapshot? snapshot)
	{
		return CreateMutationResultCore(
			request.Identity,
			outcome,
			snapshot);
	}

	private static WorkspaceDocumentMutationResult CreateMutationResultCore(
		WorkspaceDocumentRequestIdentity identity,
		WorkspaceDocumentMutationOutcome outcome,
		WorkspaceDocumentSnapshot? snapshot)
	{
		return new WorkspaceDocumentMutationResult(
			outcome,
			identity,
			snapshot);
	}

	public static WorkspaceDocumentRenameOutcome MapRenameOutcome(WorkspaceFileMoveOutcome outcome)
		=> outcome switch
		{
			WorkspaceFileMoveOutcome.DestinationExists => WorkspaceDocumentRenameOutcome.DestinationExists,
			WorkspaceFileMoveOutcome.ExternalFileConflict => WorkspaceDocumentRenameOutcome.ExternalFileConflict,
			WorkspaceFileMoveOutcome.Canceled => WorkspaceDocumentRenameOutcome.Canceled,
			WorkspaceFileMoveOutcome.MoveStateUnknown => WorkspaceDocumentRenameOutcome.MoveStateUnknown,
			_ => WorkspaceDocumentRenameOutcome.MoveFailed
		};

	public static WorkspaceDocumentDirectoryRenameOutcome MapDirectoryRenameOutcome(WorkspaceFileMoveOutcome outcome)
		=> outcome switch
		{
			WorkspaceFileMoveOutcome.DestinationExists => WorkspaceDocumentDirectoryRenameOutcome.DestinationExists,
			WorkspaceFileMoveOutcome.ExternalFileConflict => WorkspaceDocumentDirectoryRenameOutcome.ExternalFileConflict,
			WorkspaceFileMoveOutcome.Canceled => WorkspaceDocumentDirectoryRenameOutcome.Canceled,
			WorkspaceFileMoveOutcome.MoveStateUnknown => WorkspaceDocumentDirectoryRenameOutcome.MoveStateUnknown,
			_ => WorkspaceDocumentDirectoryRenameOutcome.MoveFailed
		};

	public static WorkspaceDocumentDirectoryDeleteOutcome MapDirectoryDeleteOutcome(WorkspaceFileDeleteOutcome outcome)
		=> outcome switch
		{
			WorkspaceFileDeleteOutcome.ExternalFileConflict => WorkspaceDocumentDirectoryDeleteOutcome.ExternalFileConflict,
			WorkspaceFileDeleteOutcome.Canceled => WorkspaceDocumentDirectoryDeleteOutcome.Canceled,
			_ => WorkspaceDocumentDirectoryDeleteOutcome.DeleteFailed
		};
}
