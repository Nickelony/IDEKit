using Nickelony.IDEKit.Core.Paths;

namespace Nickelony.IDEKit.Workspace.FileSystem;

/// <summary>Provides the default local-disk <see cref="IWorkspaceFileSystem"/> implementation used by the document store.</summary>
/// <remarks>
/// The deletion policy is documented on <see cref="IWorkspaceFileSystem.DeleteAsync"/>. A delete validates
/// the path kind, so a directory passed to a file delete, or a file passed to a directory delete, fails instead of succeeding silently.
/// Permission failures are reported through the operation result where it can carry one and surface as <see cref="UnauthorizedAccessException"/>
/// where it cannot. On Windows, a replace, a move or a file delete that fails with a sharing or lock violation, which leaves every file
/// under its own name, runs again a bounded number of times after short delays, and the operation's precondition is checked again before each retry;
/// a directory delete is not retried, because a recursive delete that fails midway has already removed part of the tree.
/// </remarks>
public sealed class LocalWorkspaceFileSystem : IWorkspaceFileSystem
{
	private readonly AtomicFileWriter _writer;
	private readonly FileMover _mover;
	private readonly FileDeleter _deleter;

	/// <param name="options">
	/// The options shared with the document store and the reload coordinator; its comparison describes the target file system.
	/// It decides how a case-only move (for example <c>document.txt</c> to <c>Document.txt</c>) is handled: a case-insensitive policy routes
	/// the move through a temporary intermediate path because the destination resolves to the source file, while a case-sensitive policy treats
	/// the destination spelling as a distinct path. The default follows the operating system; supply the same value that the rest of the layer uses
	/// when the file system's semantics differ from it.
	/// </param>
	public LocalWorkspaceFileSystem(WorkspaceOptions? options = null)
		: this(options, WorkspaceFileSystemPrimitives.Default) { }

	internal LocalWorkspaceFileSystem(WorkspaceOptions? options, WorkspaceFileSystemPrimitives primitives)
	{
		LocalPathComparisonPolicy pathComparison = (options ?? WorkspaceOptions.Default).PathComparison;

		_writer = new AtomicFileWriter(primitives);
		_mover = new FileMover(pathComparison, primitives);
		_deleter = new FileDeleter(primitives);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// For an existing file, the result carries raw bytes in the <see cref="WorkspaceFileReadResult.FromBytes"/> state; decoding is deferred
	/// to the document store. A missing file returns <see cref="WorkspaceFileReadResult.Missing"/>.
	/// </remarks>
	public async Task<WorkspaceFileReadResult> ReadAsync(string path, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(path);
		return await FileStampReader.ReadAsync(path, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public async Task<FileStamp> CaptureStampAsync(string path, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(path);
		return await FileStampReader.CaptureAsync(path, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Stages the bytes in a temporary file beside the destination and flushes them to the physical device before the destination is touched,
	/// so a crash after a reported write cannot expose a zero-length or partial destination. The temporary file is removed on every path,
	/// except when it holds the only copy of the new content or another process keeps it open past the cleanup's retries. A stale destination
	/// is rejected before staging by a cheap metadata probe, so a stale expectation never flushes a temporary file only to discard it, and the probe
	/// runs again after staging.
	/// </remarks>
	public async Task<WorkspaceFileReplacementResult> WriteFileAsync(
		string destinationPath,
		ReadOnlyMemory<byte> content,
		FileStamp expectedStamp,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(destinationPath);
		return await _writer.WriteAsync(destinationPath, content, expectedStamp, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// A case-only move on a case-insensitive target uses a temporary intermediate path, so the file is briefly moved to it before it receives
	/// the requested spelling; when the rollback move of that path also fails, the file can be left at the intermediate path and the failure
	/// is reported as <see cref="WorkspaceFileMoveOutcome.MoveStateUnknown"/>, with an exception that aggregates the original move failure
	/// and the rollback failure.
	/// </remarks>
	public async Task<WorkspaceFileMoveResult> MoveAsync(
		string sourcePath,
		string destinationPath,
		FileStamp expectedSourceStamp,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(sourcePath);
		ArgumentNullException.ThrowIfNull(destinationPath);

		return await _mover.MoveAsync(sourcePath, destinationPath, expectedSourceStamp, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// A path that is absent after a matching stamp reports <see cref="WorkspaceFileDeleteOutcome.Deleted"/>
	/// because the requested end state already holds; a path that exists as a directory reports <see cref="WorkspaceFileDeleteOutcome.DeleteFailed"/>
	/// with <see cref="WorkspaceOperationFailureCodes.IsDirectory"/>.
	/// </remarks>
	public async Task<WorkspaceFileDeleteResult> DeleteAsync(string path, FileStamp expectedStamp, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(path);
		return await _deleter.DeleteAsync(path, expectedStamp, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// A move whose first attempt succeeds or fails for good runs synchronously, so the returned
	/// task is already completed; only a retry after a sharing or lock violation completes it later. <paramref name="cancellationToken"/> is observed
	/// before the move starts and during the delay before a retry. A case-only move on a case-insensitive target uses a temporary intermediate path
	/// like <see cref="MoveAsync(string, string, FileStamp, CancellationToken)"/>.
	/// </remarks>
	public Task<WorkspaceFileMoveResult> MoveDirectoryAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(sourcePath);
		ArgumentNullException.ThrowIfNull(destinationPath);

		return _mover.MoveDirectoryAsync(sourcePath, destinationPath, cancellationToken);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// A path
	/// that is absent reports <see cref="WorkspaceFileDeleteOutcome.Deleted"/> because the requested end state already holds; a path that exists
	/// as a file reports <see cref="WorkspaceFileDeleteOutcome.DeleteFailed"/> with <see cref="WorkspaceOperationFailureCodes.NotDirectory"/>.
	/// The deletion runs synchronously, so the returned task is already completed.
	/// </remarks>
	public Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(path);
		return FileDeleter.DeleteDirectoryAsync(path, cancellationToken);
	}
}
