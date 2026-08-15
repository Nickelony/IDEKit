namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers the provider state machine directly: startup fencing per transport generation, failure
/// counters, one-time failure reporting, and the terminal disposed state.
/// </summary>
[TestClass]
public sealed class StartupStateTests
{
	private static StartupState CreateState(FakeLanguageServerClient client, Func<bool>? isDisposedAccessor = null)
		=> new(
			client,
			initialState: LanguageServerProviderState.Unavailable,
			readyState: LanguageServerProviderState.Ready,
			disposedState: LanguageServerProviderState.Disposed,
			isDisposedAccessor ?? (static () => false));

	[TestMethod]
	public async Task TryCompleteSuccessfulStart_OnTheCurrentReadyGeneration_TransitionsToReady()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		await client.StartAsync(CancellationToken.None).ConfigureAwait(false);
		StartupState state = CreateState(client);

		Assert.IsTrue(state.TryCompleteSuccessfulStart(client.TransportGeneration, out LanguageServerProviderState previousState));

		Assert.AreEqual(LanguageServerProviderState.Unavailable, previousState);
		Assert.AreEqual(LanguageServerProviderState.Ready, state.State);
		Assert.IsTrue(state.StartupSucceeded);
	}

	[TestMethod]
	public async Task TryCompleteSuccessfulStart_WhenClientIsNotReady_FailsWithoutTransitioning()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		await client.StartAsync(CancellationToken.None).ConfigureAwait(false);

		// Isolate the not-ready condition: the generation is valid and current, only readiness fails.
		client.IsReady = false;

		StartupState state = CreateState(client);

		Assert.IsFalse(state.TryCompleteSuccessfulStart(client.TransportGeneration, out _));
		Assert.IsFalse(state.StartupSucceeded);
		Assert.AreEqual(LanguageServerProviderState.Unavailable, state.State);
	}

	[TestMethod]
	public async Task TryCompleteSuccessfulStart_WhenGenerationWasReportedUnavailable_Fails()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		await client.StartAsync(CancellationToken.None).ConfigureAwait(false);
		StartupState state = CreateState(client);

		Assert.IsFalse(state.OnClientTransportUnavailable(client.TransportGeneration));
		Assert.IsFalse(state.TryCompleteSuccessfulStart(client.TransportGeneration, out _));
		Assert.IsFalse(state.StartupSucceeded);
	}

	[TestMethod]
	public async Task OnClientTransportUnavailable_InvalidatesOnlyTheStartupOfThatGeneration()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		await client.StartAsync(CancellationToken.None).ConfigureAwait(false);
		StartupState state = CreateState(client);

		Assert.IsTrue(state.TryCompleteSuccessfulStart(client.TransportGeneration, out _));
		Assert.IsTrue(state.OnClientTransportUnavailable(client.TransportGeneration));
		Assert.IsFalse(state.StartupSucceeded);

		// A start on the next generation succeeds, and a late notification for the previous
		// generation no longer invalidates it.
		client.IsReady = false;
		await client.StartAsync(CancellationToken.None).ConfigureAwait(false);
		Assert.IsTrue(state.TryCompleteSuccessfulStart(client.TransportGeneration, out _));
		Assert.IsFalse(state.OnClientTransportUnavailable(client.TransportGeneration - 1));
		Assert.IsTrue(state.StartupSucceeded);
	}

	[TestMethod]
	public async Task FailureReporting_IsOncePerPersistence_AndResetsAfterASuccessfulStart()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		StartupState state = CreateState(client);

		Assert.AreEqual(1, state.RegisterStartupFailure());
		Assert.AreEqual(2, state.RegisterStartupFailure());
		Assert.AreEqual(2, state.ConsecutiveStartupFailures);

		Assert.IsTrue(state.TryMarkStartupFailureReported(isPersistentFailure: false));
		Assert.IsFalse(state.TryMarkStartupFailureReported(isPersistentFailure: false));
		Assert.IsTrue(state.TryMarkStartupFailureReported(isPersistentFailure: true));
		Assert.IsFalse(state.TryMarkStartupFailureReported(isPersistentFailure: true));

		// A successful start resets the counters and the reporting flags.
		await client.StartAsync(CancellationToken.None).ConfigureAwait(false);
		Assert.IsTrue(state.TryCompleteSuccessfulStart(client.TransportGeneration, out _));
		Assert.AreEqual(0, state.ConsecutiveStartupFailures);
		Assert.IsTrue(state.TryMarkStartupFailureReported(isPersistentFailure: false));
	}

	[TestMethod]
	public async Task TryCompleteSuccessfulStart_WithGenerationZero_IsRejected()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		StartupState state = CreateState(client);

		await client.StartAsync(CancellationToken.None).ConfigureAwait(false);

		// Generation zero means no session was started yet; the fencing check must reject the completion.
		Assert.IsFalse(state.TryCompleteSuccessfulStart(0, out _));
		Assert.IsFalse(state.StartupSucceeded);
	}

	[TestMethod]
	public void TrySetState_HonorsTheTerminalDisposedState()
	{
		using var client = new FakeLanguageServerClient();
		bool isDisposed = false;
		StartupState state = CreateState(client, () => isDisposed);

		Assert.IsTrue(state.TrySetState(LanguageServerProviderState.Ready, notifyCapabilitiesChanged: true));

		isDisposed = true;

		// Once disposal started, only the disposed transition is still accepted.
		Assert.IsFalse(state.TrySetState(LanguageServerProviderState.Starting, notifyCapabilitiesChanged: true));
		Assert.AreEqual(LanguageServerProviderState.Ready, state.State);

		state.TrySetState(LanguageServerProviderState.Disposed, notifyCapabilitiesChanged: false);
		Assert.AreEqual(LanguageServerProviderState.Disposed, state.State);

		// The disposed state is terminal.
		Assert.IsFalse(state.TrySetState(LanguageServerProviderState.Ready, notifyCapabilitiesChanged: true));
		Assert.AreEqual(LanguageServerProviderState.Disposed, state.State);
	}
}
