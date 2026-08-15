namespace Nickelony.IDEKit.Workspace.Documents.FileSystem;

/// <summary>
/// Contains the content or the raw bytes captured while reading a path, together with the metadata of that read.
/// </summary>
/// <remarks>
/// <para>
/// A read has exactly one of four states, each produced by its own member: <see cref="Missing"/> (the
/// path does not exist), <see cref="Directory"/> (the path exists as a directory),
/// <see cref="FromBytes"/> (raw bytes that the document store decodes with its selected no-BOM encoding),
/// or <see cref="FromContent"/> (content the implementation already decoded).
/// </para>
/// <para>
/// The type is a sealed class rather than a record: a value comparison would have to compare byte
/// contents to be meaningful, and reference equality states that honestly instead of comparing only the
/// byte buffer's identity. <see cref="ResolveContent"/> is the single way to obtain the text and format
/// to adopt, so a caller cannot read <see cref="Content"/> and silently miss <see cref="RawBytes"/>. A
/// missing file has no on-disk format to adopt, so the caller supplies the fallback format for that case.
/// </para>
/// </remarks>
public sealed class WorkspaceFileReadResult
{
	private static readonly WorkspaceFileReadResult s_missing =
		new(string.Empty, default, FileStamp.Missing, null, isDirectory: false);

	private static readonly WorkspaceFileReadResult s_directory =
		new(string.Empty, default, FileStamp.Missing, null, isDirectory: true);

	private readonly ReadOnlyMemory<byte>? _rawBytes;

	private WorkspaceFileReadResult(
		string content,
		TextFileFormat fileFormat,
		FileStamp onDiskStamp,
		ReadOnlyMemory<byte>? rawBytes,
		bool isDirectory)
	{
		Content = content;
		FileFormat = fileFormat;
		OnDiskStamp = onDiskStamp;
		_rawBytes = rawBytes;
		IsDirectory = isDirectory;
	}

	/// <summary>
	/// Gets the result of a read of a path that does not exist: empty content, a default format, and
	/// <see cref="FileStamp.Missing"/>.
	/// </summary>
	public static WorkspaceFileReadResult Missing => s_missing;

	/// <summary>
	/// Gets the result of a read of a path that exists as a directory instead of a file. The result
	/// carries the missing-file stamp so a caller can reject the path instead of treating it as a
	/// missing file, and sets <see cref="IsDirectory"/> so it can distinguish the two.
	/// </summary>
	public static WorkspaceFileReadResult Directory => s_directory;

	/// <summary>
	/// Creates the result of a read that produced raw bytes.
	/// </summary>
	/// <remarks>
	/// <see cref="LocalWorkspaceFileSystem"/> uses this state for an existing file so the document store
	/// can apply its selected no-BOM encoding. Callers resolve the text through
	/// <see cref="ResolveContent"/>.
	/// </remarks>
	/// <param name="onDiskStamp">The stamp captured for the bytes.</param>
	/// <param name="rawBytes">The bytes read from the path.</param>
	/// <returns>A raw-bytes read result.</returns>
	public static WorkspaceFileReadResult FromBytes(FileStamp onDiskStamp, ReadOnlyMemory<byte> rawBytes)
		=> new(string.Empty, default, onDiskStamp, rawBytes, isDirectory: false);

	/// <summary>
	/// Creates the result of a read that already decoded the file content.
	/// </summary>
	/// <param name="content">The decoded content.</param>
	/// <param name="fileFormat">The format captured while reading.</param>
	/// <param name="onDiskStamp">The stamp captured while reading.</param>
	/// <returns>A decoded-content read result.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
	public static WorkspaceFileReadResult FromContent(string content, TextFileFormat fileFormat, FileStamp onDiskStamp)
	{
		ArgumentNullException.ThrowIfNull(content);

		return new(content, fileFormat, onDiskStamp, null, isDirectory: false);
	}

	/// <summary>
	/// Gets the decoded content, or an empty string when the content was not decoded (a raw-bytes,
	/// missing, or directory result). Use <see cref="ResolveContent"/> instead of reading this member
	/// directly.
	/// </summary>
	public string Content { get; }

	/// <summary>
	/// Gets the format captured while reading, or the default format when the read produced raw bytes.
	/// </summary>
	public TextFileFormat FileFormat { get; }

	/// <summary>
	/// Gets the stamp captured while reading; <see cref="FileStamp.Missing"/> when the path does not exist.
	/// </summary>
	public FileStamp OnDiskStamp { get; }

	/// <summary>
	/// Gets the raw bytes when the implementation provides them; otherwise, <see langword="null"/>.
	/// </summary>
	public ReadOnlyMemory<byte>? RawBytes => _rawBytes;

	/// <summary>
	/// Gets a value indicating whether the path exists as a directory instead of a file.
	/// </summary>
	public bool IsDirectory { get; }

	/// <summary>
	/// Resolves the text and the format to adopt for this read.
	/// </summary>
	/// <remarks>
	/// A decoded read returns <see cref="Content"/> and reports <see cref="FileFormat"/> when the file
	/// exists, or <paramref name="fallbackFileFormat"/> when it does not (a missing file has no on-disk
	/// format to adopt). A raw-bytes read decodes the bytes with <paramref name="noBomEncoding"/> and
	/// reports the format detected during decoding.
	/// </remarks>
	/// <param name="fallbackFileFormat">The format to adopt for a decoded read of a missing file.</param>
	/// <param name="noBomEncoding">The encoding to apply to raw bytes that carry no byte-order mark.</param>
	/// <param name="fileFormat">Receives the format to adopt for the document.</param>
	/// <returns>The text to adopt for the document.</returns>
	public string ResolveContent(TextFileFormat fallbackFileFormat, TextEncodingKind noBomEncoding, out TextFileFormat fileFormat)
	{
		if (_rawBytes is not { } rawBytes)
		{
			fileFormat = OnDiskStamp.Exists ? FileFormat : fallbackFileFormat;
			return Content;
		}

		return WorkspaceTextCodec.Decode(rawBytes.Span, noBomEncoding, out fileFormat);
	}
}

/// <summary>
/// Describes the outcome of conditionally replacing a destination file.
/// </summary>
public enum WorkspaceFileReplacementOutcome
{
	/// <summary>No outcome was recorded; a default-constructed result does not describe a completed operation.</summary>
	Unknown = 0,

	/// <summary>The destination was replaced after its stamp matched the expected stamp.</summary>
	Replaced,

	/// <summary>The destination stamp did not match the expected stamp.</summary>
	ExternalFileConflict,

	/// <summary>A directory occupies the destination path where the replacement expected no file, so the temporary file cannot be moved onto it.</summary>
	DestinationExists,

	/// <summary>The replacement may have occurred, but its final state could not be determined.</summary>
	ReplacementStateUnknown,

	/// <summary>The replacement did not complete because of an unexpected error.</summary>
	Failed,

	/// <summary>The replacement was canceled before the destination was touched.</summary>
	Canceled
}

/// <summary>
/// Contains the outcome of replacing a destination file.
/// </summary>
/// <remarks>
/// <see cref="ObservedOnDiskStamp"/> reports the stamp observed during conflict detection or the
/// resulting destination stamp after replacement when it could be captured. <see cref="Failure"/>
/// explains a failed or indeterminate operation.
/// </remarks>
/// <param name="Outcome">The replacement outcome.</param>
/// <param name="ObservedOnDiskStamp">The stamp observed during conflict detection or after replacement, when available.</param>
/// <param name="Failure">Explains a failed or indeterminate operation, when one occurred.</param>
public sealed record WorkspaceFileReplacementResult(
	WorkspaceFileReplacementOutcome Outcome,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Describes the outcome of moving a file or directory after validating the source when applicable.
/// </summary>
public enum WorkspaceFileMoveOutcome
{
	/// <summary>No outcome was recorded; a default-constructed result does not describe a completed operation.</summary>
	Unknown = 0,

	/// <summary>The source was moved.</summary>
	Moved,

	/// <summary>The destination already exists.</summary>
	DestinationExists,

	/// <summary>The source stamp did not match the expected stamp.</summary>
	ExternalFileConflict,

	/// <summary>The move did not complete because of an unexpected error.</summary>
	MoveFailed,

	/// <summary>The move failed and its final state could not be established; for example, a case-only rename whose rollback move also failed.</summary>
	MoveStateUnknown,

	/// <summary>The move was canceled.</summary>
	Canceled
}

/// <summary>
/// Contains the outcome of moving a file or directory.
/// </summary>
/// <remarks>
/// <see cref="ObservedOnDiskStamp"/> is populated when a source-stamp conflict is observed and after a
/// successful move when the implementation reports the resulting destination stamp.
/// </remarks>
/// <param name="Outcome">The move outcome.</param>
/// <param name="ObservedOnDiskStamp">The source stamp observed when a conflict was detected, or the resulting destination stamp after a successful move, when available.</param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceFileMoveResult(
	WorkspaceFileMoveOutcome Outcome,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Describes the outcome of deleting a file or directory.
/// </summary>
public enum WorkspaceFileDeleteOutcome
{
	/// <summary>No outcome was recorded; a default-constructed result does not describe a completed operation.</summary>
	Unknown = 0,

	/// <summary>The file or directory is absent after the delete operation.</summary>
	Deleted,

	/// <summary>The expected stamp did not match for a delete operation that validates a stamp.</summary>
	ExternalFileConflict,

	/// <summary>The delete did not complete because of an unexpected error.</summary>
	DeleteFailed,

	/// <summary>The delete operation was canceled.</summary>
	Canceled
}

/// <summary>
/// Contains the outcome of deleting a file or directory.
/// </summary>
/// <remarks>
/// A file delete validates its expected stamp before deleting. Directory deletion is recursive and
/// does not use a stamp because the directory operation has no expected-stamp parameter.
/// </remarks>
/// <param name="Outcome">The delete outcome.</param>
/// <param name="ObservedOnDiskStamp">The stamp observed when a conflict or failure was detected, when available.</param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceFileDeleteResult(
	WorkspaceFileDeleteOutcome Outcome,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Provides asynchronous file-system operations required by the document authority.
/// </summary>
/// <remarks>
/// <para>
/// Implementations return operation results for expected environmental failures and may throw for
/// failures they cannot translate. Paths supplied by the store are normalized document ids: the file
/// read, stamp, write, and delete paths, the move source and destination, and the directory move and
/// delete paths.
/// </para>
/// <list type="bullet">
/// <item>Implementations are not required to perform the work asynchronously; synchronous I/O wrapped
/// in a completed task is acceptable, and callers must not assume an operation completes off the
/// calling thread.</item>
/// <item>Implementations should await their own asynchronous work with <c>ConfigureAwait(false)</c>: a
/// host can block its calling thread while a store operation runs (for example a synchronous open),
/// and a continuation that captures that context would deadlock.</item>
/// <item>Deletion is permanent unless the implementation chooses otherwise; a host that needs
/// recoverable deletion (a trash folder or the shell recycle bin) supplies a
/// <see cref="WorkspaceFileSystemDecorator"/> instead of relying on the default
/// <see cref="LocalWorkspaceFileSystem"/>.</item>
/// <item>A cancellation token is observed before any work starts and again before a mutation: members
/// whose result vocabulary carries a canceled outcome report it instead of performing the operation,
/// and members whose result cannot carry one throw <see cref="OperationCanceledException"/>.</item>
/// </list>
/// </remarks>
public interface IWorkspaceFileSystem
{
	/// <summary>Reads a file and captures its content stamp.</summary>
	/// <remarks>
	/// An implementation returns <see cref="WorkspaceFileReadResult.FromContent"/> for content it already
	/// decoded or <see cref="WorkspaceFileReadResult.FromBytes"/> for bytes the document store decodes.
	/// Missing files return <see cref="WorkspaceFileReadResult.Missing"/>. A path that exists as a
	/// directory must return <see cref="WorkspaceFileReadResult.Directory"/>; callers treat any other
	/// result shape as a file.
	/// </remarks>
	/// <param name="path">The file path to read.</param>
	/// <param name="cancellationToken">A token that can cancel the read.</param>
	/// <returns>The read content or raw bytes together with the captured stamp.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	/// <exception cref="OperationCanceledException">The read was canceled.</exception>
	Task<WorkspaceFileReadResult> ReadAsync(
		string path,
		CancellationToken cancellationToken = default);

	/// <summary>Captures the current stamp for a file-system path.</summary>
	/// <remarks>
	/// Capturing a stamp for an existing file reads and hashes its content, so the operation cost is
	/// proportional to the file size. A file that disappears or becomes inaccessible between the
	/// existence probe and the read surfaces the underlying I/O exception. The captured length and
	/// hash describe one byte sequence; a writer that changes the file mid-capture can produce a stamp
	/// that matches neither the old nor the new state, which the next expected-stamp check reports as
	/// a conflict instead of accepting a torn read as current.
	/// </remarks>
	/// <param name="path">The file path to inspect.</param>
	/// <param name="cancellationToken">A token that can cancel the operation.</param>
	/// <returns>
	/// The current stamp, <see cref="FileStamp.Missing"/> when the file does not exist, or
	/// <see cref="FileStamp.Directory"/> when the path exists as a directory instead of a file.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	/// <exception cref="OperationCanceledException">The capture was canceled.</exception>
	Task<FileStamp> CaptureStampAsync(
		string path,
		CancellationToken cancellationToken = default);

	/// <summary>Conditionally writes content to a destination file after validating its expected stamp.</summary>
	/// <remarks>
	/// <para>
	/// The implementation owns the whole atomic write: it stages the bytes in a temporary file beside
	/// the destination, re-validates the destination against <paramref name="expectedStamp"/>, publishes
	/// the staged file over an existing destination or moves it onto a missing one, and removes the
	/// temporary file on every path. An implementation may validate <paramref name="expectedStamp"/>
	/// against a cheap metadata probe before staging, so a destination that already disagrees is
	/// rejected without writing the temporary file; the pre-publish re-validation still guards the
	/// narrower race window. The caller supplies content, never a temporary path, so an
	/// implementation is free to stage the bytes however its platform requires and no temporary
	/// artifact escapes this member.
	/// </para>
	/// <para>
	/// The staging file lives in the destination directory while the write is in progress, so a
	/// file-system watcher on that directory observes it. The default <see cref="LocalWorkspaceFileSystem"/>
	/// names it <c>.{guid}.tmp</c> (a dot-prefixed, GUID-named, <c>.tmp</c> file); a host watcher must
	/// ignore that staging pattern, so an in-progress write is not reported as a foreign change. An
	/// implementation that stages under a different name should document the pattern it uses.
	/// </para>
	/// <para>
	/// A canceled token is observed before the destination is touched, so a
	/// <see cref="WorkspaceFileReplacementOutcome.Canceled"/> result means the destination was not
	/// replaced. When the expected stamp represents a missing file and a directory occupies the
	/// destination path, the write reports
	/// <see cref="WorkspaceFileReplacementOutcome.DestinationExists"/> instead of attempting to publish
	/// over the directory.
	/// </para>
	/// </remarks>
	/// <param name="destinationPath">The file path to replace or create.</param>
	/// <param name="content">The bytes to write.</param>
	/// <param name="expectedStamp">The destination stamp that must still match.</param>
	/// <param name="cancellationToken">A token that can cancel the operation.</param>
	/// <returns>The replacement outcome, including the observed destination stamp when it could be captured.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="destinationPath"/> is <see langword="null"/>.</exception>
	Task<WorkspaceFileReplacementResult> WriteFileAsync(
		string destinationPath,
		ReadOnlyMemory<byte> content,
		FileStamp expectedStamp,
		CancellationToken cancellationToken = default);

	/// <summary>Moves a file after validating its expected source stamp.</summary>
	/// <remarks>
	/// A move that fails after its destination may have been touched reports
	/// <see cref="WorkspaceFileMoveOutcome.MoveStateUnknown"/> when its final state cannot be
	/// established, for example a case-only rename whose rollback move also failed. A successful move
	/// should report the resulting destination stamp in <see cref="WorkspaceFileMoveResult.ObservedOnDiskStamp"/>.
	/// </remarks>
	/// <param name="sourcePath">The file path to move.</param>
	/// <param name="destinationPath">The destination path.</param>
	/// <param name="expectedSourceStamp">The source stamp that must still match.</param>
	/// <param name="cancellationToken">A token that can cancel the operation.</param>
	/// <returns>The move outcome, including the source stamp observed when a conflict was detected or the resulting destination stamp after a successful move, when available.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="sourcePath"/> or <paramref name="destinationPath"/> is <see langword="null"/>.
	/// </exception>
	Task<WorkspaceFileMoveResult> MoveAsync(
		string sourcePath,
		string destinationPath,
		FileStamp expectedSourceStamp,
		CancellationToken cancellationToken = default);

	/// <summary>Moves a directory without a source-stamp precondition.</summary>
	/// <param name="sourcePath">The directory path to move.</param>
	/// <param name="destinationPath">The destination directory path.</param>
	/// <param name="cancellationToken">A token that can cancel the operation.</param>
	/// <returns>The move outcome, including the unknown state of a failed case-only rename that could not be rolled back.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="sourcePath"/> or <paramref name="destinationPath"/> is <see langword="null"/>.
	/// </exception>
	Task<WorkspaceFileMoveResult> MoveDirectoryAsync(
		string sourcePath,
		string destinationPath,
		CancellationToken cancellationToken = default);

	/// <summary>Deletes a file after validating its expected stamp.</summary>
	/// <remarks>
	/// The default <see cref="LocalWorkspaceFileSystem"/> deletes permanently; implementations that
	/// choose a recoverable policy such as a trash folder or shell recycle bin document it on their
	/// own type. The document store's delete member follows this behavior.
	/// </remarks>
	/// <param name="path">The file path to delete.</param>
	/// <param name="expectedStamp">The file stamp that must still match.</param>
	/// <param name="cancellationToken">A token that can cancel the operation.</param>
	/// <returns>The deletion outcome; a path that is absent after a matching stamp reports <see cref="WorkspaceFileDeleteOutcome.Deleted"/>, and a path that exists as a directory reports <see cref="WorkspaceFileDeleteOutcome.DeleteFailed"/> with <see cref="WorkspaceOperationFailureCodes.IsDirectory"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	Task<WorkspaceFileDeleteResult> DeleteAsync(
		string path,
		FileStamp expectedStamp,
		CancellationToken cancellationToken = default);

	/// <summary>Deletes a directory recursively.</summary>
	/// <remarks>The default <see cref="LocalWorkspaceFileSystem"/> deletes permanently; the document store's directory-delete member follows this behavior.</remarks>
	/// <param name="path">The directory path to delete.</param>
	/// <param name="cancellationToken">A token that can cancel the operation.</param>
	/// <returns>The deletion outcome; a path that is absent reports <see cref="WorkspaceFileDeleteOutcome.Deleted"/>, and a path that exists as a file reports <see cref="WorkspaceFileDeleteOutcome.DeleteFailed"/> with <see cref="WorkspaceOperationFailureCodes.NotDirectory"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(
		string path,
		CancellationToken cancellationToken = default);
}
