using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Owns logical workspace documents and their persisted file-system state.
/// </summary>
/// <remarks>
/// <para>
/// Requests use a normalized <c>DocumentId</c>, a <see cref="WorkspaceDocumentKey"/>, and an expected
/// <c>Version</c> for optimistic concurrency: operation results report expected-state failures instead
/// of applying a request to a different document state. The store composes
/// <see cref="IWorkspaceDocumentReader"/> with its logical-mutation and disk-persistence members, so a
/// read-only consumer can take the reader slice; mutations and disk persistence stay together because
/// they share the store's version, disk gate, and conflict state. The contracts here describe document
/// authority only - view coordination outcomes are composed by the view manager
/// (<c>Nickelony.IDEKit.Workspace.Views.IWorkspaceDocumentManager</c>), which wraps a store result
/// instead of extending it with view outcomes.
/// </para>
/// <list type="bullet">
/// <item>On members that accept a request, the request's <c>DocumentId</c> is validated before any
/// document state is inspected (a <see langword="null"/> id throws <see cref="ArgumentNullException"/>,
/// an empty or whitespace id throws <see cref="ArgumentException"/>), but after the disposal check, so
/// a disposed store reports <see cref="ObjectDisposedException"/> first.</item>
/// <item>Members that accept a path (<see cref="IWorkspaceDocumentReader.OpenAsync"/>,
/// <see cref="IWorkspaceDocumentReader.TryGetSnapshot"/>,
/// <see cref="IWorkspaceDocumentReader.GetSnapshotsUnderDirectory"/>, rename or save-as destinations,
/// and directory rename/delete paths) treat a <see langword="null"/>, blank, or unnormalizable path as
/// invalid input and report it through their result instead of throwing; the reader members check
/// disposal first, so a disposed store reports <see cref="ObjectDisposedException"/> even for an
/// otherwise invalid path.</item>
/// <item>Cancellation is observed before a file-system mutation starts, so a canceled result means the
/// file was not written, moved, or deleted. The one exception is a token canceled after a completed
/// replacement: the completed write is reported as
/// <see cref="WorkspaceDocumentCommitOutcome.ReplacementStateUnknown"/> instead of
/// <see cref="WorkspaceDocumentCommitOutcome.Canceled"/>, because a completed write must not be
/// reported as canceled.</item>
/// <item>An operation already started when the store is disposed reports its <c>Canceled</c> outcome;
/// only a member invoked after disposal throws <see cref="ObjectDisposedException"/>.</item>
/// <item>The directory operations and the per-operation result records stay on this interface: the
/// directory operations cannot work without the store's open reservations, disk gates, and tracked
/// rebase/removal, and a shared generic result would collapse only the five identical document results
/// while leaving two coexisting shapes for one concept, so each operation keeps its own outcome
/// vocabulary.</item>
/// </list>
/// </remarks>
public interface IWorkspaceDocumentStore : IAsyncDisposable, IWorkspaceDocumentReader
{
	/// <summary>Replaces the logical content and format of a tracked document without writing to disk.</summary>
	/// <remarks>
	/// The request is accepted only when its identity matches the current snapshot and no delete
	/// operation is active; a replacement cannot interleave with a delete. A replacement does not
	/// take the per-document disk gate, so it can interleave with an in-flight commit, reload,
	/// rename, or save-as by design: the disk operation completes against its captured snapshot,
	/// and a later logical edit remains dirty against the installed baseline.
	/// </remarks>
	/// <param name="request">The replacement request.</param>
	/// <returns>The mutation outcome; a request that matches the tracked content and format reports <see cref="WorkspaceDocumentMutationOutcome.NoChange"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> or <see cref="WorkspaceDocumentReplaceRequest.Content"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The request's document id is empty or whitespace, or when the request's file format combines Windows-1252 with a byte-order mark.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The request's file format uses an undefined text encoding.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	WorkspaceDocumentMutationResult Replace(
		WorkspaceDocumentReplaceRequest request);

	/// <summary>Conditionally commits a tracked document's captured logical content and format to disk.</summary>
	/// <remarks>
	/// <para>
	/// The expected on-disk stamp is the caller's precondition. An accepted commit that writes
	/// validates it against the conditional replacement's own capture of the destination, so the
	/// file is read and hashed once. Requests rejected by the document, key, or version checks
	/// return before any file access.
	/// </para>
	/// <para>
	/// A clean tracked document whose file exists has nothing to write: the commit reports
	/// <see cref="WorkspaceDocumentCommitOutcome.Committed"/> as a no-op without capturing a stamp or
	/// touching disk. On that path the expected stamp is validated against the tracked stamp instead
	/// of a fresh capture: an expectation that disagrees with the state the store last observed
	/// reports <see cref="WorkspaceDocumentCommitOutcome.ExternalFileConflict"/> with the tracked
	/// stamp as the observed stamp, so a no-op cannot be mistaken for a fresh on-disk match.
	/// </para>
	/// <para>
	/// A later logical edit made while the commit is in progress remains in the returned snapshot as
	/// dirty content, while the captured version becomes the persisted baseline. A commit that
	/// observes a different on-disk stamp records that observation as the document's tracked stamp
	/// even when the commit itself did not write. A replacement whose
	/// final state cannot be determined is reported as
	/// <see cref="WorkspaceDocumentCommitOutcome.ReplacementStateUnknown"/>.
	/// </para>
	/// </remarks>
	/// <param name="request">The commit request.</param>
	/// <param name="cancellationToken">A token that can cancel the commit.</param>
	/// <returns>The commit outcome with the current snapshot when the document is still tracked.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The request's document id is empty or whitespace.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	Task<WorkspaceDocumentCommitResult> CommitAsync(
		WorkspaceDocumentCommitRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Reloads a tracked document from disk, preserving unsaved logical content.</summary>
	/// <remarks>
	/// <para>
	/// A dirty document is not overwritten. The method captures the current disk stamp: when it differs
	/// from the stamp the store tracks, the reload reports
	/// <see cref="WorkspaceDocumentReloadOutcome.ExternalFileConflict"/> with the observed stamp so the
	/// host can resolve the conflict; when it matches, nothing changed externally since the store last
	/// observed the file and the reload reports <see cref="WorkspaceDocumentReloadOutcome.Unchanged"/>.
	/// The request's version must still match before the outcome is reported.
	/// </para>
	/// <para>
	/// For a clean document, the fresh disk stamp is compared with the stamp the store tracks. A
	/// matching stamp reports <see cref="WorkspaceDocumentReloadOutcome.Unchanged"/>; a different
	/// stamp is adopted and reports <see cref="WorkspaceDocumentReloadOutcome.Reloaded"/>, so a file
	/// that changed since the store last observed it is never mistaken for current state. A file
	/// that is missing on disk reloads as empty content with a missing stamp and keeps the document's
	/// current format. A path that exists as a directory fails the reload with
	/// <see cref="WorkspaceDocumentReloadOutcome.ReadFailed"/> and the
	/// <see cref="WorkspaceOperationFailureCodes.IsDirectory"/> failure code instead of being adopted.
	/// </para>
	/// <para>
	/// Reloads of dirty documents bypass the per-document disk gate and are not registered as active
	/// operations; a dirty reload in flight during disposal completes with
	/// <see cref="WorkspaceDocumentReloadOutcome.Canceled"/>. A delete in flight rejects the reload with
	/// <see cref="WorkspaceDocumentReloadOutcome.OperationInProgress"/> instead of reporting
	/// <see cref="WorkspaceDocumentReloadOutcome.Unchanged"/> or an external file conflict for a
	/// document the delete is about to remove.
	/// </para>
	/// </remarks>
	/// <param name="request">The reload request.</param>
	/// <param name="cancellationToken">A token that can cancel the reload.</param>
	/// <returns>The reload outcome with the current snapshot when the document is still tracked.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The request's document id is empty or whitespace.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	Task<WorkspaceDocumentReloadResult> ReloadAsync(
		WorkspaceDocumentReloadRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Resolves an external file conflict by choosing disk content or logical content.</summary>
	/// <remarks>
	/// <see cref="WorkspaceDocumentConflictResolutionChoice.UseLogical"/> writes the logical snapshot
	/// after capturing the current disk stamp. <see cref="WorkspaceDocumentConflictResolutionChoice.UseDisk"/>
	/// reads and adopts the current disk content. The supplied observed stamp is revalidated before
	/// either choice is applied; if the disk changed again after the conflict was observed, the
	/// resolution reports <see cref="WorkspaceDocumentConflictResolutionOutcome.ExternalFileConflict"/>
	/// instead of overwriting or adopting the newer state. Under <see cref="WorkspaceDocumentConflictResolutionChoice.UseDisk"/>,
	/// a file that is missing on disk resolves as empty content with a missing stamp and keeps the
	/// document's current format, while
	/// <see cref="WorkspaceDocumentConflictResolutionChoice.UseLogical"/> creates the missing file
	/// from the logical content. A path that exists as a directory fails the resolution with
	/// <see cref="WorkspaceDocumentConflictResolutionOutcome.ReadFailed"/> and the
	/// <see cref="WorkspaceOperationFailureCodes.IsDirectory"/> failure code.
	/// </remarks>
	/// <param name="request">The conflict-resolution request.</param>
	/// <param name="cancellationToken">A token that can cancel the resolution.</param>
	/// <returns>The resolution outcome with the current snapshot when the document is still tracked.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The request's document id is empty or whitespace.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The request's conflict resolution choice is not a defined value.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	Task<WorkspaceDocumentConflictResolutionResult> ResolveExternalConflictAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Discards unsaved logical content and format changes by restoring the persisted baseline.</summary>
	/// <remarks>
	/// This is a synchronous logical mutation; it does not write to disk. A discard does not race a
	/// disk operation: it reports <see cref="WorkspaceDocumentMutationOutcome.OperationInProgress"/>
	/// while another operation holds the document's disk gate, and while a delete is active.
	/// </remarks>
	/// <param name="request">The discard request.</param>
	/// <returns>The mutation outcome; a clean document reports <see cref="WorkspaceDocumentMutationOutcome.NoChange"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The request's document id is empty or whitespace.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	WorkspaceDocumentMutationResult Discard(
		WorkspaceDocumentDiscardRequest request);

	/// <summary>Moves a tracked document to a new path and updates its document identity path.</summary>
	/// <remarks>
	/// The logical document key is preserved; attached-view synchronization is the manager's
	/// responsibility. A rename whose expected source stamp is missing retargets the document identity
	/// without a file-system move and keeps the missing stamp, so a not-yet-written document can be
	/// renamed and later saved at the new path. The source is re-checked first: a file - or a
	/// directory - that appeared since the caller's snapshot reports
	/// <see cref="WorkspaceDocumentRenameOutcome.ExternalFileConflict"/>.
	/// </remarks>
	/// <param name="request">The rename request; its destination path is normalized for document identity.</param>
	/// <param name="cancellationToken">A token that can cancel the move.</param>
	/// <returns>The rename outcome with the current snapshot when the document is still tracked.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The request's document id is empty or whitespace.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	Task<WorkspaceDocumentRenameResult> RenameAsync(
		WorkspaceDocumentRenameRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Writes a tracked document at a destination path and retargets the tracked document to that path.</summary>
	/// <remarks>
	/// The original file is not deleted. A destination that resolves to the document's own file is
	/// saved in place and reports <see cref="WorkspaceDocumentSaveAsOutcome.SavedAs"/>. The logical
	/// document key is preserved.
	/// </remarks>
	/// <param name="request">The save-as request; its destination path is normalized for document identity.</param>
	/// <param name="cancellationToken">A token that can cancel the write.</param>
	/// <returns>The save-as outcome with the current snapshot when the document is still tracked.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The request's document id is empty or whitespace.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	Task<WorkspaceDocumentSaveAsResult> SaveAsAsync(
		WorkspaceDocumentSaveAsRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Deletes a tracked document's file and removes the document from the store after a successful delete.</summary>
	/// <remarks>Deletion uses the file system supplied to the store; see <see cref="IWorkspaceFileSystem"/> remarks for host-specific deletion behavior.</remarks>
	/// <param name="request">The delete request.</param>
	/// <param name="cancellationToken">A token that can cancel the delete.</param>
	/// <returns>The delete outcome; a successful result carries the snapshot captured before removal.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The request's document id is empty or whitespace.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	Task<WorkspaceDocumentDeleteResult> DeleteAsync(
		WorkspaceDocumentDeleteRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Moves a directory and updates the paths of the tracked documents below it.</summary>
	/// <remarks>
	/// <para>
	/// Every tracked descendant of the source directory is validated against the stamp the store
	/// currently tracks before the directory move, and the move rebases every descendant's identity path
	/// onto the destination. Normalized directory ids are handed to the file system; a case-only
	/// difference is completed by the default file system through an intermediate rename on a
	/// case-insensitive target.
	/// </para>
	/// <list type="bullet">
	/// <item>A destination path identical to the source path after normalization reports
	/// <see cref="WorkspaceDocumentDirectoryRenameOutcome.NoChange"/> with the current snapshots,
	/// mirroring <see cref="WorkspaceDocumentRenameOutcome.NoChange"/> for files.</item>
	/// <item>While the move runs, the source subtree is reserved: an open for a path inside it waits for
	/// the move to finish and then reads the post-move state (the vacated path after a rename), and a
	/// file rename or save-as into the subtree reports
	/// <see cref="WorkspaceDocumentRenameOutcome.DestinationBusy"/> or
	/// <see cref="WorkspaceDocumentSaveAsOutcome.DestinationBusy"/>.</item>
	/// <item>In-flight loads and writes inside the subtree are awaited before the physical move, so no
	/// document can be created at a path the move is about to vacate.</item>
	/// </list>
	/// </remarks>
	/// <param name="request">The directory-rename request.</param>
	/// <param name="cancellationToken">A token that can cancel the move.</param>
	/// <returns>The rename outcome with the current snapshots of the tracked descendants.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	Task<WorkspaceDocumentDirectoryRenameResult> RenameDirectoryAsync(
		WorkspaceDocumentDirectoryRenameRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Recursively deletes a directory and removes the tracked documents below it after success.</summary>
	/// <remarks>
	/// <para>
	/// Every tracked descendant of the directory is validated against the stamp the store currently
	/// tracks before deletion, and every descendant is removed from tracking after a successful delete.
	/// Deletion uses the file system supplied to the store; see <see cref="IWorkspaceFileSystem"/> remarks
	/// for host-specific deletion behavior.
	/// </para>
	/// <list type="bullet">
	/// <item>While the delete runs, the directory subtree is reserved: an open for a path inside it waits
	/// for the delete to finish and then reads the post-delete state, and a file rename or save-as into
	/// the subtree reports a busy destination.</item>
	/// <item>In-flight loads and writes inside the subtree are awaited before the physical delete.</item>
	/// </list>
	/// </remarks>
	/// <param name="request">The directory-delete request.</param>
	/// <param name="cancellationToken">A token that can cancel the delete.</param>
	/// <returns>The delete outcome with the snapshots of the removed tracked descendants.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	Task<WorkspaceDocumentDirectoryDeleteResult> DeleteDirectoryAsync(
		WorkspaceDocumentDirectoryDeleteRequest request,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// Requests persistence of the current logical document content.
/// </summary>
/// <param name="Identity">The document instance, id, and version expected by the caller.</param>
/// <param name="ExpectedOnDiskStamp">The on-disk stamp that must still match before writing.</param>
public sealed record WorkspaceDocumentCommitRequest(
	WorkspaceDocumentRequestIdentity Identity,
	FileStamp ExpectedOnDiskStamp);

/// <summary>
/// Describes the outcome of persisting a workspace document.
/// </summary>
public enum WorkspaceDocumentCommitOutcome
{
	/// <summary>The captured logical content and format were installed as the persisted baseline; a clean existing document commits as a no-op.</summary>
	Committed,

	/// <summary>The document version was stale.</summary>
	StaleDocument,

	/// <summary>The tracked document instance does not match the request's document key.</summary>
	StaleDocumentInstance,

	/// <summary>The document was not found.</summary>
	DocumentNotFound,

	/// <summary>Another operation is in progress.</summary>
	OperationInProgress,

	/// <summary>The stamp expected by the commit disagrees with the store's state: it is validated against a fresh capture when the commit writes, or against the tracked stamp for a clean no-op.</summary>
	ExternalFileConflict,

	/// <summary>The write did not complete because of an unexpected error.</summary>
	WriteFailed,

	/// <summary>The replacement state is unknown.</summary>
	ReplacementStateUnknown,

	/// <summary>The operation was canceled.</summary>
	Canceled
}

/// <summary>
/// Contains the outcome of persisting a workspace document.
/// </summary>
/// <remarks>
/// <see cref="Snapshot"/> is the current tracked state when the document exists, including for
/// most failures. It is <see langword="null"/> when the document was not found.
/// </remarks>
/// <param name="Outcome">The commit outcome.</param>
/// <param name="RequestedIdentity">The document instance, id, and version supplied with the request.</param>
/// <param name="Snapshot">The current tracked state; <see langword="null"/> when the document was not found.</param>
/// <param name="ObservedOnDiskStamp">
/// The stamp that closes the operation. After a successful write it is the resulting on-disk stamp,
/// reported by the replacement or re-captured when the file system does not report one; a completed
/// replacement whose stamp cannot be established reports
/// <see cref="WorkspaceDocumentCommitOutcome.ReplacementStateUnknown"/>. It is the stamp observed when
/// a conflict or failure was detected, and <see langword="null"/> when none was observed.
/// </param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceDocumentCommitResult(
	WorkspaceDocumentCommitOutcome Outcome,
	WorkspaceDocumentRequestIdentity RequestedIdentity,
	WorkspaceDocumentSnapshot? Snapshot,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Requests that a workspace document be reloaded from disk.
/// </summary>
/// <param name="Identity">The document instance, id, and version expected by the caller.</param>
public sealed record WorkspaceDocumentReloadRequest(
	WorkspaceDocumentRequestIdentity Identity);

/// <summary>
/// Describes the outcome of reloading a workspace document.
/// </summary>
public enum WorkspaceDocumentReloadOutcome
{
	/// <summary>The logical content and persisted baseline were replaced with the current disk content.</summary>
	Reloaded,

	/// <summary>The document was already current.</summary>
	Unchanged,

	/// <summary>The document version was stale.</summary>
	StaleDocument,

	/// <summary>The tracked document instance does not match the request's document key.</summary>
	StaleDocumentInstance,

	/// <summary>The document was not found.</summary>
	DocumentNotFound,

	/// <summary>Another operation is in progress, including a delete in flight.</summary>
	OperationInProgress,

	/// <summary>The logical document is dirty and the disk changed since the store last observed it.</summary>
	ExternalFileConflict,

	/// <summary>The read did not complete because of an unexpected error.</summary>
	ReadFailed,

	/// <summary>The operation was canceled.</summary>
	Canceled
}

/// <summary>
/// Contains the outcome of reloading a workspace document.
/// </summary>
/// <remarks>
/// <see cref="ObservedOnDiskStamp"/> reports the stamp obtained during the operation when one was
/// available. <see cref="Snapshot"/> is the current tracked state for a tracked document and is
/// <see langword="null"/> only when the document was not found.
/// </remarks>
/// <param name="Outcome">The reload outcome.</param>
/// <param name="RequestedIdentity">The document instance, id, and version supplied with the request.</param>
/// <param name="Snapshot">The current tracked state; <see langword="null"/> only when the document was not found.</param>
/// <param name="ObservedOnDiskStamp">The stamp obtained during the operation, when available.</param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceDocumentReloadResult(
	WorkspaceDocumentReloadOutcome Outcome,
	WorkspaceDocumentRequestIdentity RequestedIdentity,
	WorkspaceDocumentSnapshot? Snapshot,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Chooses the source of truth when logical content conflicts with disk content.
/// </summary>
public enum WorkspaceDocumentConflictResolutionChoice
{
	/// <summary>Use the content currently on disk.</summary>
	UseDisk,

	/// <summary>Keep the logical document content and write it to disk.</summary>
	UseLogical
}

/// <summary>
/// Requests resolution of an external workspace document conflict.
/// </summary>
/// <param name="Identity">The document instance, id, and version expected by the caller.</param>
/// <param name="ObservedOnDiskStamp">The disk stamp that caused the conflict; it is revalidated before the resolution is applied.</param>
/// <param name="Choice">The source of truth to use when resolving the conflict.</param>
public sealed record WorkspaceDocumentConflictResolutionRequest(
	WorkspaceDocumentRequestIdentity Identity,
	FileStamp ObservedOnDiskStamp,
	WorkspaceDocumentConflictResolutionChoice Choice);

/// <summary>
/// Describes the outcome of resolving an external workspace document conflict.
/// </summary>
public enum WorkspaceDocumentConflictResolutionOutcome
{
	/// <summary>The conflict was resolved with disk content.</summary>
	ResolvedWithDisk,

	/// <summary>The conflict was resolved with logical content.</summary>
	ResolvedWithLogical,

	/// <summary>The document version was stale.</summary>
	StaleDocument,

	/// <summary>The tracked document instance does not match the request's document key.</summary>
	StaleDocumentInstance,

	/// <summary>The document was not found.</summary>
	DocumentNotFound,

	/// <summary>Another operation is in progress.</summary>
	OperationInProgress,

	/// <summary>The disk state changed again while the conflict was being resolved.</summary>
	ExternalFileConflict,

	/// <summary>The read did not complete because of an unexpected error.</summary>
	ReadFailed,

	/// <summary>The write did not complete because of an unexpected error.</summary>
	WriteFailed,

	/// <summary>The replacement state is unknown.</summary>
	ReplacementStateUnknown,

	/// <summary>The operation was canceled.</summary>
	Canceled
}

/// <summary>
/// Contains the outcome of resolving an external workspace document conflict.
/// </summary>
/// <remarks>
/// <see cref="Choice"/> echoes the requested source of truth. For <see cref="WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical"/>,
/// the logical snapshot was committed; for <see cref="WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk"/>,
/// it was replaced with freshly read disk content. A later logical edit may leave the returned
/// snapshot dirty even when the resolution itself succeeded. The required members are positional and
/// the observed stamp and the failure form the trailing optional tail, so the shape is uniform with
/// the rest of the result family.
/// </remarks>
/// <param name="Outcome">The resolution outcome.</param>
/// <param name="Choice">The source of truth carried by the request.</param>
/// <param name="RequestedIdentity">The document instance, id, and version supplied with the request.</param>
/// <param name="Snapshot">The current tracked state; <see langword="null"/> when the document was not found.</param>
/// <param name="ObservedOnDiskStamp">The stamp that closes the resolution: the adopted disk stamp after <see cref="WorkspaceDocumentConflictResolutionChoice.UseDisk"/> (<see cref="FileStamp.Missing"/> for a missing file), the resulting stamp after <see cref="WorkspaceDocumentConflictResolutionChoice.UseLogical"/>, or the stamp observed when a conflict or failure was detected.</param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceDocumentConflictResolutionResult(
	WorkspaceDocumentConflictResolutionOutcome Outcome,
	WorkspaceDocumentConflictResolutionChoice Choice,
	WorkspaceDocumentRequestIdentity RequestedIdentity,
	WorkspaceDocumentSnapshot? Snapshot,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null);
