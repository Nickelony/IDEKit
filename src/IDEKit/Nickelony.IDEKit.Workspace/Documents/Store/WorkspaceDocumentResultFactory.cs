using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Constructs workspace document operation results and document snapshots.</summary>
internal static class WorkspaceDocumentResultFactory
{
	internal static WorkspaceDocumentSnapshot CreateSnapshot(LogicalDocument document)
	{
		return new WorkspaceDocumentSnapshot(
			DocumentKey: document.DocumentKey,
			DocumentId: document.DocumentId,
			DisplayPath: document.DisplayPath,
			Version: document.Version,
			PersistedVersion: document.PersistedVersion,
			IsDirty: document.IsDirty,
			Text: new StringTextSnapshot(document.Content, document.DocumentId),
			FileFormat: document.FileFormat,
			OnDiskStamp: document.OnDiskStamp
		);
	}

	// Snapshot creation reads several LogicalDocument fields that are mutated under the store lock.

	// Call sites that do not already hold the lock use this overload so a concurrent mutation cannot

	// produce a torn snapshot; the lock is re-entrant, so in-lock callers can use the plain factory.

	internal static WorkspaceDocumentSnapshot CreateSnapshotUnderLock(object syncRoot, LogicalDocument document)
	{
		lock (syncRoot)

			return CreateSnapshot(document);
	}

	internal static IReadOnlyList<WorkspaceDocumentSnapshot> CreateSnapshots(IEnumerable<LogicalDocument> documents)
	{
		// Snapshots are created under the store state lock; the explicit loop keeps the
		// state-sensitive paths free of the intermediate LINQ buffers a filter-order-project chain
		// would allocate.
		List<WorkspaceDocumentSnapshot> snapshots = [];

		foreach (LogicalDocument document in documents)
			snapshots.Add(CreateSnapshot(document));

		return snapshots;
	}

	internal static WorkspaceDocumentCommitResult CreateCommitResult(
		WorkspaceDocumentCommitRequest request,
		WorkspaceDocumentCommitOutcome outcome,
		WorkspaceDocumentSnapshot? snapshot,
		FileStamp? observedOnDiskStamp = null,
		WorkspaceOperationFailure? failure = null
	)
	{
		return new WorkspaceDocumentCommitResult(outcome, RequestedIdentity: request.Identity, snapshot, observedOnDiskStamp, failure);
	}

	internal static WorkspaceDocumentReloadResult CreateReloadResult(
		WorkspaceDocumentReloadRequest request,
		WorkspaceDocumentReloadOutcome outcome,
		WorkspaceDocumentSnapshot? snapshot,
		FileStamp? observedOnDiskStamp = null,
		WorkspaceOperationFailure? failure = null
	)
	{
		return new WorkspaceDocumentReloadResult(outcome, RequestedIdentity: request.Identity, snapshot, observedOnDiskStamp, failure);
	}

	internal static WorkspaceDocumentConflictResolutionResult CreateConflictResolutionResult(
		WorkspaceDocumentConflictResolutionRequest request,
		WorkspaceDocumentConflictResolutionOutcome outcome,
		WorkspaceDocumentSnapshot? snapshot,
		FileStamp? observedOnDiskStamp = null,
		WorkspaceOperationFailure? failure = null
	)
	{
		return new WorkspaceDocumentConflictResolutionResult(
			outcome,
			Mode: request.Mode,
			RequestedIdentity: request.Identity,
			snapshot,
			observedOnDiskStamp,
			failure
		);
	}

	internal static WorkspaceDocumentConflictResolutionResult CreateConflictResolutionFromCommit(
		WorkspaceDocumentConflictResolutionRequest request,
		WorkspaceDocumentCommitResult commitResult
	)
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
			_ => WorkspaceDocumentConflictResolutionOutcome.WriteFailed,
		};

		return new WorkspaceDocumentConflictResolutionResult(
			outcome,
			Mode: request.Mode,
			RequestedIdentity: commitResult.RequestedIdentity,
			Snapshot: commitResult.Snapshot,
			ObservedOnDiskStamp: commitResult.ObservedOnDiskStamp,
			Failure: commitResult.Failure
		);
	}

	internal static WorkspaceDocumentMutationResult CreateMutationResult(
		WorkspaceDocumentReplaceRequest request,
		WorkspaceDocumentMutationOutcome outcome,
		WorkspaceDocumentSnapshot? snapshot
	)
	{
		return CreateMutationResultCore(request.Identity, outcome, snapshot);
	}

	internal static WorkspaceDocumentRenameResult CreateRenameResult(
		WorkspaceDocumentRenameRequest request,
		WorkspaceDocumentRenameOutcome outcome,
		WorkspaceDocumentSnapshot? snapshot,
		FileStamp? observedOnDiskStamp = null,
		WorkspaceOperationFailure? failure = null
	)
	{
		return new WorkspaceDocumentRenameResult(outcome, RequestedIdentity: request.Identity, snapshot, observedOnDiskStamp, failure);
	}

	internal static WorkspaceDocumentSaveAsResult CreateSaveAsResult(
		WorkspaceDocumentSaveAsRequest request,
		WorkspaceDocumentSaveAsOutcome outcome,
		WorkspaceDocumentSnapshot? snapshot,
		FileStamp? observedOnDiskStamp = null,
		WorkspaceOperationFailure? failure = null
	)
	{
		return new WorkspaceDocumentSaveAsResult(outcome, RequestedIdentity: request.Identity, snapshot, observedOnDiskStamp, failure);
	}

	internal static WorkspaceDocumentDeleteResult CreateDeleteResult(
		WorkspaceDocumentDeleteRequest request,
		WorkspaceDocumentDeleteOutcome outcome,
		WorkspaceDocumentSnapshot? snapshot,
		FileStamp? observedOnDiskStamp = null,
		WorkspaceOperationFailure? failure = null
	)
	{
		return new WorkspaceDocumentDeleteResult(outcome, RequestedIdentity: request.Identity, snapshot, observedOnDiskStamp, failure);
	}

	internal static WorkspaceDocumentDirectoryRenameResult CreateDirectoryRenameResult(
		WorkspaceDocumentDirectoryRenameRequest request,
		WorkspaceDocumentDirectoryRenameOutcome outcome,
		IReadOnlyList<WorkspaceDocumentSnapshot> snapshots,
		WorkspaceOperationFailure? failure = null
	)
	{
		return new WorkspaceDocumentDirectoryRenameResult(
			outcome,
			SourceDirectoryPath: request.SourceDirectoryPath,
			DestinationDirectoryPath: request.DestinationDirectoryPath,
			snapshots,
			failure
		);
	}

	internal static WorkspaceDocumentDirectoryDeleteResult CreateDirectoryDeleteResult(
		WorkspaceDocumentDirectoryDeleteRequest request,
		WorkspaceDocumentDirectoryDeleteOutcome outcome,
		IReadOnlyList<WorkspaceDocumentSnapshot> snapshots,
		WorkspaceOperationFailure? failure = null
	)
	{
		return new WorkspaceDocumentDirectoryDeleteResult(outcome, DirectoryPath: request.DirectoryPath, snapshots, failure);
	}

	internal static WorkspaceDocumentMutationResult CreateMutationResult(
		WorkspaceDocumentDiscardRequest request,
		WorkspaceDocumentMutationOutcome outcome,
		WorkspaceDocumentSnapshot? snapshot
	)
	{
		return CreateMutationResultCore(request.Identity, outcome, snapshot);
	}

	private static WorkspaceDocumentMutationResult CreateMutationResultCore(
		WorkspaceDocumentRequestIdentity identity,
		WorkspaceDocumentMutationOutcome outcome,
		WorkspaceDocumentSnapshot? snapshot
	)
	{
		return new WorkspaceDocumentMutationResult(outcome, identity, snapshot);
	}
}
