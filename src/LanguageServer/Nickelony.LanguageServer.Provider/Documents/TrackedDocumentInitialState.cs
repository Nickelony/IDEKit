namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Describes the initial state for a tracked document created by a
/// <see cref="TrackedDocumentStore"/>.
/// </summary>
/// <param name="FilePath">The normalized local file path of the document.</param>
/// <param name="Uri">The normalized file URI of the document.</param>
/// <param name="Content">The initial document content.</param>
/// <param name="Version">The initial tracked version.</param>
/// <param name="IsOpen">Whether the document is open on the language server.</param>
/// <param name="OpenReferenceCount">The initial number of open-document references.</param>
/// <param name="RequestReferenceCount">The initial number of request-driven references.</param>
/// <param name="LastAccessStamp">The initial access stamp used for LRU-style trimming.</param>
public readonly record struct TrackedDocumentInitialState(
	string FilePath,
	string Uri,
	string Content,
	int Version,
	bool IsOpen,
	int OpenReferenceCount,
	int RequestReferenceCount,
	long LastAccessStamp);
