namespace Nickelony.LanguageServer.Client;

public sealed partial class LanguageServerClient
{
	/// <summary>Guards a call against a disposed client, unless <paramref name="allowDisposed"/> allows disposed access.</summary>
	/// <param name="allowDisposed">Whether disposed access should be allowed.</param>
	/// <exception cref="ObjectDisposedException">The client has been disposed and the call does not allow disposed access.</exception>
	private void ThrowIfDisposed(bool allowDisposed)
	{
		if (!allowDisposed)
			ObjectDisposedException.ThrowIf(Composition.Lifetime.IsDisposed, nameof(LanguageServerClient));
	}

	/// <summary>Stops the language-server process, faults or cancels pending requests, and releases connection resources.</summary>
	/// <remarks>
	/// Blocks the calling thread until teardown finishes, for the first caller and for every later caller that waits for the teardown
	/// the first caller started; this is sync-over-async, so a thread-affine caller can deadlock. Prefer <see cref="DisposeAsync"/> where blocking
	/// the calling thread is undesirable. Do not call this method, or block on <see cref="DisposeAsync"/>,
	/// from a <see cref="ILanguageServerClient.ConnectionUnavailable"/> handler or another client callback; see the interface remarks for the shared
	/// disposal and delivery contracts.
	/// </remarks>
	public void Dispose() => Composition.Lifetime.BeginDisposeAsync(Composition.Teardown.RunAsync).GetAwaiter().GetResult();

	/// <summary>Stops the language-server process, faults or cancels pending requests, and releases connection resources asynchronously.</summary>
	/// <returns>
	/// A task that completes when disposal finishes. Only the first caller performs teardown; a later caller waits for the teardown
	/// the first caller started, so awaiting this task always means disposal completed.
	/// </returns>
	public async ValueTask DisposeAsync() => await Composition.Lifetime.BeginDisposeAsync(Composition.Teardown.RunAsync).ConfigureAwait(false);
}
