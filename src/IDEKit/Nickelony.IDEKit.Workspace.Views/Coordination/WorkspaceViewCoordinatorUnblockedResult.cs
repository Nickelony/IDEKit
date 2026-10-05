using System.Diagnostics.CodeAnalysis;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Contains the outcome of a coordinator operation that attached views never block.</summary>
/// <typeparam name="TStoreResult">The document-authority result type of the operation.</typeparam>
/// <remarks>
/// A discard, a rename and a save-as ask attached views to follow the change instead of refusing it, so the store is always reached
/// and <see cref="StoreResult"/> is never <see langword="null"/>. A completed operation that left a view unsynchronized reports its store outcome
/// together with <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/>.
/// </remarks>
[SuppressMessage(
	"Design",
	"CA1000:Do not declare static members on generic types",
	Justification = "The factory matches the sibling result types, whose Blocked factory cannot infer the type argument, so every coordinator result is built on its own type."
)]
public sealed class WorkspaceViewCoordinatorUnblockedResult<TStoreResult> : WorkspaceViewCoordinatorResult<TStoreResult>
	where TStoreResult : class
{
	private WorkspaceViewCoordinatorUnblockedResult(TStoreResult storeResult, WorkspaceDocumentViewSynchronizationResult viewSynchronization)
		: base(storeResult, viewSynchronization) { }

	/// <summary>The document-authority result, which is never <see langword="null"/>.</summary>
	public override TStoreResult StoreResult => Reached;

	/// <summary>Creates the result of an operation that reached the document store.</summary>
	/// <param name="storeResult">The document-authority result.</param>
	/// <param name="viewSynchronization">
	/// The view-synchronization outcome, which is never <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/> for these operations.
	/// </param>
	/// <exception cref="ArgumentException">
	/// <paramref name="viewSynchronization"/> reports <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.
	/// </exception>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="storeResult"/> or <paramref name="viewSynchronization"/> is <see langword="null"/>.
	/// </exception>
	public static WorkspaceViewCoordinatorUnblockedResult<TStoreResult> FromStore(
		TStoreResult storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization
	)
	{
		ArgumentNullException.ThrowIfNull(storeResult);
		return new(storeResult, viewSynchronization);
	}
}
