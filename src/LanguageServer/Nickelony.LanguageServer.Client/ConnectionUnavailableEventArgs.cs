namespace Nickelony.LanguageServer.Client;

/// <summary>Provides the data for the <see cref="ILanguageServerClient.ConnectionUnavailable"/> event.</summary>
public sealed class ConnectionUnavailableEventArgs : EventArgs
{
	/// <param name="connectionGeneration">The connection generation that was active immediately before the loss.</param>
	public ConnectionUnavailableEventArgs(long connectionGeneration) => ConnectionGeneration = connectionGeneration;

	/// <summary>The connection generation that was active immediately before the loss.</summary>
	/// <remarks>
	/// The generation is already invalidated when this event is raised, so handlers must not call
	/// <see cref="ILanguageServerClient.TryMarkConnectionUnhealthy"/> for it; fence stale work against the value instead.
	/// </remarks>
	public long ConnectionGeneration { get; }
}
