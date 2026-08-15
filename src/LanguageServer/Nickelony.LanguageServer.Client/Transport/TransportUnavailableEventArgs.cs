namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Provides the data for the <see cref="ILanguageServerClient.TransportUnavailable"/> event.
/// </summary>
public sealed class TransportUnavailableEventArgs : EventArgs
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TransportUnavailableEventArgs"/> class.
	/// </summary>
	/// <param name="generation">The transport generation that was active immediately before the loss.</param>
	public TransportUnavailableEventArgs(long generation)
	{
		Generation = generation;
	}

	/// <summary>
	/// Gets the transport generation that was active immediately before the loss.
	/// The generation is already invalidated when this event is raised, so handlers must not call
	/// <see cref="ILanguageServerClient.TryMarkTransportUnhealthy"/> for it; fence stale work against the value instead.
	/// </summary>
	public long Generation { get; }
}
