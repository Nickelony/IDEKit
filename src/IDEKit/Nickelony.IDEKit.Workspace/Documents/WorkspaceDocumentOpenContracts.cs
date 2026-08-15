namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Describes the outcome of opening or loading a workspace document.
/// </summary>
/// <remarks><see cref="AlreadyOpen"/> means the existing tracked snapshot was returned without another load.</remarks>
public enum WorkspaceDocumentOpenOutcome
{
	/// <summary>The document was loaded from disk, or created as a new empty document when the path does not exist.</summary>
	Opened,

	/// <summary>The document was already open.</summary>
	AlreadyOpen,

	/// <summary>The path is invalid.</summary>
	InvalidPath,

	/// <summary>
	/// The path does not exist and the open options did not allow creating a new document
	/// (<see cref="WorkspaceDocumentOpenOptions.CreateIfMissing"/> is <see langword="false"/>).
	/// </summary>
	NotFound,

	/// <summary>
	/// The path exists as a directory, not a file. A directory cannot be tracked as a document: every
	/// later write or delete through the store would fail for it, so the path is rejected at open
	/// instead of being tracked as a new empty document.
	/// </summary>
	IsDirectory,

	/// <summary>The document could not be loaded.</summary>
	LoadFailed,

	/// <summary>
	/// The operation was canceled, either by the caller's token or by the disposal of the store while
	/// the call was in flight.
	/// </summary>
	Canceled
}

/// <summary>
/// Specifies format defaults used when opening or creating a workspace document.
/// </summary>
/// <remarks>
/// <see cref="NoBomEncoding"/> is used only when an existing file has no recognized byte-order mark.
/// <see cref="NewFileFormat"/> is used when the requested path does not exist and
/// <see cref="CreateIfMissing"/> allows creating a document for it. Both format values are validated
/// when the store opens the path, before the path itself is validated: an undefined encoding, or a
/// NewFileFormat that combines Windows-1252 with a byte-order mark, is an argument error instead of
/// failing later at the first commit. When the path is already tracked, the options are not
/// re-applied because the open reports <see cref="WorkspaceDocumentOpenOutcome.AlreadyOpen"/> with the
/// tracked state.
/// </remarks>
/// <param name="NoBomEncoding">The encoding to use when an existing file has no byte-order mark.</param>
/// <param name="NewFileFormat">The format to use when the requested path does not exist.</param>
/// <param name="CreateIfMissing">
/// <see langword="true"/> (the default) to open a missing path as a new empty document;
/// <see langword="false"/> to report <see cref="WorkspaceDocumentOpenOutcome.NotFound"/> instead, which
/// hosts that require an existing file (build tools, servers) use to surface a missing file as an
/// error rather than as new content.
/// </param>
public readonly record struct WorkspaceDocumentOpenOptions(
	TextEncodingKind NoBomEncoding,
	TextFileFormat NewFileFormat,
	bool CreateIfMissing = true)
{
	/// <summary>
	/// Gets the open options a host uses when it has no more specific requirement: UTF-8 for a file
	/// without a byte-order mark, a new-file format equal to <see langword="default"/>(<see cref="TextFileFormat"/>),
	/// and creating a missing path.
	/// </summary>
	/// <remarks>
	/// <see cref="Default"/> is not the same as <see langword="default"/>(<see cref="WorkspaceDocumentOpenOptions"/>)
	/// or <c>new WorkspaceDocumentOpenOptions()</c>: a <see langword="default"/> instance sets
	/// <see cref="CreateIfMissing"/> to <see langword="false"/>, because a value type's zero value cannot
	/// run the constructor's default. <see cref="Default"/> carries the constructor's documented defaults
	/// instead.
	/// </remarks>
	public static WorkspaceDocumentOpenOptions Default { get; } = new(TextEncodingKind.Utf8, default);
}

/// <summary>
/// Contains the outcome of opening or loading a workspace document.
/// </summary>
/// <remarks>
/// <see cref="WorkspaceDocumentOpenOutcome.Opened"/> and
/// <see cref="WorkspaceDocumentOpenOutcome.AlreadyOpen"/> results carry a snapshot; every failure
/// outcome returns none, and a load failure additionally carries a typed
/// <see cref="WorkspaceOperationFailure"/>.
/// </remarks>
/// <param name="Outcome">The open outcome.</param>
/// <param name="Snapshot">The loaded or already-open snapshot; otherwise, <see langword="null"/>.</param>
/// <param name="Failure">Explains a load failure, when one occurred.</param>
public sealed record WorkspaceDocumentOpenResult(
	WorkspaceDocumentOpenOutcome Outcome,
	WorkspaceDocumentSnapshot? Snapshot,
	WorkspaceOperationFailure? Failure = null);
