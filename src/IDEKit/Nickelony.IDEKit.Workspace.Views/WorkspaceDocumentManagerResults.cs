using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Contains the result of opening a workspace document and attaching a view.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Snapshot"/> is present when a document was loaded and when an already-registered view
/// made the open a no-op (the snapshot of the document the view is attached to, or
/// <see langword="null"/> when that document is no longer tracked). It is <see langword="null"/> in
/// every other case:
/// </para>
/// <list type="bullet">
/// <item><description>The path is invalid, missing, or a directory, or the load failed or was canceled.</description></item>
/// <item><description>The view was unavailable for attachment, or its id was already claimed before the load.</description></item>
/// <item><description>An unexpected exception interrupted the open or attach (reported as <see cref="WorkspaceDocumentManagerOpenOutcome.OpenFailed"/>).</description></item>
/// </list>
/// <para>
/// <see cref="StoreResult"/> carries the full document-authority outcome when the open path reached
/// the store, so a host can distinguish a fresh load from a document that was already open and
/// merely received the view; it is <see langword="null"/> when the view validation answered first.
/// <see cref="Outcome"/> uses the manager vocabulary and can differ from the store outcome for the
/// same operation: a view attached to an already-open document reports
/// <see cref="WorkspaceDocumentManagerOpenOutcome.Opened"/> while the store reported
/// <see cref="WorkspaceDocumentOpenOutcome.AlreadyOpen"/>.
/// </para>
/// </remarks>
/// <param name="Outcome">The open-and-attach outcome.</param>
/// <param name="Snapshot">The loaded snapshot, or the current snapshot of the document the view is attached to; otherwise, <see langword="null"/>.</param>
/// <param name="Failure">Explains a load or attachment failure, when one occurred.</param>
/// <param name="StoreResult">The document-authority outcome the open path reached; <see langword="null"/> when no store call happened.</param>
public sealed record WorkspaceDocumentManagerOpenResult(
	WorkspaceDocumentManagerOpenOutcome Outcome,
	WorkspaceDocumentSnapshot? Snapshot,
	WorkspaceOperationFailure? Failure = null,
	WorkspaceDocumentOpenResult? StoreResult = null);

/// <summary>
/// Describes the outcome of a manager open operation, with or without view attachment.
/// </summary>
public enum WorkspaceDocumentManagerOpenOutcome
{
	/// <summary>The document was opened or was already open; when a view was supplied, it was attached.</summary>
	Opened,

	/// <summary>
	/// The document was already open; the call was a no-op. The outcome is produced when no view was
	/// supplied, when the supplied view was already registered, when a concurrent
	/// <see cref="IWorkspaceDocumentManager.UnregisterOpenView"/> removed the view while it was being
	/// attached, or when the store reports the document as already open without a view attach; a
	/// registered view's result carries the current snapshot of the document the view is attached to,
	/// or <see langword="null"/> when that document is no longer tracked.
	/// </summary>
	AlreadyOpen,

	/// <summary>
	/// The supplied view has a document, pending edits, a conflict, or could not report its id or
	/// state, so it cannot be attached to another document. <see cref="WorkspaceDocumentManagerOpenResult.Failure"/>
	/// carries the detail when a view member threw.
	/// </summary>
	ViewUnavailable,

	/// <summary>
	/// The supplied view could not be attached because it is already in use: a different registered
	/// view reports the same view id, or the view itself reported that it is attached elsewhere. The
	/// loaded snapshot is carried when the conflict was detected after the document was loaded; the
	/// supplied view is detached again for a duplicate id and stays untouched when its own attach
	/// reported the conflict.
	/// </summary>
	ViewInUse,

	/// <summary>The document was loaded but the view did not accept the attachment; <see cref="WorkspaceDocumentManagerOpenResult.Failure"/> carries the detail.</summary>
	ViewRejected,

	/// <summary>The requested path is invalid.</summary>
	InvalidPath,

	/// <summary>
	/// The path does not exist and the open options did not allow creating a new document
	/// (<see cref="WorkspaceDocumentOpenOptions.CreateIfMissing"/> is <see langword="false"/>).
	/// </summary>
	NotFound,

	/// <summary>
	/// The path exists as a directory, not a file. A directory cannot be tracked as a document, so the
	/// open is rejected; nothing was loaded and <see cref="WorkspaceDocumentManagerOpenResult.Snapshot"/>
	/// is <see langword="null"/>.
	/// </summary>
	IsDirectory,

	/// <summary>The document could not be loaded.</summary>
	LoadFailed,

	/// <summary>
	/// Opening the document and attaching the view threw an unexpected exception;
	/// <see cref="WorkspaceDocumentManagerOpenResult.Failure"/> carries the detail. Only
	/// <see cref="IWorkspaceDocumentManager.OpenWithViewAsync"/> produces this outcome:
	/// <see cref="IWorkspaceDocumentManager.OpenAsync"/> lets an unexpected store exception fault its
	/// task, and invalid options are argument errors that throw instead of producing this outcome.
	/// </summary>
	OpenFailed,

	/// <summary>The operation was canceled.</summary>
	Canceled
}

/// <summary>
/// Contains the outcome of a manager logical-mutation operation.
/// </summary>
/// <remarks>
/// The mutation itself never blocks on attached views, so <see cref="StoreResult"/> is always
/// populated. A mutation that completed while a view could not acknowledge or refresh reports its
/// store outcome together with <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/>.
/// A logical mutation never touches disk, so the result carries no failure detail.
/// </remarks>
public sealed class WorkspaceDocumentManagerMutationResult
{
	private WorkspaceDocumentManagerMutationResult(
		WorkspaceDocumentMutationResult storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization)
	{
		StoreResult = storeResult;
		ViewSynchronization = viewSynchronization;
	}

	/// <summary>
	/// Creates the result of a mutation that reached the document store.
	/// </summary>
	/// <param name="storeResult">The document-authority outcome.</param>
	/// <param name="viewSynchronization">The view-synchronization outcome, which is never <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/> for a mutation that reached the store.</param>
	/// <returns>The composed mutation result.</returns>
	/// <exception cref="ArgumentException"><paramref name="viewSynchronization"/> reports <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="storeResult"/> or <paramref name="viewSynchronization"/> is <see langword="null"/>.</exception>
	public static WorkspaceDocumentManagerMutationResult FromStore(
		WorkspaceDocumentMutationResult storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization)
	{
		ArgumentNullException.ThrowIfNull(storeResult);
		ArgumentNullException.ThrowIfNull(viewSynchronization);

		if (viewSynchronization.Outcome == WorkspaceDocumentViewSynchronizationOutcome.Blocked)
			throw new ArgumentException("A result that reached the store cannot report blocked views.", nameof(viewSynchronization));

		return new(storeResult, viewSynchronization);
	}

	/// <summary>
	/// Gets the document-authority outcome.
	/// </summary>
	public WorkspaceDocumentMutationResult StoreResult { get; }

	/// <summary>
	/// Gets the current document snapshot when the document is still tracked; otherwise,
	/// <see langword="null"/>.
	/// </summary>
	public WorkspaceDocumentSnapshot? Snapshot => StoreResult.Snapshot;

	/// <summary>
	/// Gets the document-authority outcome.
	/// </summary>
	public WorkspaceDocumentMutationOutcome Outcome => StoreResult.Outcome;

	/// <summary>
	/// Gets the view-synchronization outcome.
	/// </summary>
	public WorkspaceDocumentViewSynchronizationResult ViewSynchronization { get; }
}

/// <summary>
/// Contains the outcome of a manager commit operation.
/// </summary>
/// <remarks>
/// <see cref="StoreResult"/> is <see langword="null"/> when attached views blocked the commit before
/// the store was reached. A commit that succeeded while a view stayed unsynchronized reports
/// <see cref="WorkspaceDocumentCommitOutcome.Committed"/> together with
/// <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/>.
/// </remarks>
public sealed class WorkspaceDocumentManagerCommitResult
{
	private readonly WorkspaceDocumentSnapshot? _blockedSnapshot;

	private WorkspaceDocumentManagerCommitResult(
		WorkspaceDocumentCommitResult? storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization,
		WorkspaceDocumentSnapshot? blockedSnapshot)
	{
		StoreResult = storeResult;
		ViewSynchronization = viewSynchronization;
		_blockedSnapshot = blockedSnapshot;
	}

	/// <summary>
	/// Creates the result of a commit that reached the document store.
	/// </summary>
	/// <param name="storeResult">The document-authority outcome.</param>
	/// <param name="viewSynchronization">The view-synchronization outcome, which is never <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/> for a commit that reached the store.</param>
	/// <returns>The composed commit result.</returns>
	/// <exception cref="ArgumentException"><paramref name="viewSynchronization"/> reports <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="storeResult"/> or <paramref name="viewSynchronization"/> is <see langword="null"/>.</exception>
	public static WorkspaceDocumentManagerCommitResult FromStore(
		WorkspaceDocumentCommitResult storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization)
	{
		ArgumentNullException.ThrowIfNull(storeResult);
		ArgumentNullException.ThrowIfNull(viewSynchronization);

		if (viewSynchronization.Outcome == WorkspaceDocumentViewSynchronizationOutcome.Blocked)
			throw new ArgumentException("A result that reached the store cannot report blocked views.", nameof(viewSynchronization));

		return new(storeResult, viewSynchronization, null);
	}

	/// <summary>
	/// Creates the result of a commit that attached views blocked before the store was reached.
	/// </summary>
	/// <param name="snapshot">The current document snapshot when the document is still tracked; otherwise, <see langword="null"/>.</param>
	/// <param name="viewSynchronization">The blocking view-synchronization outcome.</param>
	/// <returns>The composed commit result.</returns>
	/// <exception cref="ArgumentException"><paramref name="viewSynchronization"/> does not report <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="viewSynchronization"/> is <see langword="null"/>.</exception>
	public static WorkspaceDocumentManagerCommitResult Blocked(
		WorkspaceDocumentSnapshot? snapshot,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization)
	{
		ArgumentNullException.ThrowIfNull(viewSynchronization);

		if (viewSynchronization.Outcome != WorkspaceDocumentViewSynchronizationOutcome.Blocked)
			throw new ArgumentException("A result with no store outcome must report blocked views.", nameof(viewSynchronization));

		return new(null, viewSynchronization, snapshot);
	}

	/// <summary>
	/// Gets the document-authority outcome; <see langword="null"/> when attached views blocked the
	/// operation.
	/// </summary>
	public WorkspaceDocumentCommitResult? StoreResult { get; }

	/// <summary>
	/// Gets the current document snapshot when the document is still tracked; otherwise,
	/// <see langword="null"/>.
	/// </summary>
	public WorkspaceDocumentSnapshot? Snapshot => StoreResult?.Snapshot ?? _blockedSnapshot;

	/// <summary>
	/// Gets the document-authority outcome, or <see langword="null"/> when attached views blocked the
	/// operation before the store was reached.
	/// </summary>
	public WorkspaceDocumentCommitOutcome? Outcome => StoreResult?.Outcome;

	/// <summary>
	/// Gets the failure that explains a failed commit, when one occurred; <see langword="null"/> when
	/// attached views blocked the operation or the commit succeeded.
	/// </summary>
	public WorkspaceOperationFailure? Failure => StoreResult?.Failure;

	/// <summary>
	/// Gets the view-synchronization outcome.
	/// </summary>
	public WorkspaceDocumentViewSynchronizationResult ViewSynchronization { get; }
}

/// <summary>
/// Contains the outcome of a manager rename operation.
/// </summary>
/// <remarks>
/// <see cref="StoreResult"/> is always populated: the rename never blocks on attached views. A rename
/// that completed while a view could not accept the new identity
/// reports <see cref="WorkspaceDocumentRenameOutcome.Renamed"/> together with
/// <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/>. The renamed identity is
/// only in <see cref="Snapshot"/>; the wrapped store result echoes the identity supplied with the
/// request.
/// </remarks>
public sealed class WorkspaceDocumentManagerRenameResult
{
	private WorkspaceDocumentManagerRenameResult(
		WorkspaceDocumentRenameResult storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization)
	{
		StoreResult = storeResult;
		ViewSynchronization = viewSynchronization;
	}

	/// <summary>
	/// Creates the result of a rename that reached the document store.
	/// </summary>
	/// <param name="storeResult">The document-authority outcome.</param>
	/// <param name="viewSynchronization">The view-synchronization outcome, which is never <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/> for a rename that reached the store.</param>
	/// <returns>The composed rename result.</returns>
	/// <exception cref="ArgumentException"><paramref name="viewSynchronization"/> reports <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="storeResult"/> or <paramref name="viewSynchronization"/> is <see langword="null"/>.</exception>
	public static WorkspaceDocumentManagerRenameResult FromStore(
		WorkspaceDocumentRenameResult storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization)
	{
		ArgumentNullException.ThrowIfNull(storeResult);
		ArgumentNullException.ThrowIfNull(viewSynchronization);

		if (viewSynchronization.Outcome == WorkspaceDocumentViewSynchronizationOutcome.Blocked)
			throw new ArgumentException("A result that reached the store cannot report blocked views.", nameof(viewSynchronization));

		return new(storeResult, viewSynchronization);
	}

	/// <summary>
	/// Gets the document-authority outcome.
	/// </summary>
	public WorkspaceDocumentRenameResult StoreResult { get; }

	/// <summary>
	/// Gets the current document snapshot when the document is still tracked; otherwise,
	/// <see langword="null"/>.
	/// </summary>
	public WorkspaceDocumentSnapshot? Snapshot => StoreResult.Snapshot;

	/// <summary>
	/// Gets the document-authority outcome.
	/// </summary>
	public WorkspaceDocumentRenameOutcome Outcome => StoreResult.Outcome;

	/// <summary>
	/// Gets the failure that explains a failed rename, when one occurred.
	/// </summary>
	public WorkspaceOperationFailure? Failure => StoreResult.Failure;

	/// <summary>
	/// Gets the view-synchronization outcome.
	/// </summary>
	public WorkspaceDocumentViewSynchronizationResult ViewSynchronization { get; }
}

/// <summary>
/// Contains the outcome of a manager save-as operation.
/// </summary>
/// <remarks>
/// <see cref="StoreResult"/> is always populated: the save never blocks on attached views. A save
/// that completed while a view could not accept the new identity
/// reports <see cref="WorkspaceDocumentSaveAsOutcome.SavedAs"/> together with
/// <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/>. The retargeted identity
/// is only in <see cref="Snapshot"/>; the wrapped store result echoes the identity supplied with
/// the request.
/// </remarks>
public sealed class WorkspaceDocumentManagerSaveAsResult
{
	private WorkspaceDocumentManagerSaveAsResult(
		WorkspaceDocumentSaveAsResult storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization)
	{
		StoreResult = storeResult;
		ViewSynchronization = viewSynchronization;
	}

	/// <summary>
	/// Creates the result of a save-as that reached the document store.
	/// </summary>
	/// <param name="storeResult">The document-authority outcome.</param>
	/// <param name="viewSynchronization">The view-synchronization outcome, which is never <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/> for a save-as that reached the store.</param>
	/// <returns>The composed save-as result.</returns>
	/// <exception cref="ArgumentException"><paramref name="viewSynchronization"/> reports <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="storeResult"/> or <paramref name="viewSynchronization"/> is <see langword="null"/>.</exception>
	public static WorkspaceDocumentManagerSaveAsResult FromStore(
		WorkspaceDocumentSaveAsResult storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization)
	{
		ArgumentNullException.ThrowIfNull(storeResult);
		ArgumentNullException.ThrowIfNull(viewSynchronization);

		if (viewSynchronization.Outcome == WorkspaceDocumentViewSynchronizationOutcome.Blocked)
			throw new ArgumentException("A result that reached the store cannot report blocked views.", nameof(viewSynchronization));

		return new(storeResult, viewSynchronization);
	}

	/// <summary>
	/// Gets the document-authority outcome.
	/// </summary>
	public WorkspaceDocumentSaveAsResult StoreResult { get; }

	/// <summary>
	/// Gets the current document snapshot when the document is still tracked; otherwise,
	/// <see langword="null"/>.
	/// </summary>
	public WorkspaceDocumentSnapshot? Snapshot => StoreResult.Snapshot;

	/// <summary>
	/// Gets the document-authority outcome.
	/// </summary>
	public WorkspaceDocumentSaveAsOutcome Outcome => StoreResult.Outcome;

	/// <summary>
	/// Gets the failure that explains a failed save-as, when one occurred.
	/// </summary>
	public WorkspaceOperationFailure? Failure => StoreResult.Failure;

	/// <summary>
	/// Gets the view-synchronization outcome.
	/// </summary>
	public WorkspaceDocumentViewSynchronizationResult ViewSynchronization { get; }
}

/// <summary>
/// Contains the outcome of a manager delete operation.
/// </summary>
/// <remarks>
/// <see cref="StoreResult"/> is <see langword="null"/> when attached views blocked the delete before
/// the store was reached. A delete whose outcome is not
/// <see cref="WorkspaceDocumentDeleteOutcome.Deleted"/> reports
/// <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/> when a guard could not be
/// released. A successful delete reports it when a guard could not be released or a view could not be
/// closed; guard-release failures on a successful delete are only reported, not recorded on the
/// view, because the view is closed by the operation anyway.
/// </remarks>
public sealed class WorkspaceDocumentManagerDeleteResult
{
	private readonly WorkspaceDocumentSnapshot? _blockedSnapshot;

	private WorkspaceDocumentManagerDeleteResult(
		WorkspaceDocumentDeleteResult? storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization,
		WorkspaceDocumentSnapshot? blockedSnapshot)
	{
		StoreResult = storeResult;
		ViewSynchronization = viewSynchronization;
		_blockedSnapshot = blockedSnapshot;
	}

	/// <summary>
	/// Creates the result of a delete that reached the document store.
	/// </summary>
	/// <param name="storeResult">The document-authority outcome.</param>
	/// <param name="viewSynchronization">The view-synchronization outcome, which is never <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/> for a delete that reached the store.</param>
	/// <returns>The composed delete result.</returns>
	/// <exception cref="ArgumentException"><paramref name="viewSynchronization"/> reports <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="storeResult"/> or <paramref name="viewSynchronization"/> is <see langword="null"/>.</exception>
	public static WorkspaceDocumentManagerDeleteResult FromStore(
		WorkspaceDocumentDeleteResult storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization)
	{
		ArgumentNullException.ThrowIfNull(storeResult);
		ArgumentNullException.ThrowIfNull(viewSynchronization);

		if (viewSynchronization.Outcome == WorkspaceDocumentViewSynchronizationOutcome.Blocked)
			throw new ArgumentException("A result that reached the store cannot report blocked views.", nameof(viewSynchronization));

		return new(storeResult, viewSynchronization, null);
	}

	/// <summary>
	/// Creates the result of a delete that attached views blocked before the store was reached.
	/// </summary>
	/// <param name="snapshot">The current document snapshot when the document is still tracked; otherwise, <see langword="null"/>.</param>
	/// <param name="viewSynchronization">The blocking view-synchronization outcome.</param>
	/// <returns>The composed delete result.</returns>
	/// <exception cref="ArgumentException"><paramref name="viewSynchronization"/> does not report <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="viewSynchronization"/> is <see langword="null"/>.</exception>
	public static WorkspaceDocumentManagerDeleteResult Blocked(
		WorkspaceDocumentSnapshot? snapshot,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization)
	{
		ArgumentNullException.ThrowIfNull(viewSynchronization);

		if (viewSynchronization.Outcome != WorkspaceDocumentViewSynchronizationOutcome.Blocked)
			throw new ArgumentException("A result with no store outcome must report blocked views.", nameof(viewSynchronization));

		return new(null, viewSynchronization, snapshot);
	}

	/// <summary>
	/// Gets the document-authority outcome; <see langword="null"/> when attached views blocked the
	/// operation.
	/// </summary>
	public WorkspaceDocumentDeleteResult? StoreResult { get; }

	/// <summary>
	/// Gets the pre-removal snapshot when the document was removed; otherwise, the current tracked
	/// state the store reported. <see langword="null"/> when the delete was blocked before reaching
	/// the store or the store reported the document as not found.
	/// </summary>
	public WorkspaceDocumentSnapshot? Snapshot => StoreResult?.Snapshot ?? _blockedSnapshot;

	/// <summary>
	/// Gets the document-authority outcome, or <see langword="null"/> when attached views blocked the
	/// operation before the store was reached.
	/// </summary>
	public WorkspaceDocumentDeleteOutcome? Outcome => StoreResult?.Outcome;

	/// <summary>
	/// Gets the failure that explains a failed delete, when one occurred; <see langword="null"/> when
	/// attached views blocked the operation or the delete succeeded.
	/// </summary>
	public WorkspaceOperationFailure? Failure => StoreResult?.Failure;

	/// <summary>
	/// Gets the view-synchronization outcome.
	/// </summary>
	public WorkspaceDocumentViewSynchronizationResult ViewSynchronization { get; }
}

/// <summary>
/// Contains the outcome of a manager conflict-resolution operation.
/// </summary>
/// <remarks>
/// <see cref="StoreResult"/> is <see langword="null"/> when attached views blocked the resolution
/// before the store was reached. A resolution that completed while a view could not refresh reports
/// <see cref="WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk"/> or
/// <see cref="WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical"/> together with
/// <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/>.
/// </remarks>
public sealed class WorkspaceDocumentManagerConflictResolutionResult
{
	private readonly WorkspaceDocumentSnapshot? _blockedSnapshot;

	private WorkspaceDocumentManagerConflictResolutionResult(
		WorkspaceDocumentConflictResolutionResult? storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization,
		WorkspaceDocumentSnapshot? blockedSnapshot)
	{
		StoreResult = storeResult;
		ViewSynchronization = viewSynchronization;
		_blockedSnapshot = blockedSnapshot;
	}

	/// <summary>
	/// Creates the result of a resolution that reached the document store.
	/// </summary>
	/// <param name="storeResult">The document-authority outcome.</param>
	/// <param name="viewSynchronization">The view-synchronization outcome, which is never <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/> for a resolution that reached the store.</param>
	/// <returns>The composed resolution result.</returns>
	/// <exception cref="ArgumentException"><paramref name="viewSynchronization"/> reports <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="storeResult"/> or <paramref name="viewSynchronization"/> is <see langword="null"/>.</exception>
	public static WorkspaceDocumentManagerConflictResolutionResult FromStore(
		WorkspaceDocumentConflictResolutionResult storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization)
	{
		ArgumentNullException.ThrowIfNull(storeResult);
		ArgumentNullException.ThrowIfNull(viewSynchronization);

		if (viewSynchronization.Outcome == WorkspaceDocumentViewSynchronizationOutcome.Blocked)
			throw new ArgumentException("A result that reached the store cannot report blocked views.", nameof(viewSynchronization));

		return new(storeResult, viewSynchronization, null);
	}

	/// <summary>
	/// Creates the result of a resolution that attached views blocked before the store was reached.
	/// </summary>
	/// <param name="snapshot">The current document snapshot when the document is still tracked; otherwise, <see langword="null"/>.</param>
	/// <param name="viewSynchronization">The blocking view-synchronization outcome.</param>
	/// <returns>The composed resolution result.</returns>
	/// <exception cref="ArgumentException"><paramref name="viewSynchronization"/> does not report <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="viewSynchronization"/> is <see langword="null"/>.</exception>
	public static WorkspaceDocumentManagerConflictResolutionResult Blocked(
		WorkspaceDocumentSnapshot? snapshot,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization)
	{
		ArgumentNullException.ThrowIfNull(viewSynchronization);

		if (viewSynchronization.Outcome != WorkspaceDocumentViewSynchronizationOutcome.Blocked)
			throw new ArgumentException("A result with no store outcome must report blocked views.", nameof(viewSynchronization));

		return new(null, viewSynchronization, snapshot);
	}

	/// <summary>
	/// Gets the document-authority outcome; <see langword="null"/> when attached views blocked the
	/// operation.
	/// </summary>
	public WorkspaceDocumentConflictResolutionResult? StoreResult { get; }

	/// <summary>
	/// Gets the current document snapshot when the document is still tracked; otherwise,
	/// <see langword="null"/>.
	/// </summary>
	public WorkspaceDocumentSnapshot? Snapshot => StoreResult?.Snapshot ?? _blockedSnapshot;

	/// <summary>
	/// Gets the document-authority outcome, or <see langword="null"/> when attached views blocked the
	/// operation before the store was reached.
	/// </summary>
	public WorkspaceDocumentConflictResolutionOutcome? Outcome => StoreResult?.Outcome;

	/// <summary>
	/// Gets the failure that explains a failed resolution, when one occurred; <see langword="null"/>
	/// when attached views blocked the operation or the resolution succeeded.
	/// </summary>
	public WorkspaceOperationFailure? Failure => StoreResult?.Failure;

	/// <summary>
	/// Gets the view-synchronization outcome.
	/// </summary>
	public WorkspaceDocumentViewSynchronizationResult ViewSynchronization { get; }
}

/// <summary>
/// Contains the outcome of a manager reload operation.
/// </summary>
/// <remarks>
/// <see cref="StoreResult"/> is <see langword="null"/> when attached views blocked the reload before
/// the store was reached. A reload that replaced the document content while a view could not refresh
/// reports <see cref="WorkspaceDocumentReloadOutcome.Reloaded"/> together with
/// <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/>.
/// </remarks>
public sealed class WorkspaceDocumentManagerReloadResult
{
	private readonly WorkspaceDocumentSnapshot? _blockedSnapshot;

	private WorkspaceDocumentManagerReloadResult(
		WorkspaceDocumentReloadResult? storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization,
		WorkspaceDocumentSnapshot? blockedSnapshot)
	{
		StoreResult = storeResult;
		ViewSynchronization = viewSynchronization;
		_blockedSnapshot = blockedSnapshot;
	}

	/// <summary>
	/// Creates the result of a reload that reached the document store.
	/// </summary>
	/// <param name="storeResult">The document-authority outcome.</param>
	/// <param name="viewSynchronization">The view-synchronization outcome, which is never <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/> for a reload that reached the store.</param>
	/// <returns>The composed reload result.</returns>
	/// <exception cref="ArgumentException"><paramref name="viewSynchronization"/> reports <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="storeResult"/> or <paramref name="viewSynchronization"/> is <see langword="null"/>.</exception>
	public static WorkspaceDocumentManagerReloadResult FromStore(
		WorkspaceDocumentReloadResult storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization)
	{
		ArgumentNullException.ThrowIfNull(storeResult);
		ArgumentNullException.ThrowIfNull(viewSynchronization);

		if (viewSynchronization.Outcome == WorkspaceDocumentViewSynchronizationOutcome.Blocked)
			throw new ArgumentException("A result that reached the store cannot report blocked views.", nameof(viewSynchronization));

		return new(storeResult, viewSynchronization, null);
	}

	/// <summary>
	/// Creates the result of a reload that attached views blocked before the store was reached.
	/// </summary>
	/// <param name="snapshot">The current document snapshot when the document is still tracked; otherwise, <see langword="null"/>.</param>
	/// <param name="viewSynchronization">The blocking view-synchronization outcome.</param>
	/// <returns>The composed reload result.</returns>
	/// <exception cref="ArgumentException"><paramref name="viewSynchronization"/> does not report <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="viewSynchronization"/> is <see langword="null"/>.</exception>
	public static WorkspaceDocumentManagerReloadResult Blocked(
		WorkspaceDocumentSnapshot? snapshot,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization)
	{
		ArgumentNullException.ThrowIfNull(viewSynchronization);

		if (viewSynchronization.Outcome != WorkspaceDocumentViewSynchronizationOutcome.Blocked)
			throw new ArgumentException("A result with no store outcome must report blocked views.", nameof(viewSynchronization));

		return new(null, viewSynchronization, snapshot);
	}

	/// <summary>
	/// Gets the document-authority outcome; <see langword="null"/> when attached views blocked the
	/// operation.
	/// </summary>
	public WorkspaceDocumentReloadResult? StoreResult { get; }

	/// <summary>
	/// Gets the current document snapshot when the document is still tracked; otherwise,
	/// <see langword="null"/>.
	/// </summary>
	public WorkspaceDocumentSnapshot? Snapshot => StoreResult?.Snapshot ?? _blockedSnapshot;

	/// <summary>
	/// Gets the document-authority outcome, or <see langword="null"/> when attached views blocked the
	/// operation before the store was reached.
	/// </summary>
	public WorkspaceDocumentReloadOutcome? Outcome => StoreResult?.Outcome;

	/// <summary>
	/// Gets the failure that explains a failed reload, when one occurred; <see langword="null"/> when
	/// attached views blocked the operation or the reload succeeded.
	/// </summary>
	public WorkspaceOperationFailure? Failure => StoreResult?.Failure;

	/// <summary>
	/// Gets the view-synchronization outcome.
	/// </summary>
	public WorkspaceDocumentViewSynchronizationResult ViewSynchronization { get; }
}

/// <summary>
/// Contains the outcome of a manager directory-rename operation.
/// </summary>
/// <remarks>
/// <see cref="StoreResult"/> is <see langword="null"/> when attached views blocked the move before
/// the store was reached; <see cref="Snapshots"/> then carries the source snapshots of the listed
/// documents. A move that completed while a view could not accept its new identity reports
/// <see cref="WorkspaceDocumentDirectoryRenameOutcome.Renamed"/> together with
/// <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/>.
/// </remarks>
public sealed class WorkspaceDocumentManagerDirectoryRenameResult
{
	private readonly IReadOnlyList<WorkspaceDocumentSnapshot> _blockedSnapshots;

	private WorkspaceDocumentManagerDirectoryRenameResult(
		WorkspaceDocumentDirectoryRenameResult? storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization,
		IReadOnlyList<WorkspaceDocumentSnapshot> blockedSnapshots)
	{
		StoreResult = storeResult;
		ViewSynchronization = viewSynchronization;
		_blockedSnapshots = blockedSnapshots;
	}

	/// <summary>
	/// Creates the result of a directory rename that reached the document store.
	/// </summary>
	/// <param name="storeResult">The document-authority outcome.</param>
	/// <param name="viewSynchronization">The view-synchronization outcome, which is never <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/> for a move that reached the store.</param>
	/// <returns>The composed directory-rename result.</returns>
	/// <exception cref="ArgumentException"><paramref name="viewSynchronization"/> reports <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="storeResult"/> or <paramref name="viewSynchronization"/> is <see langword="null"/>.</exception>
	public static WorkspaceDocumentManagerDirectoryRenameResult FromStore(
		WorkspaceDocumentDirectoryRenameResult storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization)
	{
		ArgumentNullException.ThrowIfNull(storeResult);
		ArgumentNullException.ThrowIfNull(viewSynchronization);

		if (viewSynchronization.Outcome == WorkspaceDocumentViewSynchronizationOutcome.Blocked)
			throw new ArgumentException("A result that reached the store cannot report blocked views.", nameof(viewSynchronization));

		return new(storeResult, viewSynchronization, []);
	}

	/// <summary>
	/// Creates the result of a directory rename that attached views blocked before the store was
	/// reached.
	/// </summary>
	/// <param name="sourceSnapshots">The source snapshots of the listed documents.</param>
	/// <param name="viewSynchronization">The blocking view-synchronization outcome.</param>
	/// <returns>The composed directory-rename result.</returns>
	/// <exception cref="ArgumentException"><paramref name="viewSynchronization"/> does not report <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="sourceSnapshots"/> or <paramref name="viewSynchronization"/> is <see langword="null"/>.</exception>
	public static WorkspaceDocumentManagerDirectoryRenameResult Blocked(
		IReadOnlyList<WorkspaceDocumentSnapshot> sourceSnapshots,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization)
	{
		ArgumentNullException.ThrowIfNull(sourceSnapshots);
		ArgumentNullException.ThrowIfNull(viewSynchronization);

		if (viewSynchronization.Outcome != WorkspaceDocumentViewSynchronizationOutcome.Blocked)
			throw new ArgumentException("A result with no store outcome must report blocked views.", nameof(viewSynchronization));

		return new(null, viewSynchronization, sourceSnapshots);
	}

	/// <summary>
	/// Gets the document-authority outcome; <see langword="null"/> when attached views blocked the
	/// operation.
	/// </summary>
	public WorkspaceDocumentDirectoryRenameResult? StoreResult { get; }

	/// <summary>
	/// Gets the current snapshots of the listed documents when available; otherwise, the source
	/// snapshots.
	/// </summary>
	public IReadOnlyList<WorkspaceDocumentSnapshot> Snapshots => StoreResult?.Snapshots ?? _blockedSnapshots;

	/// <summary>
	/// Gets the document-authority outcome, or <see langword="null"/> when attached views blocked the
	/// operation before the store was reached.
	/// </summary>
	public WorkspaceDocumentDirectoryRenameOutcome? Outcome => StoreResult?.Outcome;

	/// <summary>
	/// Gets the failure that explains a failed move, when one occurred; <see langword="null"/> when
	/// attached views blocked the operation or the move succeeded.
	/// </summary>
	public WorkspaceOperationFailure? Failure => StoreResult?.Failure;

	/// <summary>
	/// Gets the view-synchronization outcome.
	/// </summary>
	public WorkspaceDocumentViewSynchronizationResult ViewSynchronization { get; }
}

/// <summary>
/// Contains the outcome of a manager directory-delete operation.
/// </summary>
/// <remarks>
/// <see cref="StoreResult"/> is <see langword="null"/> when attached views blocked the delete before
/// the store was reached; <see cref="Snapshots"/> then carries the source snapshots of the listed
/// documents. A delete that completed while a guard could not be released or a view could not be
/// closed reports <see cref="WorkspaceDocumentDirectoryDeleteOutcome.Deleted"/> together with
/// <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/>.
/// </remarks>
public sealed class WorkspaceDocumentManagerDirectoryDeleteResult
{
	private readonly IReadOnlyList<WorkspaceDocumentSnapshot> _blockedSnapshots;

	private WorkspaceDocumentManagerDirectoryDeleteResult(
		WorkspaceDocumentDirectoryDeleteResult? storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization,
		IReadOnlyList<WorkspaceDocumentSnapshot> blockedSnapshots)
	{
		StoreResult = storeResult;
		ViewSynchronization = viewSynchronization;
		_blockedSnapshots = blockedSnapshots;
	}

	/// <summary>
	/// Creates the result of a directory delete that reached the document store.
	/// </summary>
	/// <param name="storeResult">The document-authority outcome.</param>
	/// <param name="viewSynchronization">The view-synchronization outcome, which is never <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/> for a delete that reached the store.</param>
	/// <returns>The composed directory-delete result.</returns>
	/// <exception cref="ArgumentException"><paramref name="viewSynchronization"/> reports <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="storeResult"/> or <paramref name="viewSynchronization"/> is <see langword="null"/>.</exception>
	public static WorkspaceDocumentManagerDirectoryDeleteResult FromStore(
		WorkspaceDocumentDirectoryDeleteResult storeResult,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization)
	{
		ArgumentNullException.ThrowIfNull(storeResult);
		ArgumentNullException.ThrowIfNull(viewSynchronization);

		if (viewSynchronization.Outcome == WorkspaceDocumentViewSynchronizationOutcome.Blocked)
			throw new ArgumentException("A result that reached the store cannot report blocked views.", nameof(viewSynchronization));

		return new(storeResult, viewSynchronization, []);
	}

	/// <summary>
	/// Creates the result of a directory delete that attached views blocked before the store was
	/// reached.
	/// </summary>
	/// <param name="sourceSnapshots">The source snapshots of the listed documents.</param>
	/// <param name="viewSynchronization">The blocking view-synchronization outcome.</param>
	/// <returns>The composed directory-delete result.</returns>
	/// <exception cref="ArgumentException"><paramref name="viewSynchronization"/> does not report <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="sourceSnapshots"/> or <paramref name="viewSynchronization"/> is <see langword="null"/>.</exception>
	public static WorkspaceDocumentManagerDirectoryDeleteResult Blocked(
		IReadOnlyList<WorkspaceDocumentSnapshot> sourceSnapshots,
		WorkspaceDocumentViewSynchronizationResult viewSynchronization)
	{
		ArgumentNullException.ThrowIfNull(sourceSnapshots);
		ArgumentNullException.ThrowIfNull(viewSynchronization);

		if (viewSynchronization.Outcome != WorkspaceDocumentViewSynchronizationOutcome.Blocked)
			throw new ArgumentException("A result with no store outcome must report blocked views.", nameof(viewSynchronization));

		return new(null, viewSynchronization, sourceSnapshots);
	}

	/// <summary>
	/// Gets the document-authority outcome; <see langword="null"/> when attached views blocked the
	/// operation.
	/// </summary>
	public WorkspaceDocumentDirectoryDeleteResult? StoreResult { get; }

	/// <summary>
	/// Gets the snapshots of the listed documents; the source snapshots when attached views blocked
	/// the operation.
	/// </summary>
	public IReadOnlyList<WorkspaceDocumentSnapshot> Snapshots => StoreResult?.Snapshots ?? _blockedSnapshots;

	/// <summary>
	/// Gets the document-authority outcome, or <see langword="null"/> when attached views blocked the
	/// operation before the store was reached.
	/// </summary>
	public WorkspaceDocumentDirectoryDeleteOutcome? Outcome => StoreResult?.Outcome;

	/// <summary>
	/// Gets the failure that explains a failed delete, when one occurred; <see langword="null"/> when
	/// attached views blocked the operation or the delete succeeded.
	/// </summary>
	public WorkspaceOperationFailure? Failure => StoreResult?.Failure;

	/// <summary>
	/// Gets the view-synchronization outcome.
	/// </summary>
	public WorkspaceDocumentViewSynchronizationResult ViewSynchronization { get; }
}
