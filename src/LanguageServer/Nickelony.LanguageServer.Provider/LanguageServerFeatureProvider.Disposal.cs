namespace Nickelony.LanguageServer.Provider;

public sealed partial class LanguageServerFeatureProvider
{
	/// <inheritdoc/>
	/// <remarks>
	/// See the class remarks for the disposal contract. The provider tears down what it owns, and unsubscribes from the owned client,
	/// before it disposes that client. A later caller waits for the teardown the first caller started instead of returning while it continues.
	/// </remarks>
	public void Dispose()
	{
		if (!Composition.Disposal.TryBegin())
		{
			// The first caller owns teardown; wait for it rather than returning a completed disposal while the
			// client is still being torn down.
			Composition.Disposal.Completed.GetAwaiter().GetResult();
			return;
		}

		GC.SuppressFinalize(this);

		try
		{
			RunDisposeCoreContained();
			_client?.Dispose();
		}
		finally
		{
			Composition.Disposal.Complete();
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Shares one teardown with <see cref="Dispose"/>: whichever overload runs first performs it, and a later caller waits for that teardown
	/// to finish instead of returning while it continues. This overload awaits the owned client's asynchronous teardown instead of blocking
	/// the calling thread on it, so a host with a synchronization context can dispose the provider without deadlocking its thread;
	/// a later synchronous caller still blocks its own thread until the shared teardown completes.
	/// </remarks>
	public async ValueTask DisposeAsync()
	{
		if (!Composition.Disposal.TryBegin())
		{
			await Composition.Disposal.Completed.ConfigureAwait(false);
			return;
		}

		GC.SuppressFinalize(this);

		try
		{
			RunDisposeCoreContained();

			if (_client is not null)
				await _client.DisposeAsync().ConfigureAwait(false);
		}
		finally
		{
			Composition.Disposal.Complete();
		}
	}

	/// <summary>Runs the provider-owned teardown steps, containing a failure so it cannot skip the client disposal that follows.</summary>
	private void RunDisposeCoreContained()
	{
		try
		{
			DisposeCore();
		}
		catch (Exception exception)
		{
			Logger.LogWarning(
				exception,
				"{DisplayName} disposal reported a failure while tearing down provider-owned state; continuing with client disposal.",
				ProviderDisplayName
			);
		}
	}

	/// <summary>Performs the provider-owned teardown steps shared by <see cref="Dispose"/> and <see cref="DisposeAsync"/>.</summary>
	private void DisposeCore()
	{
		Composition.Disposal.Signal();
		Composition.Events.Close();
		Composition.Lifecycle.SetProviderState(LanguageServerFeatureProviderState.Disposed);

		if (_client is not null)
		{
			_client.DiagnosticsPublished -= Composition.DiagnosticsSource.HandlePublished;
			_client.SemanticTokensRefreshRequested -= Composition.SemanticTokensSource.HandleRefreshRequested;
			_client.DiagnosticRefreshRequested -= Composition.DiagnosticsSource.HandleRefreshRequested;
			_client.ConnectionUnavailable -= HandleConnectionUnavailable;
		}

		Composition.WorkspaceChanges.Dispose();

		Composition.Scheduler.CancelAllLatestUpdates();

		Composition.SemanticTokensSource.CancelAll();
		Composition.DiagnosticsSource.CancelAll();
	}
}
