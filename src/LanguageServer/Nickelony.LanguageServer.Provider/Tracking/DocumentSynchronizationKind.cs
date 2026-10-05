namespace Nickelony.LanguageServer.Provider;

/// <summary>Identifies the document-synchronization action that should be sent for a tracked file.</summary>
/// <remarks>
/// Each value selects the <c>textDocument/didOpen</c>, <c>didChange</c>, or <c>didClose</c> notification a synchronization emits.
/// </remarks>
internal enum DocumentSynchronizationKind
{
	/// <summary>The document must be opened on the server.</summary>
	Open,

	/// <summary>The document content changed and should be updated on the server.</summary>
	Change,

	/// <summary>The document's server copy must be closed.</summary>
	Close,
}
