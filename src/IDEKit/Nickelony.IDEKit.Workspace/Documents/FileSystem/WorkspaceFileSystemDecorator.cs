namespace Nickelony.IDEKit.Workspace.Documents.FileSystem;

/// <summary>
/// Forwards every <see cref="IWorkspaceFileSystem"/> member to an inner implementation so a host can
/// override only the operations that need host policy.
/// </summary>
/// <remarks>
/// <para>
/// Derive from this type and override the members that implement host behavior - routing a delete
/// through a trash store, for example - while the remaining members keep forwarding to
/// <see cref="Inner"/>. <see cref="LocalWorkspaceFileSystem"/> is the intended inner implementation
/// for local files; see its remarks for the deletion and permission behavior a decorator inherits.
/// </para>
/// <para>
/// An override should preserve the documented contract of the member it replaces: expected
/// environmental failures are reported through the result, an unexpected exception may be thrown, and
/// an expected stamp is validated before a mutation. A decorator that changes those guarantees changes
/// them for the document store as well.
/// </para>
/// </remarks>
public abstract class WorkspaceFileSystemDecorator : IWorkspaceFileSystem
{
	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceFileSystemDecorator"/> class.
	/// </summary>
	/// <param name="inner">The file system that receives the forwarded operations.</param>
	/// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
	protected WorkspaceFileSystemDecorator(IWorkspaceFileSystem inner)
	{
		ArgumentNullException.ThrowIfNull(inner);

		Inner = inner;
	}

	/// <summary>
	/// Gets the file system that receives the forwarded operations.
	/// </summary>
	protected IWorkspaceFileSystem Inner { get; }

	/// <inheritdoc/>
	public virtual Task<WorkspaceFileReadResult> ReadAsync(
		string path,
		CancellationToken cancellationToken = default)
		=> Inner.ReadAsync(path, cancellationToken);

	/// <inheritdoc/>
	public virtual Task<FileStamp> CaptureStampAsync(
		string path,
		CancellationToken cancellationToken = default)
		=> Inner.CaptureStampAsync(path, cancellationToken);

	/// <inheritdoc/>
	public virtual Task<WorkspaceFileReplacementResult> WriteFileAsync(
		string destinationPath,
		ReadOnlyMemory<byte> content,
		FileStamp expectedStamp,
		CancellationToken cancellationToken = default)
		=> Inner.WriteFileAsync(destinationPath, content, expectedStamp, cancellationToken);

	/// <inheritdoc/>
	public virtual Task<WorkspaceFileMoveResult> MoveAsync(
		string sourcePath,
		string destinationPath,
		FileStamp expectedSourceStamp,
		CancellationToken cancellationToken = default)
		=> Inner.MoveAsync(sourcePath, destinationPath, expectedSourceStamp, cancellationToken);

	/// <inheritdoc/>
	public virtual Task<WorkspaceFileMoveResult> MoveDirectoryAsync(
		string sourcePath,
		string destinationPath,
		CancellationToken cancellationToken = default)
		=> Inner.MoveDirectoryAsync(sourcePath, destinationPath, cancellationToken);

	/// <inheritdoc/>
	public virtual Task<WorkspaceFileDeleteResult> DeleteAsync(
		string path,
		FileStamp expectedStamp,
		CancellationToken cancellationToken = default)
		=> Inner.DeleteAsync(path, expectedStamp, cancellationToken);

	/// <inheritdoc/>
	public virtual Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(
		string path,
		CancellationToken cancellationToken = default)
		=> Inner.DeleteDirectoryAsync(path, cancellationToken);
}
