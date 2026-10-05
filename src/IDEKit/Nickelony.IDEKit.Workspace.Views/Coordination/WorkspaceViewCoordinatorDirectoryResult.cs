using System.Diagnostics.CodeAnalysis;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Contains the outcome of a coordinator operation on a directory and the documents tracked under it.</summary>
/// <typeparam name="TStoreResult">The document-authority result type of the operation.</typeparam>
/// <remarks>
/// When attached views blocked the operation before the store was reached,
/// <see cref="WorkspaceViewCoordinatorResult{TStoreResult}.StoreResult"/> is <see langword="null"/> and <see cref="BlockedSnapshots"/> carries
/// the source snapshots of the documents the operation would have affected. An operation that completed while a lease could not be released or a view
/// could not follow reports its store outcome together with <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/>.
/// </remarks>
[SuppressMessage(
	"Design",
	"CA1000:Do not declare static members on generic types",
	Justification = "The factories are the named constructors of a result that has no public constructor, and Blocked has no argument to infer the type from."
)]
public sealed class WorkspaceViewCoordinatorDirectoryResult<TStoreResult> : WorkspaceViewCoordinatorResult<TStoreResult>
	where TStoreResult : class
{
	private WorkspaceViewCoordinatorDirectoryResult(
		TStoreResult? storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization,
		IReadOnlyList<WorkspaceDocumentSnapshot> blockedSnapshots
	)
		: base(storeResult, viewSynchronization)
	{
		BlockedSnapshots = blockedSnapshots;
	}

	/// <summary>
	/// The source snapshots of the documents that attached views blocked the operation on; empty when the operation reached the store.
	/// </summary>
	public IReadOnlyList<WorkspaceDocumentSnapshot> BlockedSnapshots { get; }

	/// <summary>Creates the result of an operation that reached the document store.</summary>
	/// <param name="storeResult">The document-authority result.</param>
	/// <param name="viewSynchronization">
	/// The view-synchronization outcome, which is never <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/> for an operation
	/// that reached the store.
	/// </param>
	/// <exception cref="ArgumentException">
	/// <paramref name="viewSynchronization"/> reports <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.
	/// </exception>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="storeResult"/> or <paramref name="viewSynchronization"/> is <see langword="null"/>.
	/// </exception>
	public static WorkspaceViewCoordinatorDirectoryResult<TStoreResult> FromStore(
		TStoreResult storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization
	)
	{
		ArgumentNullException.ThrowIfNull(storeResult);
		return new(storeResult, viewSynchronization, []);
	}

	/// <summary>Creates the result of an operation that attached views blocked before the document store was reached.</summary>
	/// <param name="sourceSnapshots">The snapshots of the tracked documents under the directory.</param>
	/// <param name="viewSynchronization">The view-synchronization outcome, which reports the blocking views.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="viewSynchronization"/> does not report <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.
	/// </exception>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="sourceSnapshots"/> or <paramref name="viewSynchronization"/> is <see langword="null"/>.
	/// </exception>
	public static WorkspaceViewCoordinatorDirectoryResult<TStoreResult> Blocked(
		IReadOnlyList<WorkspaceDocumentSnapshot> sourceSnapshots,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization
	)
	{
		ArgumentNullException.ThrowIfNull(sourceSnapshots);
		return new(null, viewSynchronization, sourceSnapshots);
	}
}
