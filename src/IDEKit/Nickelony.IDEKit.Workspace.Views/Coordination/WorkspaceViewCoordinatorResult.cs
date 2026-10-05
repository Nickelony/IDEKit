namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Contains the outcome of a coordinator operation: the document authority's result and how the attached views fared.</summary>
/// <typeparam name="TStoreResult">The document-authority result type of the operation.</typeparam>
/// <remarks>
/// <see cref="StoreResult"/> is <see langword="null"/> exactly when attached views blocked the operation before the store was reached,
/// and then <see cref="ViewSynchronization"/> reports <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>. A derived result
/// rejects every other combination when it is created. The result of an operation that attached views never block overrides <see cref="StoreResult"/>
/// so that it is never <see langword="null"/>.
/// </remarks>
public abstract class WorkspaceViewCoordinatorResult<TStoreResult>
	where TStoreResult : class
{
	private readonly TStoreResult? _storeResult;

	private protected WorkspaceViewCoordinatorResult(TStoreResult? storeResult, WorkspaceDocumentViewSynchronizationResult viewSynchronization)
	{
		ArgumentNullException.ThrowIfNull(viewSynchronization);

		bool isBlocked = viewSynchronization.Outcome == WorkspaceDocumentViewSynchronizationOutcome.Blocked;

		if (storeResult is not null && isBlocked)
			throw new ArgumentException("A result that reached the store cannot report blocked views.", nameof(viewSynchronization));

		if (storeResult is null && !isBlocked)
			throw new ArgumentException("A result with no store outcome must report blocked views.", nameof(viewSynchronization));

		_storeResult = storeResult;
		ViewSynchronization = viewSynchronization;
	}

	/// <summary>The document-authority result; <see langword="null"/> when attached views blocked the operation.</summary>
	public virtual TStoreResult? StoreResult => _storeResult;

	/// <summary>The synchronization outcome of the attached views, including the views that blocked the operation or could not follow it.</summary>
	public WorkspaceDocumentViewSynchronizationResult ViewSynchronization { get; }

	/// <summary>The document-authority result of an operation that attached views never block.</summary>
	/// <exception cref="InvalidOperationException">The result carries no store result.</exception>
	private protected TStoreResult Reached =>
		_storeResult ?? throw new InvalidOperationException("The operation was blocked before it reached the store.");
}
