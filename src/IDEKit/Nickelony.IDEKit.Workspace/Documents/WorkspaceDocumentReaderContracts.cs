using Nickelony.IDEKit.Core.Pathing;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Provides read access to tracked workspace documents.
/// </summary>
/// <remarks>
/// <para>
/// This is the read-side slice of <see cref="IWorkspaceDocumentStore"/>. Consumers that only inspect
/// document state - for example analyzers or language services - can depend on this interface
/// instead of the full store surface. Opening a document is part of read access because it is the
/// step that acquires the document for reading.
/// </para>
/// <para>
/// Snapshot lookups are point-in-time and do not wait for in-flight operations: a document whose
/// delete is in flight can still be listed until the operation completes, while
/// <see cref="OpenAsync"/> waits for the delete to resolve.
/// </para>
/// <para>
/// On members that instead accept a path (<see cref="OpenAsync"/>, <see cref="TryGetSnapshot"/>,
/// and <see cref="GetSnapshotsUnderDirectory"/>), a <see langword="null"/>, blank, or unnormalizable path is invalid
/// input and is reported through the result or return value instead of throwing.
/// </para>
/// </remarks>
public interface IWorkspaceDocumentReader
{
	/// <summary>
	/// Gets the path comparison policy used for document identities.
	/// </summary>
	/// <remarks>
	/// Consumers that track the same documents, such as a view manager or a reload coordinator, should
	/// use this value so a customized policy cannot drift between the store and its consumers.
	/// </remarks>
	LocalPathComparisonPolicy PathComparison { get; }

	/// <summary>Opens a document and loads its content from disk, or creates an empty logical document for a missing path.</summary>
	/// <remarks>
	/// The path is normalized for document identity (including trailing directory separators) while the
	/// supplied path is retained as
	/// <see cref="WorkspaceDocumentSnapshot.DisplayPath"/>. Only fully qualified paths are accepted: a
	/// relative path is invalid input, because resolving it against the process current directory
	/// would bind document identity to ambient process state. Opening an already tracked path returns
	/// <see cref="WorkspaceDocumentOpenOutcome.AlreadyOpen"/> without reading it again; when a delete of
	/// that document is in flight, the call waits for the delete to resolve and then re-evaluates the
	/// path: a failed delete reports <see cref="WorkspaceDocumentOpenOutcome.AlreadyOpen"/>, and a
	/// completed delete makes the call a fresh open. An open also waits for an in-flight directory
	/// rename or recursive delete that covers the path, for a rename or save-as that reserves the
	/// path as its destination, and for an earlier open of the same path, then re-evaluates. A path
	/// that exists as a directory is rejected with
	/// <see cref="WorkspaceDocumentOpenOutcome.IsDirectory"/>. A missing path
	/// uses <see cref="WorkspaceDocumentOpenOptions.NewFileFormat"/>. A call that has already started -
	/// it owns or joined a reservation - and then observes the disposal of the store reports
	/// <see cref="WorkspaceDocumentOpenOutcome.Canceled"/>; only a call made after disposal throws
	/// <see cref="ObjectDisposedException"/>.
	/// </remarks>
	/// <param name="filePath">The path to open; a <see langword="null"/>, blank, or unnormalizable path reports <see cref="WorkspaceDocumentOpenOutcome.InvalidPath"/>.</param>
	/// <param name="options">The encoding and new-file format defaults for the load.</param>
	/// <param name="cancellationToken">A token that can cancel the load.</param>
	/// <returns>The open outcome with a snapshot for a successful or already-open document, or a failure outcome without a snapshot.</returns>
	/// <exception cref="ArgumentException"><paramref name="options"/> uses a NewFileFormat that combines Windows-1252 with a byte-order mark.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/> uses an undefined text encoding.</exception>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	Task<WorkspaceDocumentOpenResult> OpenAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		CancellationToken cancellationToken = default);

	/// <summary>Gets tracked document snapshots for documents below a directory.</summary>
	/// <remarks>The result is ordered by normalized document id and is empty for a <see langword="null"/>, blank, or invalid directory path, or when the directory has no tracked descendants. A document tracked at the exact directory path is included, matching the subtree operations: a directory rename or delete reaches it too.</remarks>
	/// <param name="directoryPath">The directory path to inspect; a <see langword="null"/>, blank, or unnormalizable path returns an empty list.</param>
	/// <returns>The tracked snapshots at or below the directory ordered by normalized document id; otherwise, an empty list.</returns>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	IReadOnlyList<WorkspaceDocumentSnapshot> GetSnapshotsUnderDirectory(string? directoryPath);

	/// <summary>Tries to get a tracked document snapshot by path.</summary>
	/// <param name="filePath">The path to look up; a <see langword="null"/>, blank, or unnormalizable path reports <see langword="false"/>.</param>
	/// <param name="snapshot">The current snapshot when the path is tracked; otherwise, <see langword="null"/>.</param>
	/// <returns><see langword="true"/> and the current snapshot when the path is tracked; otherwise, <see langword="false"/> and <see langword="null"/>.</returns>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	bool TryGetSnapshot(string? filePath, out WorkspaceDocumentSnapshot? snapshot);
}
