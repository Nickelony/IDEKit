namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Owns generic tracked-document synchronization and lifecycle mechanics for a language-server integration.
/// Derived stores keep language-specific caches and policies outside this core abstraction.
/// </summary>
/// <remarks>
/// Every protected member runs while the store's internal lock is held and must not block on work that needs another
/// thread to take that lock, and the rename hook runs after the renamed entry became visible at its new path so a
/// throwing override cannot leave the record unreachable. The pending-close machine stays in the provider; the
/// rationale and the split live in <c>docs/LanguageServerInternals.md</c>.
/// </remarks>
public abstract partial class TrackedDocumentStore
{
	// Store state is keyed by normalized file path. Access stamps provide an LRU-style ordering for
	// idle-document trimming without pushing that policy into derived language-specific stores.
	private readonly object _syncRoot = new();

	private readonly Dictionary<string, TrackedDocumentState> _documents = new(LanguageServerPaths.LocalPathComparer);
	private long _nextAccessStamp;

	/// <summary>
	/// Creates a tracked document state for a newly seen document.
	/// </summary>
	/// <param name="initialState">The initial state for the new tracked document.</param>
	/// <returns>The newly created tracked document state.</returns>
	protected abstract TrackedDocumentState CreateTrackedDocumentState(TrackedDocumentInitialState initialState);

	/// <summary>
	/// Reads the current access stamp for a tracked document.
	/// </summary>
	/// <param name="state">The tracked document state to read.</param>
	/// <returns>The currently stored access stamp.</returns>
	protected abstract long GetLastAccessStamp(TrackedDocumentState state);

	/// <summary>
	/// Updates the access stamp for a tracked document.
	/// </summary>
	/// <param name="state">The tracked document state to update.</param>
	/// <param name="lastAccessStamp">The access stamp to store.</param>
	/// <remarks>
	/// Stamps must only advance while the store lock is held: idle documents are sorted by stamp under that lock
	/// during trimming, and out-of-lock advances can violate the sort's comparer contract.
	/// </remarks>
	protected abstract void TouchTrackedDocumentState(TrackedDocumentState state, long lastAccessStamp);

	/// <summary>
	/// Reopens a previously closed tracked document.
	/// </summary>
	/// <param name="state">The tracked document state to reopen.</param>
	/// <param name="content">The content to reopen the document with.</param>
	protected abstract void ReopenTrackedDocumentState(TrackedDocumentState state, string content);

	/// <summary>
	/// Replaces the tracked document content and returns the previous content.
	/// </summary>
	/// <param name="state">The tracked document state to update.</param>
	/// <param name="content">The new document content.</param>
	/// <returns>The content that was tracked before the replacement.</returns>
	protected abstract string ReplaceTrackedDocumentContent(TrackedDocumentState state, string content);

	/// <summary>
	/// Renames a tracked document to a new path and URI.
	/// </summary>
	/// <param name="state">The tracked document state to rename.</param>
	/// <param name="filePath">The new normalized local file path.</param>
	/// <param name="uri">The new normalized file URI.</param>
	protected abstract void RenameTrackedDocumentState(TrackedDocumentState state, string filePath, string uri);

	/// <summary>
	/// Marks a tracked document as locally closed after a transport loss or before a restart.
	/// </summary>
	/// <param name="state">The tracked document state to mark closed.</param>
	protected abstract void MarkTrackedDocumentClosed(TrackedDocumentState state);

	/// <summary>
	/// Allows derived stores to react after a tracked document rename has completed.
	/// The default implementation does nothing.
	/// </summary>
	/// <param name="state">The renamed tracked document state.</param>
	/// <param name="contentChanged">Whether the rename also replaced the tracked document content.</param>
	/// <remarks>
	/// The hook runs while the store lock is held and after the renamed entry became visible at its new path, so
	/// implementations must not block on work that requires another thread to take the store lock.
	/// </remarks>
	protected virtual void OnTrackedDocumentRenamed(TrackedDocumentState state, bool contentChanged)
	{ }

	/// <summary>
	/// Increments the shared access counter used for LRU-style trimming ordering.
	/// </summary>
	/// <returns>The next monotonic access stamp.</returns>
	private long GetNextAccessStamp() => ++_nextAccessStamp;
}
