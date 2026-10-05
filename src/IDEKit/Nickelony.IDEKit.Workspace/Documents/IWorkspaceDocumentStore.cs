using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Owns logical workspace documents and their persisted file-system state.</summary>
/// <remarks>
/// Requests use a normalized <c>DocumentId</c>, a <see cref="WorkspaceDocumentKey"/>, and an expected <c>Version</c> for optimistic concurrency,
/// so an operation reports an expected-state failure instead of applying to a different document state. A started operation reports
/// its <c>Canceled</c> outcome when the store is disposed; only a member invoked after disposal throws <see cref="ObjectDisposedException"/>.
/// </remarks>
public interface IWorkspaceDocumentStore : IAsyncDisposable, IWorkspaceDocumentReader
{
	/// <summary>Opens a document and loads its content from disk, or creates an empty logical document for a missing path.</summary>
	/// <param name="filePath">
	/// The path to open; a <see langword="null"/>, blank, or unnormalizable path reports <see cref="WorkspaceDocumentOpenOutcome.InvalidPath"/>.
	/// </param>
	/// <param name="options">The encoding and new-file format defaults for the load.</param>
	/// <param name="cancellationToken">A token that can cancel the load.</param>
	/// <returns>The open outcome with a snapshot for a successful or already-open document, or a failure outcome without a snapshot.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	/// <remarks>
	/// The path is normalized for document identity while the supplied path is retained as <see cref="WorkspaceDocumentSnapshot.DisplayPath"/>,
	/// and only a fully qualified path is accepted: a relative path would bind document identity to the process current directory.
	/// The open waits for any in-flight operation that covers the path - a delete, a directory rename or recursive delete, a destination reservation,
	/// or an earlier open - and then re-evaluates it. A missing path uses <see cref="WorkspaceDocumentOpenOptions.NewFileFormat"/>,
	/// a directory is rejected with <see cref="WorkspaceDocumentOpenOutcome.IsDirectory"/>, and a started call that observes the store's disposal
	/// reports <see cref="WorkspaceDocumentOpenOutcome.Canceled"/>.
	/// </remarks>
	Task<WorkspaceDocumentOpenResult> OpenAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		CancellationToken cancellationToken = default
	);

	/// <summary>Conditionally commits a tracked document's captured logical content and format to disk.</summary>
	/// <param name="request">The commit request.</param>
	/// <param name="cancellationToken">A token that can cancel the commit.</param>
	/// <returns>The commit outcome with the current snapshot when the document is still tracked.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The request's document id is empty or whitespace.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	/// <remarks>
	/// The expected on-disk stamp is the caller's precondition, validated against the conditional replacement's own capture of the destination
	/// so the file is read once; a request rejected by the document, key, or version check returns before any file access. A clean existing
	/// document commits as a no-op without touching disk, validating the expected stamp against the tracked stamp instead. A later logical edit made
	/// while the commit runs stays dirty in the returned snapshot, while the captured version becomes the persisted baseline.
	/// </remarks>
	Task<WorkspaceDocumentCommitResult> CommitAsync(WorkspaceDocumentCommitRequest request, CancellationToken cancellationToken = default);

	/// <summary>Reloads a tracked document from disk, preserving unsaved logical content.</summary>
	/// <param name="request">The reload request.</param>
	/// <param name="cancellationToken">A token that can cancel the reload.</param>
	/// <returns>The reload outcome with the current snapshot when the document is still tracked.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The request's document id is empty or whitespace.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	/// <remarks>
	/// A dirty document is never overwritten: the reload reports <see cref="WorkspaceDocumentReloadOutcome.ExternalFileConflict"/>
	/// when the disk stamp differs from the tracked stamp and <see cref="WorkspaceDocumentReloadOutcome.Unchanged"/>
	/// when it matches, whereas a request that asks for an encoding reports <see cref="WorkspaceDocumentReloadOutcome.UnsavedChanges"/>.
	/// A clean document adopts a changed stamp as <see cref="WorkspaceDocumentReloadOutcome.Reloaded"/>. A missing file reloads as empty
	/// content with a missing stamp and keeps the current format; a directory fails with <see cref="WorkspaceDocumentReloadOutcome.ReadFailed"/>.
	/// A reload and every other file operation on the document, a delete included, exclude one another: the one that arrives
	/// second reports <see cref="WorkspaceDocumentReloadOutcome.OperationInProgress"/>. A file whose bytes no candidate
	/// encoding decodes fails the reload with <see cref="WorkspaceDocumentReloadOutcome.ReadFailed"/> and the <c>InvalidEncoding</c> failure code,
	/// and a candidate that the process cannot resolve fails it with the <c>EncodingUnavailable</c> failure code; either leaves the document
	/// as it was. A plain reload of a file whose stamp the store tracks reports <see cref="WorkspaceDocumentReloadOutcome.Unchanged"/>
	/// without decoding.
	/// </remarks>
	Task<WorkspaceDocumentReloadResult> ReloadAsync(WorkspaceDocumentReloadRequest request, CancellationToken cancellationToken = default);

	/// <summary>Resolves an external file conflict by choosing disk content or logical content.</summary>
	/// <param name="request">The conflict-resolution request.</param>
	/// <param name="cancellationToken">A token that can cancel the resolution.</param>
	/// <returns>The resolution outcome with the current snapshot when the document is still tracked.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The request's document id is empty or whitespace.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The request's conflict resolution choice is not a defined value.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	/// <remarks>
	/// <see cref="WorkspaceDocumentConflictResolutionMode.ResolveWithLogical"/> writes
	/// the logical snapshot; <see cref="WorkspaceDocumentConflictResolutionMode.ResolveWithDisk"/> reads and adopts
	/// the disk content. The supplied stamp is revalidated first, so a disk that changed again reports
	/// <see cref="WorkspaceDocumentConflictResolutionOutcome.ExternalFileConflict"/> instead of overwriting or adopting newer state. A missing file
	/// resolves as empty content under <see cref="WorkspaceDocumentConflictResolutionMode.ResolveWithDisk"/> and is created from the logical content
	/// under <see cref="WorkspaceDocumentConflictResolutionMode.ResolveWithLogical"/>.
	/// </remarks>
	Task<WorkspaceDocumentConflictResolutionResult> ResolveExternalConflictAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		CancellationToken cancellationToken = default
	);

	/// <summary>Replaces the logical content and format of a tracked document without writing to disk.</summary>
	/// <returns>
	/// The mutation outcome; a request that matches the tracked content and format reports <see cref="WorkspaceDocumentMutationOutcome.NoChange"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="request"/> or <see cref="WorkspaceDocumentReplaceRequest.Content"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// The request's document id is empty or whitespace, or its file format asks for a byte-order mark that its encoding has none of.
	/// </exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	/// <remarks>
	/// The request is accepted only when its identity matches the current snapshot and no delete operation is active; a replacement cannot interleave
	/// with a delete. A replacement does not take the per-document disk gate, so it can interleave with an in-flight commit, reload, rename,
	/// or save-as by design: the disk operation completes against its captured snapshot, and a later logical edit remains dirty
	/// against the installed baseline.
	/// </remarks>
	WorkspaceDocumentMutationResult Replace(WorkspaceDocumentReplaceRequest request);

	/// <summary>Discards unsaved logical content and format changes by restoring the persisted baseline.</summary>
	/// <returns>The mutation outcome; a clean document reports <see cref="WorkspaceDocumentMutationOutcome.NoChange"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The request's document id is empty or whitespace.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	/// <remarks>
	/// This is a synchronous logical mutation; it does not write to disk. A discard does not race a disk operation:
	/// it reports <see cref="WorkspaceDocumentMutationOutcome.OperationInProgress"/> while another operation holds the document's disk gate,
	/// and while a delete is active.
	/// </remarks>
	WorkspaceDocumentMutationResult Discard(WorkspaceDocumentDiscardRequest request);

	/// <summary>Renames a tracked document: moves its file to a new path and updates its document identity path.</summary>
	/// <param name="request">The rename request; its destination path is normalized for document identity.</param>
	/// <param name="cancellationToken">A token that can cancel the rename.</param>
	/// <returns>The rename outcome with the current snapshot when the document is still tracked.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The request's document id is empty or whitespace.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	/// <remarks>
	/// The logical document key is preserved; attached-view synchronization belongs to the view layer above the store. A rename whose expected
	/// source stamp is missing retargets the document identity without a file-system move and keeps the missing stamp, so a not-yet-written document
	/// can be renamed and later saved at the new path. The source is re-checked first: a file - or a directory - that appeared since the caller's
	/// snapshot reports <see cref="WorkspaceDocumentRenameOutcome.ExternalFileConflict"/>.
	/// </remarks>
	Task<WorkspaceDocumentRenameResult> RenameAsync(WorkspaceDocumentRenameRequest request, CancellationToken cancellationToken = default);

	/// <summary>Writes a tracked document at a destination path and retargets the tracked document to that path.</summary>
	/// <param name="request">The save-as request; its destination path is normalized for document identity.</param>
	/// <param name="cancellationToken">A token that can cancel the write.</param>
	/// <returns>The save-as outcome with the current snapshot when the document is still tracked.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The request's document id is empty or whitespace.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	/// <remarks>
	/// The original file is not deleted. A destination that resolves to the document's own file is saved in place
	/// and reports <see cref="WorkspaceDocumentSaveAsOutcome.SavedAs"/>. The logical document key is preserved.
	/// </remarks>
	Task<WorkspaceDocumentSaveAsResult> SaveAsAsync(WorkspaceDocumentSaveAsRequest request, CancellationToken cancellationToken = default);

	/// <summary>Deletes a tracked document's file and removes the document from the store after a successful delete.</summary>
	/// <param name="request">The delete request.</param>
	/// <param name="cancellationToken">A token that can cancel the delete.</param>
	/// <returns>The delete outcome; a successful result carries the snapshot captured before removal.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The request's document id is empty or whitespace.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	/// <remarks>
	/// Deletion uses the file system supplied to the store, so it follows the policy on <see cref="IWorkspaceFileSystem.DeleteAsync"/>.
	/// </remarks>
	Task<WorkspaceDocumentDeleteResult> DeleteAsync(WorkspaceDocumentDeleteRequest request, CancellationToken cancellationToken = default);

	/// <summary>Renames a directory: moves it and updates the document identities of the tracked documents below it.</summary>
	/// <param name="request">The directory-rename request.</param>
	/// <param name="cancellationToken">A token that can cancel the rename.</param>
	/// <returns>The rename outcome with the current snapshots of the tracked descendants.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	/// <remarks>
	/// Every tracked descendant is validated against the tracked stamp before the move and rebased onto the destination afterwards. While the move
	/// runs the source subtree is reserved: an open inside it waits and then reads the post-move state, a file rename or save-as into it reports
	/// a busy destination, and in-flight loads and writes inside it are awaited before the physical move.
	/// </remarks>
	Task<WorkspaceDocumentDirectoryRenameResult> RenameDirectoryAsync(
		WorkspaceDocumentDirectoryRenameRequest request,
		CancellationToken cancellationToken = default
	);

	/// <summary>Recursively deletes a directory and removes the tracked documents below it after success.</summary>
	/// <param name="request">The directory-delete request.</param>
	/// <param name="cancellationToken">A token that can cancel the delete.</param>
	/// <returns>The delete outcome with the snapshots of the removed tracked descendants.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	/// <remarks>
	/// Every tracked descendant is validated before deletion and removed from tracking after success. Deletion uses the supplied
	/// file system (see <see cref="IWorkspaceFileSystem"/>). While the delete runs the directory subtree is reserved: an open inside it waits
	/// and then reads the post-delete state, a file rename or save-as into it reports a busy destination, and in-flight loads and writes
	/// inside it are awaited before the physical delete.
	/// </remarks>
	Task<WorkspaceDocumentDirectoryDeleteResult> DeleteDirectoryAsync(
		WorkspaceDocumentDirectoryDeleteRequest request,
		CancellationToken cancellationToken = default
	);
}
