using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Holds the logical content and persistence state of an open workspace document.</summary>
/// <remarks>
/// <para>
/// The identity, the content, the version pair, the persisted baseline,
/// the on-disk stamp and the no-BOM encoding candidates change only through the transitions of this type. Each transition requires the store
/// lock and throws <see cref="InvalidOperationException"/> without it, changes all its fields in one call so that no reader under the lock sees
/// a half-applied change, and leaves <see cref="PersistedVersion"/> at most <see cref="Version"/>.
/// </para>
/// <para>
/// <see cref="Version"/> counts the changes of the logical content and identity. <see cref="PersistedVersion"/> is the version of the content
/// that the persisted baseline holds, so a commit that wrote an earlier version leaves it behind <see cref="Version"/>.
/// </para>
/// </remarks>
internal sealed class LogicalDocument
{
	private readonly object _syncRoot;

	internal LogicalDocument(
		object syncRoot,
		WorkspaceDocumentKey documentKey,
		string documentId,
		string displayPath,
		string content,
		TextFileFormat fileFormat,
		FileStamp onDiskStamp,
		IReadOnlyList<TextEncoding> noBomEncodings
	)
	{
		_syncRoot = syncRoot;
		DocumentKey = documentKey;
		DocumentId = documentId;
		DisplayPath = displayPath;
		Content = content;
		PersistedContent = content;
		FileFormat = fileFormat;
		PersistedFileFormat = fileFormat;
		OnDiskStamp = onDiskStamp;
		NoBomEncodings = noBomEncodings;
	}

	internal WorkspaceDocumentKey DocumentKey { get; }

	internal string DocumentId { get; private set; }

	internal string DisplayPath { get; private set; }

	internal string Content { get; private set; }

	internal string PersistedContent { get; private set; }

	internal TextFileFormat FileFormat { get; private set; }

	internal TextFileFormat PersistedFileFormat { get; private set; }

	// The dirty state is recomputed at the end of each transition that touches the content or the format, which are the only mutation points,
	// so snapshot creation and gate checks never scan the content while another thread waits to enter the state lock. The content comparison
	// is skipped when both fields reference the same string instance.
	internal bool IsDirty { get; private set; }

	internal FileStamp OnDiskStamp { get; private set; }

	// The candidates that decode a file without a byte-order mark. A reload that asked for an encoding, and a write that persisted another
	// encoding, replace them under the store lock; the reads outside the lock belong to operations that hold this document's disk gate, as
	// those writers do.
	internal IReadOnlyList<TextEncoding> NoBomEncodings { get; private set; }

	internal SemaphoreSlim DiskOperationGate { get; } = new(1, 1);

	internal long Version { get; private set; }

	internal long PersistedVersion { get; private set; }

	/// <summary>Replaces the logical content and format and moves the version on; the baseline stays.</summary>
	/// <param name="content">The new logical content.</param>
	/// <param name="fileFormat">The new logical format.</param>
	/// <remarks>
	/// Requires the store lock.
	/// </remarks>
	internal void ReplaceContent(string content, TextFileFormat fileFormat)
	{
		RequireStoreLock();
		Content = content;
		FileFormat = fileFormat;
		Version++;
		UpdateDirty();
	}

	/// <summary>Restores the persisted content and format as the logical ones and moves the version on; the persisted version follows it.</summary>
	/// <remarks>
	/// Requires the store lock.
	/// </remarks>
	internal void DiscardChanges()
	{
		RequireStoreLock();
		Content = PersistedContent;
		FileFormat = PersistedFileFormat;
		Version++;
		PersistedVersion = Version;
		UpdateDirty();
	}

	/// <summary>Installs the disk content as both the logical content and the baseline, so the document is clean at the new version.</summary>
	/// <param name="content">The decoded content of the file.</param>
	/// <param name="fileFormat">The format the file decoded with.</param>
	/// <param name="onDiskStamp">The stamp of the file the content was read from.</param>
	/// <remarks>
	/// Requires the store lock.
	/// </remarks>
	internal void AdoptDiskContent(string content, TextFileFormat fileFormat, FileStamp onDiskStamp)
	{
		RequireStoreLock();
		Content = content;
		PersistedContent = content;
		FileFormat = fileFormat;
		PersistedFileFormat = fileFormat;
		Version++;
		PersistedVersion = Version;
		OnDiskStamp = onDiskStamp;
		UpdateDirty();
	}

	/// <summary>Installs the content that a commit or a save-as wrote as the persisted baseline; the logical content and the version stay.</summary>
	/// <param name="content">The written content.</param>
	/// <param name="fileFormat">The written format.</param>
	/// <param name="version">The version of the written content, which can lag <see cref="Version"/> when an edit landed during the write.</param>
	/// <param name="onDiskStamp">The stamp of the file after the write.</param>
	/// <remarks>
	/// Requires the store lock. A recorded format is the one the store reads back with, so a no-BOM encoding other than the one the document last
	/// decoded with becomes the only candidate.
	/// </remarks>
	internal void InstallBaseline(string content, TextFileFormat fileFormat, long version, FileStamp onDiskStamp)
	{
		RequireStoreLock();

		if (!fileFormat.HasBom && fileFormat.Encoding != PersistedFileFormat.Encoding)
			NoBomEncodings = [fileFormat.Encoding];

		PersistedContent = content;
		PersistedFileFormat = fileFormat;
		PersistedVersion = version;
		OnDiskStamp = onDiskStamp;
		UpdateDirty();
	}

	/// <summary>Moves the document to another id, display path and file stamp, and moves the version on.</summary>
	/// <param name="documentId">The new normalized document id.</param>
	/// <param name="displayPath">The new display path.</param>
	/// <param name="onDiskStamp">The stamp of the file at the new path, or the missing stamp when the file did not move.</param>
	/// <remarks>
	/// Requires the store lock, which the caller also holds while it moves the document between the tracker's keys.
	/// </remarks>
	internal void Retarget(string documentId, string displayPath, FileStamp onDiskStamp)
	{
		RequireStoreLock();
		DocumentId = documentId;
		DisplayPath = displayPath;
		OnDiskStamp = onDiskStamp;
		Version++;
	}

	/// <summary>Records the stamp that a read or a failed write observed on disk; the content and the version stay.</summary>
	/// <param name="onDiskStamp">The observed stamp.</param>
	/// <remarks>
	/// Requires the store lock.
	/// </remarks>
	internal void ObserveOnDiskStamp(FileStamp onDiskStamp)
	{
		RequireStoreLock();
		OnDiskStamp = onDiskStamp;
	}

	/// <summary>Makes one encoding the only candidate that decodes a file without a byte-order mark.</summary>
	/// <param name="encoding">The encoding a reload was asked for and decoded the file with.</param>
	/// <remarks>
	/// Requires the store lock.
	/// </remarks>
	internal void RestrictNoBomEncodings(TextEncoding encoding)
	{
		RequireStoreLock();
		NoBomEncodings = [encoding];
	}

	private void RequireStoreLock()
	{
		if (!Monitor.IsEntered(_syncRoot))
			throw new InvalidOperationException("A change of a document requires the store lock.");
	}

	private void UpdateDirty()
	{
		IsDirty =
			(FileFormat != PersistedFileFormat)
			|| (!ReferenceEquals(Content, PersistedContent) && !string.Equals(Content, PersistedContent, StringComparison.Ordinal));
	}
}
