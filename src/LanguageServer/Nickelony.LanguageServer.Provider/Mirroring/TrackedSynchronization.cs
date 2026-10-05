namespace Nickelony.LanguageServer.Provider;

/// <summary>Marks the server mirror of a tracked document as stale and announces it to the features.</summary>
internal static class TrackedSynchronization
{
	/// <summary>Marks one document's tracked state as not mirrored to the server, and announces the invalidation.</summary>
	/// <param name="documents">The tracker whose record for the document is marked closed on the server.</param>
	/// <param name="signals">The reactions of the features, which cancel the work issued for the stale state.</param>
	/// <param name="filePath">The document whose tracked server synchronization should be invalidated.</param>
	/// <remarks>
	/// Unlike <see cref="ProviderLifecycleCoordinator.MarkConnectionUnavailable"/> this only touches tracked state, so callers can run
	/// it while the per-document scheduler slot is still held; that keeps the invalidation ordered before any operation queued behind a failed send,
	/// which then observes the invalidated state and reopens the document instead of trusting a server copy that never received the change.
	/// </remarks>
	internal static void Invalidate(DocumentTracker documents, DocumentSignals signals, string filePath)
	{
		// Deliberately no cancellation of the latest update here: this runs while the update that failed still
		// holds the document's scheduler slot, and any still-pending update is the newest one for the path.
		// It waits for the failed update to complete, so it observes the invalidated state and reopens the
		// document; canceling it would drop that recovery.
		documents.InvalidateServerSynchronization(filePath);
		signals.Invalidated(filePath);
	}
}
