using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Covers the debounced scheduled request of the completion request gate: the delay, the restart on a second schedule, the scheduled-request flag,
/// and how a failing, canceled, or abandoned callback is reported. The virtual clock moves only when a test advances it.
/// </summary>
public sealed partial class CompletionRequestGateTests
{
	[TestMethod]
	public void ScheduleRequest_PastTheDelay_RunsTheScheduledRequestAndClearsTheFlag()
	{
		int requestCount = 0;

		using var host = new CompletionRequestGateTestHost
		{
			ScheduledRequestAsync = _ =>
			{
				requestCount++;
				return Task.CompletedTask;
			},
		};

		using CompletionRequestGate gate = host.CreateGate();

		gate.ScheduleRequest();

		Assert.AreSequenceEqual([true], host.ScheduledFlags);
		Assert.AreEqual(0, requestCount);

		host.Dispatcher.Advance(CompletionRequestGateTestHost.s_debounceDelay);

		// The debounced request ran through the timer path and cleared the scheduled flag.
		Assert.AreEqual(1, requestCount);
		Assert.AreSequenceEqual([true, false], host.ScheduledFlags);
	}

	[TestMethod]
	public void ScheduleRequest_BeforeTheDelayElapsed_RunsNothingYet()
	{
		int requestCount = 0;

		using var host = new CompletionRequestGateTestHost
		{
			ScheduledRequestAsync = _ =>
			{
				requestCount++;
				return Task.CompletedTask;
			},
		};

		using CompletionRequestGate gate = host.CreateGate();

		gate.ScheduleRequest();
		host.Dispatcher.Advance(CompletionRequestGateTestHost.s_debounceDelay - TimeSpan.FromMilliseconds(1.0));

		Assert.AreEqual(0, requestCount);

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(1.0));

		Assert.AreEqual(1, requestCount);
	}

	[TestMethod]
	public void ScheduleRequest_Again_RestartsTheDelayAndRunsOnce()
	{
		int requestCount = 0;

		using var host = new CompletionRequestGateTestHost
		{
			ScheduledRequestAsync = _ =>
			{
				requestCount++;
				return Task.CompletedTask;
			},
		};

		using CompletionRequestGate gate = host.CreateGate();

		gate.ScheduleRequest();
		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(6.0));

		gate.ScheduleRequest();
		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(6.0));

		// The second call restarted the delay, so twelve milliseconds in total have not yet run the request.
		Assert.AreEqual(0, requestCount);

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(4.0));

		Assert.AreEqual(1, requestCount);
	}

	[TestMethod]
	public void ScheduleRequest_WithoutScheduledCallback_IsIgnored()
	{
		using var host = new CompletionRequestGateTestHost();
		using CompletionRequestGate gate = host.CreateGate();

		gate.ScheduleRequest();

		Assert.IsEmpty(host.ScheduledFlags);
		Assert.AreEqual(0, host.Dispatcher.PendingTimerCount);
	}

	[TestMethod]
	public void ScheduledRequest_Throwing_IsLoggedAndTheGateStaysUsable()
	{
		using var host = new CompletionRequestGateTestHost
		{
			ScheduledRequestAsync = static _ => throw new InvalidOperationException("Completion request failed."),
		};

		using CompletionRequestGate gate = host.CreateGate();

		gate.ScheduleRequest();
		host.Dispatcher.Advance(CompletionRequestGateTestHost.s_debounceDelay);

		// A failing scheduled request is logged instead of escaping the timer callback, with the documented event id.
		Assert.HasCount(1, host.Logger.Entries);
		Assert.Contains("Completion request failed", host.Logger.Messages[0]);
		Assert.AreEqual(1000, host.Logger.Entries[0].EventId.Id);

		gate.ScheduleRequest();

		Assert.AreSequenceEqual([true, false, true], host.ScheduledFlags);
	}

	[TestMethod]
	public void ScheduledRequest_FaultingTask_IsLogged()
	{
		using var host = new CompletionRequestGateTestHost
		{
			ScheduledRequestAsync = static _ => Task.FromException(new InvalidOperationException("Completion request failed.")),
		};

		using CompletionRequestGate gate = host.CreateGate();

		gate.ScheduleRequest();
		host.Dispatcher.Advance(CompletionRequestGateTestHost.s_debounceDelay);
		host.Dispatcher.PumpUntil(() => host.Logger.Entries.Count > 0);

		Assert.HasCount(1, host.Logger.Entries);
		Assert.AreEqual(1000, host.Logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void ScheduledRequest_ReportingCancellation_IsIgnoredAndNotLogged()
	{
		int requestCount = 0;

		using var host = new CompletionRequestGateTestHost
		{
			ScheduledRequestAsync = _ =>
			{
				requestCount++;
				throw new OperationCanceledException();
			},
		};

		using CompletionRequestGate gate = host.CreateGate();

		gate.ScheduleRequest();
		host.Dispatcher.Advance(CompletionRequestGateTestHost.s_debounceDelay);

		Assert.AreEqual(1, requestCount);
		Assert.IsEmpty(host.Logger.Entries);
	}

	[TestMethod]
	public void ScheduledRequest_ObservingTheTokenCanceledByDisposal_IsNotLogged()
	{
		CancellationToken receivedToken = default;
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		using var host = new CompletionRequestGateTestHost
		{
			ScheduledRequestAsync = token =>
			{
				receivedToken = token;
				token.Register(() => completion.TrySetCanceled(token));

				return completion.Task;
			},
		};

		CompletionRequestGate gate = host.CreateGate();

		gate.ScheduleRequest();
		host.Dispatcher.Advance(CompletionRequestGateTestHost.s_debounceDelay);

		Assert.IsTrue(receivedToken.CanBeCanceled);
		Assert.IsFalse(receivedToken.IsCancellationRequested);

		gate.Dispose();
		host.Dispatcher.PumpUntil(() => completion.Task.IsCompleted);
		host.Dispatcher.PumpUntilIdle();

		// Disposal cancels the token of the running scheduled request, which reports cancellation instead of a failure.
		Assert.IsTrue(receivedToken.IsCancellationRequested);
		Assert.IsEmpty(host.Logger.Entries);
	}

	[TestMethod]
	public void ScheduledRequest_FailingAfterDisposal_IsNotLogged()
	{
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		using var host = new CompletionRequestGateTestHost { ScheduledRequestAsync = _ => completion.Task };

		CompletionRequestGate gate = host.CreateGate();

		gate.ScheduleRequest();
		host.Dispatcher.Advance(CompletionRequestGateTestHost.s_debounceDelay);

		gate.Dispose();
		completion.SetException(new InvalidOperationException("Completion request failed after disposal."));
		host.Dispatcher.PumpUntilIdle();

		Assert.IsEmpty(host.Logger.Entries);
	}

	[TestMethod]
	public void CancelScheduledRequest_BeforeTheDelayElapsed_SkipsTheRequestAndClearsTheFlag()
	{
		int requestCount = 0;

		using var host = new CompletionRequestGateTestHost
		{
			ScheduledRequestAsync = _ =>
			{
				requestCount++;
				return Task.CompletedTask;
			},
		};

		using CompletionRequestGate gate = host.CreateGate();

		gate.ScheduleRequest();
		gate.CancelScheduledRequest();

		// The scheduled request was canceled before the debounce elapsed: the flag is cleared and the callback never runs.
		Assert.AreSequenceEqual([true, false], host.ScheduledFlags);

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(100.0));

		Assert.AreEqual(0, requestCount);
	}

	[TestMethod]
	public void CancelScheduledRequest_WithNothingScheduled_ReportsNoRequestAndIsSafe()
	{
		using var host = new CompletionRequestGateTestHost { ScheduledRequestAsync = static _ => Task.CompletedTask };
		using CompletionRequestGate gate = host.CreateGate();

		gate.CancelScheduledRequest();

		Assert.AreSequenceEqual([false], host.ScheduledFlags);
	}

	[TestMethod]
	public void CancelScheduledRequest_DoesNotAffectTheRequestInFlight()
	{
		using var host = new CompletionRequestGateTestHost { ScheduledRequestAsync = static _ => Task.CompletedTask };
		using CompletionRequestGate gate = host.CreateGate();

		var provider = new CompletionRequestGateTestHost.GatedDecision();

		Task<bool> request = gate.RequestAsync(provider.RunAsync);
		host.Dispatcher.PumpUntil(() => provider.Started.IsCompleted);

		gate.ScheduleRequest();
		gate.CancelScheduledRequest();

		// The scheduled callback and the request pipeline are separate: canceling the schedule leaves the provider running.
		Assert.IsFalse(provider.Token.IsCancellationRequested);

		provider.Release();

		Assert.IsTrue(host.RunToCompletion(request));
	}

	[TestMethod]
	public void Dispose_WithAScheduledRequest_StopsTheTimerSoTheRequestNeverRuns()
	{
		int requestCount = 0;

		using var host = new CompletionRequestGateTestHost
		{
			ScheduledRequestAsync = _ =>
			{
				requestCount++;
				return Task.CompletedTask;
			},
		};

		CompletionRequestGate gate = host.CreateGate();

		gate.ScheduleRequest();
		gate.Dispose();

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(100.0));

		Assert.AreEqual(0, requestCount);
		Assert.AreEqual(0, host.Dispatcher.PendingTimerCount);
	}
}
