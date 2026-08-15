namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Identifies the document-synchronization action that should be sent for a tracked file.
/// </summary>
/// <remarks>
/// This is the outbound-notification axis: it selects the <c>textDocument/didOpen</c>, <c>didChange</c>, or
/// <c>didClose</c> notification a synchronization emits. It is independent of
/// <see cref="DocumentReferenceAcquisition"/>, which selects the references a
/// <see cref="TrackedDocumentStore.Synchronize"/> call acquires, and of <see cref="DocumentCloseOutcome"/>, which
/// reports what an explicit close call did to the tracked record.
/// </remarks>
public enum DocumentSynchronizationKind
{
	/// <summary>
	/// The document must be opened on the server.
	/// </summary>
	Open,

	/// <summary>
	/// The document content changed and should be updated on the server.
	/// </summary>
	Change,

	/// <summary>
	/// The document's server copy must be closed.
	/// </summary>
	Close
}
