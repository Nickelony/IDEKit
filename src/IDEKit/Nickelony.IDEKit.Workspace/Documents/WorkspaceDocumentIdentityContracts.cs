using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Identifies one logical instance of a workspace document.
/// </summary>
/// <remarks>
/// The key remains stable when the document instance is renamed or saved at a new path, so the
/// instance stays identifiable under its new path; a document reopened after the instance was
/// deleted receives a new key. The store generates the key with a new <see cref="Guid"/> for every
/// opened document, so a default value (an empty <see cref="Guid"/>) never identifies a tracked
/// instance.
/// </remarks>
/// <param name="Value">The unique identifier for this instance.</param>
public readonly record struct WorkspaceDocumentKey(Guid Value);

/// <summary>
/// Identifies a specific version of a workspace document in requests and results.
/// </summary>
/// <remarks>
/// A request carries the identity the caller expects; a result echoes the requested identity so a
/// caller can correlate the result with its request even when validation rejected it. The version is
/// the optimistic-concurrency token, so requests that do not mutate the document (for example a
/// reload) still use the same identity shape.
/// </remarks>
/// <param name="DocumentKey">The document instance key expected by the caller.</param>
/// <param name="DocumentId">The normalized document id expected by the caller.</param>
/// <param name="Version">The document version captured by the caller.</param>
public readonly record struct WorkspaceDocumentRequestIdentity(
	WorkspaceDocumentKey DocumentKey,
	string DocumentId,
	long Version);

/// <summary>
/// Captures the authoritative logical and persisted state of a workspace document.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DocumentId"/> is the normalized path used by the store to identify the document;
/// <see cref="DisplayPath"/> is the supplied path retained for display. The logical content is in
/// <see cref="Text"/>; the snapshot captures the content as an immutable string and builds line metadata
/// on first line-oriented access, so creating a snapshot does not build the line index.
/// </para>
/// <para>
/// <see cref="PersistedVersion"/> identifies the logical version whose content and format are the
/// persisted baseline; it is not a write counter and not tied to the document's path. It can trail
/// <see cref="Version"/> in two situations: a logical edit advanced the document after an earlier
/// version was persisted, or a rename or save-as advanced the version for the identity change
/// without changing the persisted baseline.
/// </para>
/// <para>
/// A document can therefore be clean while <see cref="PersistedVersion"/> trails
/// <see cref="Version"/>: <see cref="IsDirty"/> is the authority for unsaved changes, and it is
/// computed by comparing the current content and format with the baseline rather than from the
/// versions, because an edit can restore the baseline content at a newer version.
/// </para>
/// <para>
/// The dirty comparison runs when a mutating member changes the content or format, so reading a
/// snapshot never scans the content.
/// </para>
/// </remarks>
/// <param name="DocumentKey">The stable identity of the document instance.</param>
/// <param name="DocumentId">The normalized path used to identify the document.</param>
/// <param name="DisplayPath">The path supplied by the caller, retained for display.</param>
/// <param name="Version">The logical document version.</param>
/// <param name="PersistedVersion">The logical version whose content and format the persisted baseline represents; it can trail <see cref="Version"/>.</param>
/// <param name="IsDirty">Whether the current logical content and format differ from the persisted baseline.</param>
/// <param name="Text">The logical document content.</param>
/// <param name="FileFormat">The format associated with the current logical content.</param>
/// <param name="OnDiskStamp">The last observed on-disk stamp of the document's file.</param>
public sealed record WorkspaceDocumentSnapshot(
	WorkspaceDocumentKey DocumentKey,
	string DocumentId,
	string DisplayPath,
	long Version,
	long PersistedVersion,
	bool IsDirty,
	ITextSnapshot Text,
	TextFileFormat FileFormat,
	FileStamp OnDiskStamp)
{
	/// <summary>
	/// Gets a value indicating whether the store's last observed on-disk stamp represents an existing file.
	/// </summary>
	/// <value><see langword="true"/> when <see cref="OnDiskStamp"/> represents an existing file.</value>
	public bool ExistsOnDisk => OnDiskStamp.Exists;

	/// <summary>
	/// Gets the complete logical document content represented by <see cref="Text"/>.
	/// </summary>
	/// <remarks>
	/// The content is returned as a string; a store-created snapshot and a
	/// <see cref="StringTextSnapshot"/>-backed snapshot return their captured string without copying,
	/// while other <see cref="ITextSnapshot"/> implementations may materialize a substring on every
	/// access. Capture the value in a local when it is used more than once.
	/// </remarks>
	public string Content => Text is DeferredTextSnapshot deferred ? deferred.Text : Text.GetText(0, Text.TextLength);
}
