namespace Nickelony.LanguageServer.Provider;

/// <summary>Refreshes one kind of server-published payload in reaction to what happened to a tracked document.</summary>
/// <remarks>
/// The composition drives every refresher through this contract in the order it chooses for each signal, so the refreshers know
/// nothing of each other.
/// </remarks>
internal interface IServerPayloadRefresher
{
	/// <summary>Starts a refresh of the payload for a document the server now holds in its current state.</summary>
	/// <param name="document">The synchronized tracked document snapshot.</param>
	/// <remarks>
	/// The refresh runs detached from the document-sync pipeline: a stalled round trip must not delay the next change notification or the replay
	/// of the remaining documents after a restart. It starts through <see cref="DocumentOperationScheduler.RunDetachedFromActiveContext"/>
	/// so it does not inherit the enclosing scheduler slot's re-entrancy scope: a refresh that fails over to the connection-start path legitimately
	/// queues per-document operations, which the slot-scoped guard would otherwise reject and count as a startup failure. The per-document request
	/// coordinator that the refresher runs its requests through arbitrates per-document supersession.
	/// </remarks>
	void Trigger(DocumentSnapshot document);

	/// <summary>Refreshes the payload of every open document after the server asked the client to.</summary>
	/// <param name="sender">The client that raised the event.</param>
	/// <param name="e">The event arguments.</param>
	void HandleRefreshRequested(object? sender, EventArgs e);

	/// <summary>Raises the update event with the payload cached for a document that is renamed to a new path.</summary>
	/// <param name="filePath">The normalized path the document is renamed to.</param>
	void AnnounceRenamed(string filePath);

	/// <summary>Cancels the in-flight work for one document path.</summary>
	/// <param name="filePath">The normalized document path.</param>
	void Cancel(string filePath);

	/// <summary>Cancels every in-flight work item.</summary>
	void CancelAll();
}
