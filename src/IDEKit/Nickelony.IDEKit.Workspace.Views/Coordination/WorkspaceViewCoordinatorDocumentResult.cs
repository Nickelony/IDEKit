using System.Diagnostics.CodeAnalysis;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Contains the outcome of a coordinator operation on one document.</summary>
/// <typeparam name="TStoreResult">The document-authority result type of the operation.</typeparam>
/// <remarks>
/// When attached views blocked the operation before the store was reached,
/// <see cref="WorkspaceViewCoordinatorResult{TStoreResult}.StoreResult"/> is <see langword="null"/> and <see cref="BlockedSnapshot"/>
/// carries the current snapshot of the document. A completed operation that left a view unsynchronized reports its store outcome together
/// with <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/>.
/// </remarks>
[SuppressMessage(
	"Design",
	"CA1000:Do not declare static members on generic types",
	Justification = "The factories are the named constructors of a result that has no public constructor, and Blocked has no argument to infer the type from."
)]
public sealed class WorkspaceViewCoordinatorDocumentResult<TStoreResult> : WorkspaceViewCoordinatorResult<TStoreResult>
	where TStoreResult : class
{
	private WorkspaceViewCoordinatorDocumentResult(
		TStoreResult? storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization,
		WorkspaceDocumentSnapshot? blockedSnapshot
	)
		: base(storeResult, viewSynchronization)
	{
		BlockedSnapshot = blockedSnapshot;
	}

	/// <summary>
	/// The current snapshot of the document that attached views blocked the operation on; <see langword="null"/> otherwise or when the document
	/// is not tracked.
	/// </summary>
	public WorkspaceDocumentSnapshot? BlockedSnapshot { get; }

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
	public static WorkspaceViewCoordinatorDocumentResult<TStoreResult> FromStore(
		TStoreResult storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization
	)
	{
		ArgumentNullException.ThrowIfNull(storeResult);
		return new(storeResult, viewSynchronization, null);
	}

	/// <summary>Creates the result of an operation that attached views blocked before the document store was reached.</summary>
	/// <param name="snapshot">The current snapshot of the document, or <see langword="null"/> when it is not tracked.</param>
	/// <param name="viewSynchronization">The view-synchronization outcome, which reports the blocking views.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="viewSynchronization"/> does not report <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.
	/// </exception>
	/// <exception cref="ArgumentNullException"><paramref name="viewSynchronization"/> is <see langword="null"/>.</exception>
	public static WorkspaceViewCoordinatorDocumentResult<TStoreResult> Blocked(
		WorkspaceDocumentSnapshot? snapshot,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization
	) => new(null, viewSynchronization, snapshot);
}
