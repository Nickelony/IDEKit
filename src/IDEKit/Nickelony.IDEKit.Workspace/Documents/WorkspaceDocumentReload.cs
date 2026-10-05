using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Requests that a workspace document be reloaded from disk.</summary>
/// <param name="Identity">The document instance, id, and version expected by the caller.</param>
/// <param name="Encoding">
/// The encoding to decode the file's bytes with, or <see langword="null"/> to decode them with the candidates that the document currently has.
/// </param>
/// <remarks>
/// <para>
/// An <paramref name="Encoding"/> re-reads a clean document whose file did not change, so a host can reopen a file with another encoding.
/// The reload reports <see cref="WorkspaceDocumentReloadOutcome.Unchanged"/> only when the decoded text and format match the document's,
/// and otherwise the document records the encoding in its format. The file system must return the file's bytes: a read that returns decoded content
/// fails the reload with <see cref="WorkspaceDocumentReloadOutcome.ReadFailed"/> and the <c>EncodingNotApplicable</c> failure code, and an encoding
/// that the process cannot resolve fails it with <c>EncodingUnavailable</c>.
/// </para>
/// <para>
/// A UTF-8 or UTF-16 byte-order mark still decides the encoding, so a file with one decodes as it names whatever <paramref name="Encoding"/> says,
/// and a reopen never overrides a mark: a Windows-1252 file that starts with the bytes of a UTF-8 mark cannot be reopened as Windows-1252.
/// When <paramref name="Encoding"/> decoded the bytes, which the recorded format shows by naming it without a mark,
/// it becomes the document's only candidate, so a later reload without one decodes with it first and fails with the <c>InvalidEncoding</c>
/// failure code when it cannot decode the bytes. Another reload with an encoding replaces it, and a reload that a mark decided leaves the candidates
/// as they were.
/// </para>
/// <para>
/// A document with unsaved changes is never re-read, because adopting the disk would discard the changes.
/// Its reload with an <paramref name="Encoding"/> reports <see cref="WorkspaceDocumentReloadOutcome.UnsavedChanges"/>, and the host saves
/// or discards first.
/// </para>
/// </remarks>
public sealed record WorkspaceDocumentReloadRequest(WorkspaceDocumentRequestIdentity Identity, TextEncoding? Encoding = null);

/// <summary>Describes the outcome of reloading a workspace document.</summary>
/// <remarks>
/// The set can grow, so a host that switches over it keeps a default branch.
/// </remarks>
public enum WorkspaceDocumentReloadOutcome
{
	/// <summary>The logical content and persisted baseline were replaced with the current disk content.</summary>
	Reloaded,

	Unchanged,

	StaleDocument,

	/// <summary>The tracked document instance does not match the request's document key.</summary>
	StaleDocumentInstance,

	DocumentNotFound,

	/// <summary>Another operation is in progress, including a delete in flight.</summary>
	OperationInProgress,

	/// <summary>The logical document is dirty and the disk changed since the store last observed it.</summary>
	ExternalFileConflict,

	/// <summary>The file could not be read or decoded; <see cref="WorkspaceDocumentReloadResult.Failure"/> says why.</summary>
	ReadFailed,

	Canceled,

	/// <summary>The logical document has unsaved changes and the request asked for an encoding, so the file was not re-read.</summary>
	UnsavedChanges,
}

/// <summary>Contains the outcome of reloading a workspace document.</summary>
/// <param name="Outcome">The reload outcome.</param>
/// <param name="RequestedIdentity">The document instance, id, and version supplied with the request.</param>
/// <param name="Snapshot">The current tracked state; <see langword="null"/> only when the document was not found.</param>
/// <param name="ObservedOnDiskStamp">The stamp obtained during the operation, when available.</param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceDocumentReloadResult(
	WorkspaceDocumentReloadOutcome Outcome,
	WorkspaceDocumentRequestIdentity RequestedIdentity,
	WorkspaceDocumentSnapshot? Snapshot,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null
);
