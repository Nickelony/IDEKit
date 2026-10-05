namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the provider startup state machine directly.</summary>
[TestClass]
public sealed class StartupStateTests
{
	private static StartupState CreateState(FakeLanguageServerClient client, ProviderLifetime? disposal = null) =>
		new(client, disposal ?? new ProviderLifetime());

	[TestMethod]
	public async Task TryCompleteSuccessfulStart_OnTheCurrentReadyGeneration_TransitionsToReady()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		await client.EnsureStartedAsync(CancellationToken.None).ConfigureAwait(false);
		StartupState state = CreateState(client);

		Assert.IsTrue(state.TryCompleteSuccessfulStart(client.ConnectionGeneration, out LanguageServerFeatureProviderState previousState));

		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, previousState);
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, state.State);
		Assert.IsTrue(state.StartupSucceeded);
	}

	[TestMethod]
	public async Task TryCompleteSuccessfulStart_WhenClientIsNotReady_FailsWithoutTransitioning()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		await client.EnsureStartedAsync(CancellationToken.None).ConfigureAwait(false);

		// Isolate the not-ready condition: the generation is valid and current, only readiness fails.
		client.IsReady = false;

		StartupState state = CreateState(client);

		Assert.IsFalse(state.TryCompleteSuccessfulStart(client.ConnectionGeneration, out _));
		Assert.IsFalse(state.StartupSucceeded);
		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, state.State);
	}

	[TestMethod]
	public async Task TryCompleteSuccessfulStart_WhenGenerationWasReportedUnavailable_Fails()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		await client.EnsureStartedAsync(CancellationToken.None).ConfigureAwait(false);

		StartupState state = CreateState(client);

		Assert.IsFalse(state.HandleClientConnectionUnavailable(client.ConnectionGeneration));
		Assert.IsFalse(state.TryCompleteSuccessfulStart(client.ConnectionGeneration, out _));
		Assert.IsFalse(state.StartupSucceeded);
	}

	[TestMethod]
	public async Task HandleClientConnectionUnavailable_InvalidatesOnlyTheStartupOfThatGeneration()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		await client.EnsureStartedAsync(CancellationToken.None).ConfigureAwait(false);

		StartupState state = CreateState(client);

		Assert.IsTrue(state.TryCompleteSuccessfulStart(client.ConnectionGeneration, out _));
		Assert.IsTrue(state.HandleClientConnectionUnavailable(client.ConnectionGeneration));
		Assert.IsFalse(state.StartupSucceeded);

		// A start on the next generation succeeds, and a late notification for the previous
		// generation does not invalidate it.
		client.IsReady = false;
		await client.EnsureStartedAsync(CancellationToken.None).ConfigureAwait(false);

		Assert.IsTrue(state.TryCompleteSuccessfulStart(client.ConnectionGeneration, out _));
		Assert.IsFalse(state.HandleClientConnectionUnavailable(client.ConnectionGeneration - 1));
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
		await client.EnsureStartedAsync(CancellationToken.None).ConfigureAwait(false);

		Assert.IsTrue(state.TryCompleteSuccessfulStart(client.ConnectionGeneration, out _));
		Assert.AreEqual(0, state.ConsecutiveStartupFailures);
		Assert.IsTrue(state.TryMarkStartupFailureReported(isPersistentFailure: false));
	}

	[TestMethod]
	public async Task TryCompleteSuccessfulStart_WithGenerationZero_IsRejected()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		StartupState state = CreateState(client);

		await client.EnsureStartedAsync(CancellationToken.None).ConfigureAwait(false);

		// Generation zero means no session was started yet; the fencing check must reject the completion.
		Assert.IsFalse(state.TryCompleteSuccessfulStart(0, out _));
		Assert.IsFalse(state.StartupSucceeded);
	}

	[TestMethod]
	public void TrySetState_HonorsTheTerminalDisposedState()
	{
		using var client = new FakeLanguageServerClient();

		var disposal = new ProviderLifetime();
		StartupState state = CreateState(client, disposal);

		Assert.IsTrue(state.TrySetState(LanguageServerFeatureProviderState.Ready, notifyCapabilitiesChanged: true));

		disposal.Signal();

		// Once disposal started, only the disposed transition is still accepted.
		Assert.IsFalse(state.TrySetState(LanguageServerFeatureProviderState.Starting, notifyCapabilitiesChanged: true));
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, state.State);

		state.TrySetState(LanguageServerFeatureProviderState.Disposed, notifyCapabilitiesChanged: false);
		Assert.AreEqual(LanguageServerFeatureProviderState.Disposed, state.State);

		// The disposed state is terminal.
		Assert.IsFalse(state.TrySetState(LanguageServerFeatureProviderState.Ready, notifyCapabilitiesChanged: true));
		Assert.AreEqual(LanguageServerFeatureProviderState.Disposed, state.State);
	}

	[TestMethod]
	public async Task TryCompleteSuccessfulStart_AfterDisposalStarted_FailsWithoutTransitioning()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		await client.EnsureStartedAsync(CancellationToken.None).ConfigureAwait(false);

		var disposal = new ProviderLifetime();
		StartupState state = CreateState(client, disposal);

		disposal.Signal();

		Assert.IsFalse(state.TryCompleteSuccessfulStart(client.ConnectionGeneration, out _));
		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, state.State);
		Assert.IsFalse(state.StartupSucceeded);
	}

	[TestMethod]
	public async Task HandleClientConnectionUnavailable_AfterDisposalStarted_ChangesNothing()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		await client.EnsureStartedAsync(CancellationToken.None).ConfigureAwait(false);

		var disposal = new ProviderLifetime();
		StartupState state = CreateState(client, disposal);

		Assert.IsTrue(state.TryCompleteSuccessfulStart(client.ConnectionGeneration, out _));

		disposal.Signal();

		// A disposing provider does not react to the loss: the startup stays recorded and no transition is requested.
		Assert.IsFalse(state.HandleClientConnectionUnavailable(client.ConnectionGeneration));
		Assert.IsTrue(state.StartupSucceeded);
	}
}
