using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Verifies the request pipeline of the completion request gate: admission as the newest request, the contained failures,
/// the application of a decision, and the creation rules. The stale-decision, scheduling, and disposal concerns live in the sibling partials.
/// </summary>
/// <remarks>
/// The tests share <see cref="CompletionRequestGateTestHost"/>, which owns a manual dispatcher and records what the gate applied and reported.
/// A test that awaits a pending provider pumps the dispatcher instead, because the host installs its synchronization context.
/// </remarks>
[TestClass]
public sealed partial class CompletionRequestGateTests
{
	[TestMethod]
	public void RequestAsync_ProviderDecision_AppliesTheDecisionOnTheDispatcherThread()
	{
		using var host = new CompletionRequestGateTestHost();
		using CompletionRequestGate gate = host.CreateGate();

		CompletionSessionDirective decision = CompletionRequestGateTestHost.OpenDecision();

		Task<bool> request = gate.RequestAsync(_ => Task.FromResult(decision));

		Assert.IsTrue(host.RunToCompletion(request));
		Assert.HasCount(1, host.Applied);
		Assert.AreEqual(decision, host.Applied[0]);
		Assert.AreSequenceEqual([host.Dispatcher.OwnerThreadId], host.ApplyThreadIds);
	}

	[TestMethod]
	public void RequestAsync_ApplyReportingNoWindow_ReturnsFalse()
	{
		using var host = new CompletionRequestGateTestHost { ApplyDirective = static _ => false };
		using CompletionRequestGate gate = host.CreateGate();

		Task<bool> request = gate.RequestAsync(static _ => Task.FromResult(CompletionRequestGateTestHost.OpenDecision()));

		// The decision was applied, but the apply delegate opened nothing, which the request reports.
		Assert.IsFalse(host.RunToCompletion(request));
		Assert.HasCount(1, host.Applied);
	}

	[TestMethod]
	public void RequestAsync_ThrowingCallback_IsContainedAndLogged()
	{
		using var host = new CompletionRequestGateTestHost();
		using CompletionRequestGate gate = host.CreateGate();

		Task<bool> request = gate.RequestAsync(static _ => throw new InvalidOperationException("Completion provider failed."));

		// A provider failure must not fault the caller: it is contained and logged, and the request reports its not-applied result.
		Assert.IsFalse(host.RunToCompletion(request));
		Assert.IsEmpty(host.Applied);
		Assert.HasCount(1, host.Logger.Entries);
		Assert.AreEqual(1000, host.Logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void RequestAsync_FaultingProviderTask_IsContainedAndLogged()
	{
		using var host = new CompletionRequestGateTestHost();
		using CompletionRequestGate gate = host.CreateGate();

		Task<bool> request = gate.RequestAsync(static _ =>
			Task.FromException<CompletionSessionDirective>(new InvalidOperationException("Provider failed."))
		);

		Assert.IsFalse(host.RunToCompletion(request));
		Assert.HasCount(1, host.Logger.Entries);
		Assert.AreEqual(1000, host.Logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void RequestAsync_ThrowingApplyDelegate_IsContainedAndLoggedOnce()
	{
		using var host = new CompletionRequestGateTestHost { ApplyDirective = static _ => throw new InvalidOperationException("Apply failed.") };
		using CompletionRequestGate gate = host.CreateGate();

		Task<bool> request = gate.RequestAsync(static _ => Task.FromResult(CompletionRequestGateTestHost.OpenDecision()));

		// The apply step is host code, so its failure is contained like a provider failure and logged once.
		Assert.IsFalse(host.RunToCompletion(request));
		Assert.HasCount(1, host.Logger.Entries);
		Assert.AreEqual(1000, host.Logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void RequestAsync_CallbackReportingCancellation_AppliesNothingAndLogsNothing()
	{
		using var host = new CompletionRequestGateTestHost();
		using CompletionRequestGate gate = host.CreateGate();

		Task<bool> request = gate.RequestAsync(static _ => throw new OperationCanceledException());

		// A canceled request is not a failure and applies nothing.
		Assert.IsFalse(host.RunToCompletion(request));
		Assert.IsEmpty(host.Applied);
		Assert.IsEmpty(host.Logger.Entries);
	}

	[TestMethod]
	public void RequestAsync_SupersededRequest_DoesNotApplyTheStaleDecision()
	{
		using var host = new CompletionRequestGateTestHost();
		using CompletionRequestGate gate = host.CreateGate();

		var stale = new CompletionRequestGateTestHost.GatedDecision { Decision = CompletionRequestGateTestHost.OpenDecision("stale") };

		Task<bool> first = gate.RequestAsync(stale.RunAsync);
		host.Dispatcher.PumpUntil(() => stale.Started.IsCompleted);

		// The newest request wins: the first callback completes later with a decision that must not be applied.
		CompletionSessionDirective newer = CompletionRequestGateTestHost.OpenDecision("newer");
		Task<bool> second = gate.RequestAsync(_ => Task.FromResult(newer));

		Assert.IsTrue(host.RunToCompletion(second));

		stale.Release();

		Assert.IsFalse(host.RunToCompletion(first));
		Assert.AreSequenceEqual([newer], host.Applied);
	}

	[TestMethod]
	public void RequestAsync_ProviderCompletingAfterCancelInFlightRequest_DoesNotApplyTheDecision()
	{
		using var host = new CompletionRequestGateTestHost();
		using CompletionRequestGate gate = host.CreateGate();

		// The provider cancels its own request and returns a decision anyway, like a provider that cannot stop early.
		Task<bool> request = gate.RequestAsync(_ =>
		{
			gate.CancelInFlightRequest();
			return Task.FromResult(CompletionRequestGateTestHost.OpenDecision());
		});

		Assert.IsFalse(host.RunToCompletion(request));
		Assert.IsEmpty(host.Applied);
	}

	[TestMethod]
	[DataRow(true, DisplayName = "WithTheSynchronizationContext")]
	[DataRow(false, DisplayName = "WithoutTheSynchronizationContext")]
	public void RequestAsync_PoolThreadProviderContinuation_AppliesTheDecisionOnTheDispatcherThread(bool installSynchronizationContext)
	{
		using var host = new CompletionRequestGateTestHost(installSynchronizationContext);

		using CompletionRequestGate gate = host.CreateGate();

		var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var providerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		// The provider continuation resumes on a thread-pool thread, so the apply step must still run on the dispatcher thread.
		Task<bool> request = gate.RequestAsync(async _ =>
		{
			providerStarted.SetResult();

			await providerGate.Task.ConfigureAwait(false);
			return CompletionRequestGateTestHost.OpenDecision();
		});

		// Wait for the provider to reach the gate instead of a fixed window, so the release cannot race the provider's start on a loaded machine.
		host.Dispatcher.PumpUntil(() => providerStarted.Task.IsCompleted);
		providerGate.SetResult();

		Assert.IsTrue(host.RunToCompletion(request));
		Assert.AreSequenceEqual([host.Dispatcher.OwnerThreadId], host.ApplyThreadIds);
	}

	[TestMethod]
	public void RequestAsync_OffTheDispatcherThread_FaultsTheReturnedTask()
	{
		using var host = new CompletionRequestGateTestHost();

		using CompletionRequestGate gate = host.CreateGate();

		Task<bool>? request = null;
		bool callbackCalled = false;

		Exception? failure = CaptureOffThreadFailure(() =>
			request = gate.RequestAsync(_ =>
			{
				callbackCalled = true;
				return Task.FromResult(CompletionSessionDirective.None);
			})
		);

		// The asynchronous member reports the thread violation through the task it returns, and it starts nothing.
		Assert.IsNull(failure);
		Assert.IsNotNull(request);
		Assert.IsTrue(request.IsFaulted);
		Assert.IsInstanceOfType<InvalidOperationException>(request.Exception?.InnerException);
		Assert.IsFalse(callbackCalled);
	}

	[TestMethod]
	public void Constructor_OffTheDispatcherThread_ThrowsInvalidOperationException()
	{
		using var host = new CompletionRequestGateTestHost();

		IUiDispatcher dispatcher = host.Dispatcher;

		Exception? failure = CaptureOffThreadFailure(() => _ = new CompletionRequestGate(dispatcher, applyDecision: static _ => true));

		Assert.IsInstanceOfType<InvalidOperationException>(failure);
	}

	// A dedicated thread runs genuinely off the gate's dispatcher thread, and it carries no synchronization context.
	private static Exception? CaptureOffThreadFailure(Action action)
	{
		Exception? failure = null;

		var thread = new Thread(() =>
		{
			try
			{
				action();
			}
			catch (Exception exception)
			{
				failure = exception;
			}
		});

		thread.Start();
		thread.Join();

		return failure;
	}
}
