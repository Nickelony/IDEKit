namespace Nickelony.LanguageServer.Provider;

/// <summary>Describes the initial state for a tracked document created by a <see cref="DocumentTracker"/>.</summary>
/// <param name="FilePath">The normalized local file path of the document.</param>
/// <param name="Uri">The normalized file URI of the document.</param>
/// <param name="Content">The initial document content.</param>
/// <param name="Version">The initial tracked version.</param>
/// <param name="IsOpen">Whether the document is open on the language server.</param>
/// <param name="OpenLeaseCount">The initial number of open-document leases.</param>
/// <param name="RequestLeaseCount">The initial number of request-driven leases.</param>
/// <param name="LastAccessStamp">The initial access stamp used for LRU-style trimming.</param>
/// <param name="DocumentId">The id the host supplied for the document, or <see langword="null"/> when it equals <paramref name="FilePath"/>.</param>
internal readonly record struct TrackedDocumentDescriptor(
	string FilePath,
	string Uri,
	string Content,
	int Version,
	bool IsOpen,
	int OpenLeaseCount,
	int RequestLeaseCount,
	long LastAccessStamp,
	string? DocumentId = null
);
