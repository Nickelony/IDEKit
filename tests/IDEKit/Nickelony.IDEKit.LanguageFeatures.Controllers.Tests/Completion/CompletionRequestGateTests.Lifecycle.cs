using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Covers how a newer request, a cancellation, an invalidation, a host context change, and a disposal supersede an outstanding request,
/// including the window between the coordinator's checks and the dispatched apply step, and the safe behavior of the gate after disposal.
/// </summary>
public sealed partial class CompletionRequestGateTests
{
	[TestMethod]
	public void InvalidateRequests_DiscardsTheDecisionWithoutCancelingTheToken()
	{
		using var host = new CompletionRequestGateTestHost();
		using CompletionRequestGate gate = host.CreateGate();

		var provider = new CompletionRequestGateTestHost.GatedDecision();

		Task<bool> request = gate.RequestAsync(provider.RunAsync);
		host.Dispatcher.PumpUntil(() => provider.Started.IsCompleted);

		gate.InvalidateRequests();

		// Invalidating only rejects the result; the provider keeps running until it is canceled explicitly.
		Assert.IsFalse(provider.Token.IsCancellationRequested);

		provider.Release();

		Assert.IsFalse(host.RunToCompletion(request));
		Assert.IsEmpty(host.Applied);
	}

	[TestMethod]
	public void CancelInFlightRequest_CancelsTheTokenAndDiscardsTheDecision()
	{
		using var host = new CompletionRequestGateTestHost();
		using CompletionRequestGate gate = host.CreateGate();

		var provider = new CompletionRequestGateTestHost.GatedDecision();

		Task<bool> request = gate.RequestAsync(provider.RunAsync);
		host.Dispatcher.PumpUntil(() => provider.Started.IsCompleted);

		gate.CancelInFlightRequest();

		// Canceling stops the provider call, and a provider that ignores the token and returns a decision anyway is discarded.
		Assert.IsTrue(provider.Token.IsCancellationRequested);

		provider.Release();

		Assert.IsFalse(host.RunToCompletion(request));
		Assert.IsEmpty(host.Applied);
	}

	[TestMethod]
	public void RequestAsync_NewerRequest_CancelsThePreviousTokenAndDiscardsItsDecision()
	{
		using var host = new CompletionRequestGateTestHost();
		using CompletionRequestGate gate = host.CreateGate();

		var first = new CompletionRequestGateTestHost.GatedDecision { Decision = CompletionRequestGateTestHost.OpenDecision("stale") };

		Task<bool> firstRequest = gate.RequestAsync(first.RunAsync);
		host.Dispatcher.PumpUntil(() => first.Started.IsCompleted);

		CompletionSessionDirective newer = CompletionRequestGateTestHost.OpenDecision("newer");
		Task<bool> secondRequest = gate.RequestAsync(_ => Task.FromResult(newer));

		Assert.IsTrue(host.RunToCompletion(secondRequest));

		// The newest request wins: its start canceled the first provider's token, and the first decision, which arrives afterwards, is discarded.
		Assert.IsTrue(first.Token.IsCancellationRequested);

		first.Release();

		Assert.IsFalse(host.RunToCompletion(firstRequest));
		Assert.AreSequenceEqual([newer], host.Applied);
	}

	[TestMethod]
	public void RequestAsync_ContextVersionChangedInFlight_DiscardsTheDecisionWithoutCancelingTheToken()
	{
		int contextVersion = 1;

		using var host = new CompletionRequestGateTestHost { ContextVersionProvider = () => contextVersion };
		using CompletionRequestGate gate = host.CreateGate();

		var provider = new CompletionRequestGateTestHost.GatedDecision();

		Task<bool> request = gate.RequestAsync(provider.RunAsync);
		host.Dispatcher.PumpUntil(() => provider.Started.IsCompleted);

		// The host state moved on while the provider was working, so the decision belongs to the earlier state.
		contextVersion++;
		provider.Release();

		Assert.IsFalse(host.RunToCompletion(request));
		Assert.IsEmpty(host.Applied);
		Assert.IsFalse(provider.Token.IsCancellationRequested, "The version check discards the decision without canceling the provider.");
	}

	[TestMethod]
	public void RequestAsync_ContextVersionUnchanged_AppliesTheDecision()
	{
		using var host = new CompletionRequestGateTestHost { ContextVersionProvider = static () => 7 };
		using CompletionRequestGate gate = host.CreateGate();

		var provider = new CompletionRequestGateTestHost.GatedDecision();

		Task<bool> request = gate.RequestAsync(provider.RunAsync);
		host.Dispatcher.PumpUntil(() => provider.Started.IsCompleted);

		provider.Release();

		Assert.IsTrue(host.RunToCompletion(request));
		Assert.HasCount(1, host.Applied);
	}

	[TestMethod]
	public void RequestAsync_ContextVersionProviderThrowsWhenTheDecisionArrives_IsContainedAndLogged()
	{
		int calls = 0;

		using var host = new CompletionRequestGateTestHost
		{
			ContextVersionProvider = () => ++calls == 1 ? 1 : throw new InvalidOperationException("Version failed."),
		};

		using CompletionRequestGate gate = host.CreateGate();

		var provider = new CompletionRequestGateTestHost.GatedDecision();

		Task<bool> request = gate.RequestAsync(provider.RunAsync);
		host.Dispatcher.PumpUntil(() => provider.Started.IsCompleted);

		provider.Release();

		// The check runs when the decision arrives, so a failure of the host hook there is contained like a provider failure.
		Assert.IsFalse(host.RunToCompletion(request));
		Assert.IsEmpty(host.Applied);
		Assert.HasCount(1, host.Logger.Entries);
		Assert.AreEqual(1000, host.Logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void RequestAsync_ThrowingContextVersionProvider_IsContainedAndLoggedAndStartsNoRequest()
	{
		bool providerCalled = false;

		using var host = new CompletionRequestGateTestHost
		{
			ContextVersionProvider = static () => throw new InvalidOperationException("Version failed."),
		};

		using CompletionRequestGate gate = host.CreateGate();

		Task<bool> request = gate.RequestAsync(_ =>
		{
			providerCalled = true;
			return Task.FromResult(CompletionSessionDirective.None);
		});

		// The hook is host code, so its failure is contained like a provider failure and no request starts.
		Assert.IsFalse(host.RunToCompletion(request));
		Assert.IsFalse(providerCalled);
		Assert.HasCount(1, host.Logger.Entries);
		Assert.AreEqual(1000, host.Logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void RequestAsync_DisposalLandsBeforeTheQueuedApply_AppliesNothing()
	{
		using var host = new CompletionRequestGateTestHost(installSynchronizationContext: false);

		StaleDecisionScenario scenario = StartStaleDecisionScenario(host);

		// The disposal lands between the coordinator's checks and the queued step, which must not apply after it.
		scenario.Gate.Dispose();
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted);

		Assert.IsFalse(scenario.Request.GetAwaiter().GetResult());
		Assert.IsEmpty(host.Applied);
	}

	[TestMethod]
	public void RequestAsync_CancellationLandsBeforeTheQueuedApply_AppliesNothing()
	{
		using var host = new CompletionRequestGateTestHost(installSynchronizationContext: false);

		StaleDecisionScenario scenario = StartStaleDecisionScenario(host);

		// A cancellation in the same window must not be undone by the decision that had already passed the coordinator's checks.
		scenario.Gate.CancelInFlightRequest();
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted);

		Assert.IsFalse(scenario.Request.GetAwaiter().GetResult());
		Assert.IsEmpty(host.Applied);

		scenario.Gate.Dispose();
	}

	[TestMethod]
	public void RequestAsync_InvalidationLandsBeforeTheQueuedApply_AppliesNothing()
	{
		using var host = new CompletionRequestGateTestHost(installSynchronizationContext: false);

		StaleDecisionScenario scenario = StartStaleDecisionScenario(host);

		scenario.Gate.InvalidateRequests();
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted);

		Assert.IsFalse(scenario.Request.GetAwaiter().GetResult());
		Assert.IsEmpty(host.Applied);

		scenario.Gate.Dispose();
	}

	[TestMethod]
	public void RequestAsync_NewerRequestStartsBeforeTheQueuedApply_AppliesOnlyTheNewerDecision()
	{
		using var host = new CompletionRequestGateTestHost(installSynchronizationContext: false);

		StaleDecisionScenario scenario = StartStaleDecisionScenario(host);

		CompletionSessionDirective newer = CompletionRequestGateTestHost.OpenDecision("newer");

		// The newer request supersedes the decision that waits in the queue; only the newer decision may apply.
		Task<bool> secondRequest = scenario.Gate.RequestAsync(_ => Task.FromResult(newer));

		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted && secondRequest.IsCompleted);

		Assert.IsFalse(scenario.Request.GetAwaiter().GetResult());
		Assert.IsTrue(secondRequest.GetAwaiter().GetResult());
		Assert.AreSequenceEqual([newer], host.Applied);

		scenario.Gate.Dispose();
	}

	[TestMethod]
	public void RequestAsync_DispatcherShutsDownBeforeTheApply_CompletesWithoutFaultingOrLogging()
	{
		using var host = new CompletionRequestGateTestHost(installSynchronizationContext: false);

		StaleDecisionScenario scenario = StartStaleDecisionScenario(host);

		// The dispatcher rejects the queued apply; the gate treats that as the end of the request, not as a failure.
		host.Dispatcher.Dispose();

		Assert.IsTrue(SpinWait.SpinUntil(() => scenario.Request.IsCompleted, TimeSpan.FromSeconds(5.0)));
		Assert.IsFalse(scenario.Request.GetAwaiter().GetResult());
		Assert.IsEmpty(host.Applied);
		Assert.IsEmpty(host.Logger.Entries);

		scenario.Gate.Dispose();
	}

	[TestMethod]
	public void Dispose_WhileRequestInFlight_AppliesAndLogsNothingWhenTheProviderFailsAfterwards()
	{
		using var host = new CompletionRequestGateTestHost();

		CompletionRequestGate gate = host.CreateGate();

		var provider = new CompletionRequestGateTestHost.GatedDecision();

		Task<bool> request = gate.RequestAsync(provider.RunAsync);
		host.Dispatcher.PumpUntil(() => provider.Started.IsCompleted);

		gate.Dispose();

		// Disposal cancels the token of the request in flight.
		Assert.IsTrue(provider.Token.IsCancellationRequested);

		provider.Fail(new InvalidOperationException("Provider failed after the gate was disposed."));

		// The request belonged to a disposed gate, so its failure neither applies anything nor reaches the log.
		Assert.IsFalse(host.RunToCompletion(request));
		Assert.IsEmpty(host.Applied);
		Assert.IsEmpty(host.Logger.Entries);
	}

	[TestMethod]
	public void RequestAsync_AfterDisposal_ReturnsFalseWithoutCallingTheCallback()
	{
		using var host = new CompletionRequestGateTestHost();

		CompletionRequestGate gate = host.CreateGate();

		gate.Dispose();

		bool callbackCalled = false;

		Task<bool> request = gate.RequestAsync(_ =>
		{
			callbackCalled = true;
			return Task.FromResult(CompletionRequestGateTestHost.OpenDecision());
		});

		Assert.IsTrue(request.IsCompletedSuccessfully);
		Assert.IsFalse(request.GetAwaiter().GetResult());
		Assert.IsFalse(callbackCalled);
	}

	[TestMethod]
	public void PublicOperations_AfterDisposal_AreSafeFromAnyThread()
	{
		using var host = new CompletionRequestGateTestHost { ScheduledRequestAsync = static _ => Task.CompletedTask };

		CompletionRequestGate gate = host.CreateGate();

		gate.Dispose();

		Assert.IsTrue(gate.IsDisposed);

		Task<bool>? request = null;

		// After disposal every member returns before the dispatcher-affinity check, so the calls are safe from any thread.
		Exception? failure = CaptureOffThreadFailure(() =>
		{
			gate.ScheduleRequest();
			gate.CancelScheduledRequest();
			gate.CancelInFlightRequest();
			gate.InvalidateRequests();
			request = gate.RequestAsync(static _ => Task.FromResult(CompletionSessionDirective.None));
			gate.Dispose();
		});

		Assert.IsNull(failure);
		Assert.IsNotNull(request);
		Assert.IsTrue(request.IsCompletedSuccessfully);
		Assert.IsEmpty(host.ScheduledFlags);
	}

	// Starts a request whose provider answers on a thread-pool thread and waits until the gate's apply step for that answer is queued on the dispatcher,
	// so the test can interrupt the request in the window between the coordinator's checks and the dispatched step.
	private static StaleDecisionScenario StartStaleDecisionScenario(CompletionRequestGateTestHost host)
	{
		CompletionRequestGate gate = host.CreateGate();

		var provider = new CompletionRequestGateTestHost.GatedDecision();

		Task<bool> request = gate.RequestAsync(provider.RunAsync);

		host.Dispatcher.PumpUntil(() => provider.Started.IsCompleted);

		// Without a synchronization context the decision resumes on a thread-pool thread, whose apply step waits in the dispatcher's queue.
		provider.Release();

		Assert.IsTrue(SpinWait.SpinUntil(() => host.Dispatcher.QueuedCount == 1, TimeSpan.FromSeconds(5.0)));

		return new StaleDecisionScenario(gate, request);
	}

	private readonly record struct StaleDecisionScenario(CompletionRequestGate Gate, Task<bool> Request);
}
