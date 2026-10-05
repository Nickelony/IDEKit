namespace Nickelony.IDEKit.Workspace.FileSystem;

/// <summary>Provides asynchronous file-system operations required by the document authority.</summary>
/// <remarks>
/// Implementations return operation results for expected environmental failures and may throw for ones they cannot translate; the paths the store
/// supplies are normalized document ids. A cancellation token is observed before any work and again before a mutation. The deletion policy
/// is documented on <see cref="DeleteAsync"/>.
/// </remarks>
public interface IWorkspaceFileSystem
{
	/// <summary>Reads a file and captures its content stamp.</summary>
	/// <param name="path">The file path to read.</param>
	/// <param name="cancellationToken">A token that can cancel the read.</param>
	/// <returns>The read content or raw bytes together with the captured stamp.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	/// <exception cref="OperationCanceledException">The read was canceled.</exception>
	/// <remarks>
	/// An implementation
	/// returns <see cref="WorkspaceFileReadResult.FromContent"/> for content it decoded or <see cref="WorkspaceFileReadResult.FromBytes"/>
	/// for bytes the store decodes; missing files return <see cref="WorkspaceFileReadResult.Missing"/>, and a directory path
	/// must return <see cref="WorkspaceFileReadResult.Directory"/>.
	/// </remarks>
	Task<WorkspaceFileReadResult> ReadAsync(string path, CancellationToken cancellationToken = default);

	/// <summary>Captures the current stamp for a file-system path.</summary>
	/// <param name="path">The file path to inspect.</param>
	/// <param name="cancellationToken">A token that can cancel the capture.</param>
	/// <returns>
	/// The current stamp, <see cref="FileStamp.Missing"/> when the file does not exist, or <see cref="FileStamp.Directory"/> when the path exists
	/// as a directory instead of a file.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	/// <exception cref="OperationCanceledException">The capture was canceled.</exception>
	/// <remarks>
	/// Capturing a stamp for an existing file reads and hashes its content, so the cost is proportional to the file size. A file that disappears
	/// or becomes inaccessible after the existence probe surfaces the underlying I/O exception. The length and hash describe one byte sequence,
	/// so a writer that changes the file mid-capture can yield a stamp matching neither the old nor the new state, which the next stamp check reports
	/// as a conflict.
	/// </remarks>
	Task<FileStamp> CaptureStampAsync(string path, CancellationToken cancellationToken = default);

	/// <summary>Conditionally writes content to a destination file after validating its expected stamp.</summary>
	/// <param name="destinationPath">The file path to replace or create.</param>
	/// <param name="content">The bytes to write.</param>
	/// <param name="expectedStamp">The destination stamp that must still match.</param>
	/// <param name="cancellationToken">A token that can cancel the write.</param>
	/// <returns>The replacement outcome, including the observed destination stamp when it could be captured.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="destinationPath"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The implementation owns the atomic write: it stages the bytes in a temporary file beside the destination and removes that file on every path,
	/// so the caller supplies content, never a temporary path. The default <see cref="LocalWorkspaceFileSystem"/> keeps the file only when it holds
	/// the only copy of the new content, which the failure names, or when another process holds it open past the cleanup's retries. The staging file
	/// is visible to a watcher on the destination directory; the default names it <c>.{guid}.tmp</c>, which a host watcher ignores.
	/// </remarks>
	Task<WorkspaceFileReplacementResult> WriteFileAsync(
		string destinationPath,
		ReadOnlyMemory<byte> content,
		FileStamp expectedStamp,
		CancellationToken cancellationToken = default
	);

	/// <summary>Moves a file after validating its expected source stamp.</summary>
	/// <param name="sourcePath">The file path to move.</param>
	/// <param name="destinationPath">The destination path.</param>
	/// <param name="expectedSourceStamp">The source stamp that must still match.</param>
	/// <param name="cancellationToken">A token that can cancel the move.</param>
	/// <returns>
	/// The move outcome, including the source stamp observed when a conflict was detected or the resulting destination stamp after a successful move,
	/// when available.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="sourcePath"/> or <paramref name="destinationPath"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// A move that fails after its destination may have been touched reports <see cref="WorkspaceFileMoveOutcome.MoveStateUnknown"/> when its final
	/// state cannot be established, for example a case-only move whose rollback move also failed; a successful move reports the resulting destination
	/// stamp in <see cref="WorkspaceFileMoveResult.ObservedOnDiskStamp"/>.
	/// </remarks>
	Task<WorkspaceFileMoveResult> MoveAsync(
		string sourcePath,
		string destinationPath,
		FileStamp expectedSourceStamp,
		CancellationToken cancellationToken = default
	);

	/// <summary>Moves a directory without a source-stamp precondition.</summary>
	/// <param name="sourcePath">The directory path to move.</param>
	/// <param name="destinationPath">The destination directory path.</param>
	/// <param name="cancellationToken">A token that can cancel the move.</param>
	/// <returns>The move outcome, including the unknown state of a failed case-only move that could not be rolled back.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="sourcePath"/> or <paramref name="destinationPath"/> is <see langword="null"/>.
	/// </exception>
	Task<WorkspaceFileMoveResult> MoveDirectoryAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);

	/// <summary>Deletes a file after validating its expected stamp.</summary>
	/// <param name="path">The file path to delete.</param>
	/// <param name="expectedStamp">The file stamp that must still match.</param>
	/// <param name="cancellationToken">A token that can cancel the delete.</param>
	/// <returns>
	/// The deletion outcome; a path that is absent after a matching stamp reports <see cref="WorkspaceFileDeleteOutcome.Deleted"/>,
	/// and a path that exists as a directory reports <see cref="WorkspaceFileDeleteOutcome.DeleteFailed"/>
	/// with <see cref="WorkspaceOperationFailureCodes.IsDirectory"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// Deletion is permanent unless the implementation chooses a recoverable policy, such as a trash folder or the shell recycle bin,
	/// and documents it on its own type. The default <see cref="LocalWorkspaceFileSystem"/> removes the file with <see cref="File.Delete(string)"/>,
	/// which does not use the recycle bin. <see cref="DeleteDirectoryAsync"/> follows the same policy, and a host that needs recoverable deletion
	/// overrides <see cref="DeleteAsync"/> and <see cref="DeleteDirectoryAsync"/> in a <see cref="WorkspaceFileSystemDecorator"/> subclass.
	/// </remarks>
	Task<WorkspaceFileDeleteResult> DeleteAsync(string path, FileStamp expectedStamp, CancellationToken cancellationToken = default);

	/// <summary>Deletes a directory recursively.</summary>
	/// <param name="path">The directory path to delete.</param>
	/// <param name="cancellationToken">A token that can cancel the delete.</param>
	/// <returns>
	/// The deletion outcome; a path that is absent reports <see cref="WorkspaceFileDeleteOutcome.Deleted"/>, and a path that exists as a file reports
	/// <see cref="WorkspaceFileDeleteOutcome.DeleteFailed"/> with <see cref="WorkspaceOperationFailureCodes.NotDirectory"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default);
}
