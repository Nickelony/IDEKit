using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Captures the authoritative logical and persisted state of a workspace document.</summary>
/// <param name="DocumentKey">The stable identity of the document instance.</param>
/// <param name="DocumentId">The normalized path that identifies the document.</param>
/// <param name="DisplayPath">The caller-supplied path, retained for display.</param>
/// <param name="Version">The logical document version.</param>
/// <param name="PersistedVersion">The logical version whose content and format the persisted baseline represents.</param>
/// <param name="IsDirty">Whether the current logical content and format differ from the persisted baseline.</param>
/// <param name="Text">The logical document content.</param>
/// <param name="FileFormat">The format associated with the current logical content.</param>
/// <param name="OnDiskStamp">The last observed on-disk stamp of the document's file.</param>
/// <remarks>
/// <see cref="PersistedVersion"/> is the baseline the persisted file represents, not a write counter and not tied to the path;
/// it can trail <see cref="Version"/>. <see cref="IsDirty"/> is the authority for unsaved changes, computed from the current content and format
/// rather than the versions, so an edit that restores the baseline content leaves the document clean while the versions differ.
/// </remarks>
public sealed record WorkspaceDocumentSnapshot(
	WorkspaceDocumentKey DocumentKey,
	string DocumentId,
	string DisplayPath,
	long Version,
	long PersistedVersion,
	bool IsDirty,
	ITextSnapshot Text,
	TextFileFormat FileFormat,
	FileStamp OnDiskStamp
)
{
	/// <summary>Whether the store's last observed on-disk stamp represents an existing file.</summary>
	public bool ExistsOnDisk => OnDiskStamp.Exists;

	/// <summary>The identity a request for this version of the document carries, so the store rejects it once the document moved on.</summary>
	public WorkspaceDocumentRequestIdentity Identity => new(DocumentKey, DocumentId, Version);

	/// <summary>The complete logical document content represented by <see cref="Text"/>.</summary>
	/// <remarks>
	/// The store creates snapshots backed by a <see cref="StringTextSnapshot"/>, which returns its captured string without copying,
	/// while another <see cref="ITextSnapshot"/> implementation may materialize a substring on every access. Capture the value in a local
	/// when it is used more than once.
	/// </remarks>
	public string Content => Text.GetText(0, Text.TextLength);
}
