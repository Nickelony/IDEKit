using System.Reflection;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

/// <summary>Covers the state and validation that the coordinator results share, and the typed members of each result.</summary>
[TestClass]
public sealed class WorkspaceViewCoordinatorResultTests
{
	private static readonly WorkspaceDocumentViewSynchronizationResult s_unsynchronized = WorkspaceDocumentViewSynchronizationResult.Unsynchronized([
		new("view"),
	]);

	private static readonly WorkspaceDocumentViewSynchronizationResult s_blocked = WorkspaceDocumentViewSynchronizationResult.Blocked([new("view")]);

	private static readonly WorkspaceDocumentSnapshot s_snapshot = TestSnapshots.Create("doc.lua", "content");
	private static readonly WorkspaceDocumentSnapshot s_blockedSnapshot = TestSnapshots.Create("blocked.lua", "content");

	private static readonly WorkspaceDocumentRequestIdentity s_identity = new(s_snapshot.DocumentKey, s_snapshot.DocumentId, s_snapshot.Version);
	private static readonly WorkspaceOperationFailure s_failure = new("code", "message");

	private static WorkspaceDocumentMutationResult MutationStore =>
		new(Outcome: WorkspaceDocumentMutationOutcome.Changed, RequestedIdentity: s_identity, Snapshot: s_snapshot);

	private static WorkspaceDocumentRenameResult RenameStore =>
		new(Outcome: WorkspaceDocumentRenameOutcome.Renamed, RequestedIdentity: s_identity, Snapshot: s_snapshot, Failure: s_failure);

	private static WorkspaceDocumentSaveAsResult SaveAsStore =>
		new(Outcome: WorkspaceDocumentSaveAsOutcome.SavedAs, RequestedIdentity: s_identity, Snapshot: s_snapshot, Failure: s_failure);

	private static WorkspaceDocumentDeleteResult DeleteStore =>
		new(Outcome: WorkspaceDocumentDeleteOutcome.Deleted, RequestedIdentity: s_identity, Snapshot: s_snapshot, Failure: s_failure);

	private static WorkspaceDocumentCommitResult CommitStore =>
		new(Outcome: WorkspaceDocumentCommitOutcome.Committed, RequestedIdentity: s_identity, Snapshot: s_snapshot, Failure: s_failure);

	private static WorkspaceDocumentReloadResult ReloadStore =>
		new(Outcome: WorkspaceDocumentReloadOutcome.Reloaded, RequestedIdentity: s_identity, Snapshot: s_snapshot, Failure: s_failure);

	private static WorkspaceDocumentConflictResolutionResult ConflictResolutionStore =>
		new(
			Outcome: WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk,
			Mode: WorkspaceDocumentConflictResolutionMode.ResolveWithDisk,
			RequestedIdentity: s_identity,
			Snapshot: s_snapshot,
			Failure: s_failure
		);

	private static WorkspaceDocumentDirectoryRenameResult DirectoryRenameStore =>
		new(
			Outcome: WorkspaceDocumentDirectoryRenameOutcome.Renamed,
			SourceDirectoryPath: "src",
			DestinationDirectoryPath: "dest",
			Snapshots: [s_snapshot],
			Failure: s_failure
		);

	private static WorkspaceDocumentDirectoryDeleteResult DirectoryDeleteStore =>
		new(Outcome: WorkspaceDocumentDirectoryDeleteOutcome.Deleted, DirectoryPath: "src", Snapshots: [s_snapshot], Failure: s_failure);

	private static void AssertReachedStoreContract<TStore, TResult>(
		TStore store,
		Func<TStore, WorkspaceDocumentViewSynchronizationResult, TResult> fromStore
	)
		where TStore : class
		where TResult : WorkspaceViewCoordinatorResult<TStore>
	{
		TResult synchronized = fromStore(store, WorkspaceDocumentViewSynchronizationResult.Synchronized);
		TResult unsynchronized = fromStore(store, s_unsynchronized);

		Assert.AreSame(store, synchronized.StoreResult);
		Assert.AreSame(WorkspaceDocumentViewSynchronizationResult.Synchronized, synchronized.ViewSynchronization);
		Assert.AreSame(store, unsynchronized.StoreResult);
		Assert.AreSame(s_unsynchronized, unsynchronized.ViewSynchronization);

		Assert.ThrowsExactly<ArgumentNullException>(() => fromStore(null!, WorkspaceDocumentViewSynchronizationResult.Synchronized));
		Assert.ThrowsExactly<ArgumentNullException>(() => fromStore(store, null!));

		ArgumentException blocked = Assert.ThrowsExactly<ArgumentException>(() => fromStore(store, s_blocked));

		Assert.AreEqual("viewSynchronization", blocked.ParamName);
	}

	private static void AssertBlockedContract<TStore, TBlocked>(
		TBlocked blockedPayload,
		Func<TBlocked, WorkspaceDocumentViewSynchronizationResult, WorkspaceViewCoordinatorResult<TStore>> blocked
	)
		where TStore : class
	{
		WorkspaceViewCoordinatorResult<TStore> result = blocked(blockedPayload, s_blocked);

		Assert.IsNull(result.StoreResult);
		Assert.AreSame(s_blocked, result.ViewSynchronization);

		Assert.ThrowsExactly<ArgumentNullException>(() => blocked(blockedPayload, null!));

		ArgumentException notBlocked = Assert.ThrowsExactly<ArgumentException>(() => blocked(blockedPayload, s_unsynchronized));

		Assert.AreEqual("viewSynchronization", notBlocked.ParamName);

		Assert.ThrowsExactly<ArgumentException>(() => blocked(blockedPayload, WorkspaceDocumentViewSynchronizationResult.Synchronized));
	}

	[TestMethod]
	public void FromStore_KeepsTheStoreResultAndTheSynchronizationAndRejectsBlockedViewsForEveryResult()
	{
		AssertReachedStoreContract(MutationStore, WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentMutationResult>.FromStore);
		AssertReachedStoreContract(RenameStore, WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult>.FromStore);
		AssertReachedStoreContract(SaveAsStore, WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentSaveAsResult>.FromStore);
		AssertReachedStoreContract(DeleteStore, WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult>.FromStore);
		AssertReachedStoreContract(CommitStore, WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult>.FromStore);
		AssertReachedStoreContract(ReloadStore, WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult>.FromStore);

		AssertReachedStoreContract(
			ConflictResolutionStore,
			WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentConflictResolutionResult>.FromStore
		);

		AssertReachedStoreContract(DirectoryRenameStore, WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult>.FromStore);
		AssertReachedStoreContract(DirectoryDeleteStore, WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryDeleteResult>.FromStore);
	}

	[TestMethod]
	public void Blocked_HasNoStoreResultAndRequiresBlockedViewsForEveryBlockableResult()
	{
		AssertBlockedContract<WorkspaceDocumentDeleteResult, WorkspaceDocumentSnapshot?>(
			s_blockedSnapshot,
			WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult>.Blocked
		);

		AssertBlockedContract<WorkspaceDocumentCommitResult, WorkspaceDocumentSnapshot?>(
			s_blockedSnapshot,
			WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult>.Blocked
		);

		AssertBlockedContract<WorkspaceDocumentReloadResult, WorkspaceDocumentSnapshot?>(
			s_blockedSnapshot,
			WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult>.Blocked
		);

		AssertBlockedContract<WorkspaceDocumentConflictResolutionResult, WorkspaceDocumentSnapshot?>(
			s_blockedSnapshot,
			WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentConflictResolutionResult>.Blocked
		);

		AssertBlockedContract<WorkspaceDocumentDirectoryRenameResult, IReadOnlyList<WorkspaceDocumentSnapshot>>(
			[s_blockedSnapshot],
			WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult>.Blocked
		);

		AssertBlockedContract<WorkspaceDocumentDirectoryDeleteResult, IReadOnlyList<WorkspaceDocumentSnapshot>>(
			[s_blockedSnapshot],
			WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryDeleteResult>.Blocked
		);
	}

	[TestMethod]
	public void Blocked_WithoutSourceSnapshots_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult>.Blocked(null!, s_blocked)
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryDeleteResult>.Blocked(null!, s_blocked)
		);
	}

	[TestMethod]
	public void ANeverBlockedResult_ForwardsTheMembersOfItsStoreResult()
	{
		var mutation = WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentMutationResult>.FromStore(MutationStore, s_unsynchronized);
		var rename = WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult>.FromStore(RenameStore, s_unsynchronized);
		var saveAs = WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentSaveAsResult>.FromStore(SaveAsStore, s_unsynchronized);

		Assert.AreSame(s_snapshot, mutation.StoreResult.Snapshot);
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, mutation.StoreResult.Outcome);
		Assert.AreSame(s_snapshot, rename.StoreResult.Snapshot);
		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, rename.StoreResult.Outcome);
		Assert.AreSame(s_failure, rename.StoreResult.Failure);
		Assert.AreSame(s_snapshot, saveAs.StoreResult.Snapshot);
		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, saveAs.StoreResult.Outcome);
		Assert.AreSame(s_failure, saveAs.StoreResult.Failure);
	}

	[TestMethod]
	public void StoreResult_IsDeclaredNonNullForTheResultsThatNeverBlockAndNullableForTheOthers()
	{
		var context = new NullabilityInfoContext();

		Type[] neverBlocked =
		[
			typeof(WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentMutationResult>),
			typeof(WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult>),
			typeof(WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentSaveAsResult>),
		];

		Type[] blockable =
		[
			typeof(WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult>),
			typeof(WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult>),
			typeof(WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult>),
			typeof(WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentConflictResolutionResult>),
			typeof(WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult>),
			typeof(WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryDeleteResult>),
		];

		foreach (Type type in neverBlocked)
		{
			Assert.AreEqual(
				NullabilityState.NotNull,

				context
					.Create(type.GetProperty(nameof(WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentMutationResult>.StoreResult))!)
					.ReadState,

				type.Name
			);
		}

		foreach (Type type in blockable)
		{
			Assert.AreEqual(
				NullabilityState.Nullable,

				context
					.Create(type.GetProperty(nameof(WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult>.StoreResult))!)
					.ReadState,

				type.Name
			);
		}
	}

	[TestMethod]
	public void ABlockableResult_ForwardsTheMembersOfItsStoreResultWhenTheStoreWasReached()
	{
		var delete = WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult>.FromStore(DeleteStore, s_unsynchronized);
		var commit = WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult>.FromStore(CommitStore, s_unsynchronized);
		var reload = WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult>.FromStore(ReloadStore, s_unsynchronized);

		var conflict = WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentConflictResolutionResult>.FromStore(
			ConflictResolutionStore,
			s_unsynchronized
		);

		Assert.AreSame(s_snapshot, delete.StoreResult?.Snapshot);
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, delete.StoreResult?.Outcome);
		Assert.AreSame(s_failure, delete.StoreResult?.Failure);
		Assert.AreSame(s_snapshot, commit.StoreResult?.Snapshot);
		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, commit.StoreResult?.Outcome);
		Assert.AreSame(s_failure, commit.StoreResult?.Failure);
		Assert.AreSame(s_snapshot, reload.StoreResult?.Snapshot);
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, reload.StoreResult?.Outcome);
		Assert.AreSame(s_failure, reload.StoreResult?.Failure);
		Assert.AreSame(s_snapshot, conflict.StoreResult?.Snapshot);
		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk, conflict.StoreResult?.Outcome);
		Assert.AreSame(s_failure, conflict.StoreResult?.Failure);
	}

	[TestMethod]
	public void ABlockedDocumentResult_ReportsTheBlockedSnapshotAndNoStoreResult()
	{
		var delete = WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult>.Blocked(s_blockedSnapshot, s_blocked);
		var reload = WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult>.Blocked(null, s_blocked);

		Assert.AreSame(s_blockedSnapshot, delete.BlockedSnapshot);
		Assert.IsNull(delete.StoreResult);
		Assert.IsNull(reload.BlockedSnapshot);
		Assert.IsNull(reload.StoreResult);
	}

	[TestMethod]
	public void ADirectoryResult_ReportsTheStoreSnapshotsWhenTheStoreWasReachedAndTheSourceSnapshotsWhenBlocked()
	{
		var renamed = WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult>.FromStore(
			DirectoryRenameStore,
			s_unsynchronized
		);

		var deleted = WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryDeleteResult>.FromStore(
			DirectoryDeleteStore,
			s_unsynchronized
		);

		var blockedRename = WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult>.Blocked([s_blockedSnapshot], s_blocked);
		var blockedDelete = WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryDeleteResult>.Blocked([s_blockedSnapshot], s_blocked);

		Assert.AreSame(s_snapshot, Assert.ContainsSingle(renamed.StoreResult?.Snapshots));
		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, renamed.StoreResult?.Outcome);
		Assert.IsEmpty(renamed.BlockedSnapshots);
		Assert.AreSame(s_snapshot, Assert.ContainsSingle(deleted.StoreResult?.Snapshots));
		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Deleted, deleted.StoreResult?.Outcome);
		Assert.IsEmpty(deleted.BlockedSnapshots);
		Assert.AreSame(s_blockedSnapshot, Assert.ContainsSingle(blockedRename.BlockedSnapshots));
		Assert.IsNull(blockedRename.StoreResult);
		Assert.AreSame(s_blockedSnapshot, Assert.ContainsSingle(blockedDelete.BlockedSnapshots));
		Assert.IsNull(blockedDelete.StoreResult);
	}
}
