using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class BackgroundTaskObserverTests
{
	private static BackgroundTaskObserver CreateObserver(ILogger logger, ClientLifetime? lifetime = null, Action? markConnectionUnhealthy = null) =>
		new(lifetime ?? CreateLifetime(logger), markConnectionUnhealthy: markConnectionUnhealthy ?? (() => { }), logger);

	private static ClientLifetime CreateLifetime(ILogger logger) => new(TimeSpan.FromSeconds(5), workspaceDisplayText: TestPaths.Root, logger);

	[TestMethod]
	public async Task ObserveAbandoned_WhenTheTaskLaterFaults_LogsADebugDiagnostic()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		BackgroundTaskObserver observer = CreateObserver(logger);
		var abandoned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		observer.ObserveAbandoned(abandoned.Task);
		abandoned.TrySetException(new IOException("Simulated abandoned notification failure."));

		await TestPolling
			.UntilAsync(
				() =>
					logger.Logs.Any(log =>
						log.StartsWith("Debug|", StringComparison.Ordinal)
						&& log.Contains("abandoned language server connection task", StringComparison.OrdinalIgnoreCase)
						&& log.Contains("Simulated abandoned notification failure.", StringComparison.Ordinal)
					),

				TimeSpan.FromSeconds(5),
				"The abandoned task failure should be logged at debug level."
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ObserveAbandoned_WhenTheTaskIsCanceledOrSucceeds_LogsNothing()
	{
		using var logger = new RecordingLogger(LogLevel.Trace);

		BackgroundTaskObserver observer = CreateObserver(logger);

		var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var succeeded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		observer.ObserveAbandoned(canceled.Task);
		observer.ObserveAbandoned(succeeded.Task);

		canceled.TrySetCanceled();
		succeeded.TrySetResult(true);

		await Task.WhenAll(canceled.Task.ContinueWith(static _ => { }, TaskScheduler.Default), succeeded.Task).ConfigureAwait(false);
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.IsEmpty(logger.Logs);
	}

	[TestMethod]
	public async Task ObserveLoop_WhenTheLoopFaultsWhileTheClientIsActive_LogsAWarningAndMarksTheConnectionUnhealthy()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		int markCount = 0;
		BackgroundTaskObserver observer = CreateObserver(logger, markConnectionUnhealthy: () => Interlocked.Increment(ref markCount));

		observer.ObserveLoop(Task.FromException(new IOException("Simulated loop failure.")), "server-message dispatcher");

		await TestPolling
			.UntilAsync(
				() =>
					logger.Logs.Any(log =>
						log.StartsWith("Warn|", StringComparison.Ordinal)
						&& log.Contains("'server-message dispatcher' terminated unexpectedly", StringComparison.Ordinal)
						&& log.Contains("Simulated loop failure.", StringComparison.Ordinal)
					),

				TimeSpan.FromSeconds(5),
				"The unexpected loop failure should be logged as a warning."
			)
			.ConfigureAwait(false);

		Assert.AreEqual(1, Volatile.Read(ref markCount));
	}

	[TestMethod]
	public async Task ObserveLoop_WhenTheLoopIsCanceledWhileTheClientIsActive_LogsAWarningAndMarksTheConnectionUnhealthy()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		int markCount = 0;
		BackgroundTaskObserver observer = CreateObserver(logger, markConnectionUnhealthy: () => Interlocked.Increment(ref markCount));

		observer.ObserveLoop(Task.FromCanceled(new CancellationToken(canceled: true)), "diagnostics coalescer");

		await TestPolling
			.UntilAsync(
				() =>
					logger.Logs.Any(log =>
						log.StartsWith("Warn|", StringComparison.Ordinal)
						&& log.Contains("'diagnostics coalescer' was canceled unexpectedly", StringComparison.Ordinal)
					),

				TimeSpan.FromSeconds(5),
				"The unexpected loop cancellation should be logged as a warning."
			)
			.ConfigureAwait(false);

		Assert.AreEqual(1, Volatile.Read(ref markCount));
	}

	[TestMethod]
	public async Task ObserveLoop_WhenTheLoopCompletesWhileTheClientIsActive_LogsAWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		BackgroundTaskObserver observer = CreateObserver(logger);

		observer.ObserveLoop(Task.CompletedTask, "server-message dispatcher");

		await TestPolling
			.UntilAsync(
				() => logger.Logs.Any(log => log.Contains("'server-message dispatcher' completed unexpectedly", StringComparison.Ordinal)),
				TimeSpan.FromSeconds(5),
				"A loop that completes while the client is active should be logged."
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	[DataRow(true, false, DisplayName = "The client is disposed")]
	[DataRow(false, true, DisplayName = "The lifetime token is canceled")]
	public async Task ObserveLoop_WhenTheClientIsShuttingDown_LogsNothingAndDoesNotMarkTheConnection(bool isDisposed, bool lifetimeCanceled)
	{
		using var logger = new RecordingLogger(LogLevel.Trace);
		ClientLifetime lifetime = CreateLifetime(logger);

		if (isDisposed)
			_ = lifetime.BeginDisposeAsync(static () => Task.CompletedTask);

		if (lifetimeCanceled)
			lifetime.Cancel();

		int markCount = 0;

		BackgroundTaskObserver observer = CreateObserver(logger, lifetime, markConnectionUnhealthy: () => Interlocked.Increment(ref markCount));

		var loop = Task.FromException(new IOException("Simulated loop failure during shutdown."));

		observer.ObserveLoop(loop, "server-message dispatcher");

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.IsEmpty(logger.Logs);
		Assert.AreEqual(0, Volatile.Read(ref markCount));
		Assert.IsFalse(observer.WasLoopTerminationLogged(loop));
	}

	[TestMethod]
	public async Task ObserveLoop_ObservingTheSameLoopTwice_ReportsItOnce()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		int markCount = 0;
		BackgroundTaskObserver observer = CreateObserver(logger, markConnectionUnhealthy: () => Interlocked.Increment(ref markCount));

		var loop = Task.FromException(new IOException("Simulated loop failure."));

		observer.ObserveLoop(loop, "server-message dispatcher");
		observer.ObserveLoop(loop, "server-message dispatcher");

		await TestPolling
			.UntilAsync(() => observer.WasLoopTerminationLogged(loop), TimeSpan.FromSeconds(5), "The loop should be recorded.")
			.ConfigureAwait(false);

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.AreEqual(1, Volatile.Read(ref markCount));
		Assert.HasCount(1, logger.Logs);
	}

	[TestMethod]
	public async Task ForgetLoop_AndForgetAllLoops_DropTheRecordedTerminations()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		BackgroundTaskObserver observer = CreateObserver(logger);

		var first = Task.FromException(new IOException("first"));
		var second = Task.FromException(new IOException("second"));

		observer.ObserveLoop(first, "server-message dispatcher");
		observer.ObserveLoop(second, "diagnostics coalescer");

		await TestPolling
			.UntilAsync(
				() => observer.WasLoopTerminationLogged(first) && observer.WasLoopTerminationLogged(second),
				TimeSpan.FromSeconds(5),
				"Both loops should be recorded."
			)
			.ConfigureAwait(false);

		observer.ForgetLoop(first);

		Assert.IsFalse(observer.WasLoopTerminationLogged(first));
		Assert.IsTrue(observer.WasLoopTerminationLogged(second));

		observer.ForgetAllLoops();

		Assert.IsFalse(observer.WasLoopTerminationLogged(second));
	}

	[TestMethod]
	public async Task StartObservedLoop_RunsTheLoopAndReportsItsUnexpectedEnd()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		BackgroundTaskObserver observer = CreateObserver(logger);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Task loop = observer.StartObservedLoop(async () => await release.Task.ConfigureAwait(false), "server-message dispatcher");

		Assert.IsFalse(loop.IsCompleted);
		Assert.IsEmpty(logger.Logs);

		release.SetResult();
		await loop.ConfigureAwait(false);

		await TestPolling
			.UntilAsync(
				() => logger.Logs.Any(log => log.Contains("'server-message dispatcher' completed unexpectedly", StringComparison.Ordinal)),
				TimeSpan.FromSeconds(5),
				"The completed loop should be reported."
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public void TryLogCanceledLoop_LogsOnlyACanceledTaskAtDebugLevel()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		BackgroundTaskObserver observer = CreateObserver(logger);

		Assert.IsFalse(observer.TryLogCanceledLoop(null, "stderr read"));
		Assert.IsFalse(observer.TryLogCanceledLoop(Task.CompletedTask, "stderr read"));
		Assert.IsFalse(observer.TryLogCanceledLoop(Task.FromException(new IOException("fault")), "stderr read"));
		Assert.IsEmpty(logger.Logs);

		Assert.IsTrue(observer.TryLogCanceledLoop(Task.FromCanceled(new CancellationToken(canceled: true)), "stderr read"));

		Assert.Contains(
			log => log.StartsWith("Debug|", StringComparison.Ordinal) && log.Contains("'stderr read' was canceled during disposal"),
			logger.Logs
		);
	}

	[TestMethod]
	public void TryLogFaultedLoop_LogsOnlyAFaultedTaskAsAWarningWithItsException()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		BackgroundTaskObserver observer = CreateObserver(logger);

		Assert.IsFalse(observer.TryLogFaultedLoop(null, "JSON-RPC completion"));
		Assert.IsFalse(observer.TryLogFaultedLoop(Task.CompletedTask, "JSON-RPC completion"));
		Assert.IsFalse(observer.TryLogFaultedLoop(Task.FromCanceled(new CancellationToken(canceled: true)), "JSON-RPC completion"));
		Assert.IsEmpty(logger.Logs);

		Assert.IsTrue(observer.TryLogFaultedLoop(Task.FromException(new IOException("Simulated loop failure.")), "JSON-RPC completion"));

		Assert.Contains(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("'JSON-RPC completion' failed during disposal")
				&& log.Contains("Simulated loop failure."),

			logger.Logs
		);
	}

	[TestMethod]
	public async Task WaitForTeardownStageAsync_StageCompletesInTime_LogsNothing()
	{
		using var logger = new RecordingLogger(LogLevel.Trace);
		BackgroundTaskObserver observer = CreateObserver(logger);

		await observer.WaitForTeardownStageAsync(Task.CompletedTask, TimeSpan.FromSeconds(5), "stage").ConfigureAwait(false);

		Assert.IsEmpty(logger.Logs);
	}

	[TestMethod]
	public async Task WaitForTeardownStageAsync_BudgetExhaustedAndStageRunning_LogsTheSkipAndObservesTheStage()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		BackgroundTaskObserver observer = CreateObserver(logger);
		var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		await observer.WaitForTeardownStageAsync(running.Task, TimeSpan.Zero, "slow stage").ConfigureAwait(false);

		Assert.Contains(
			log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("'slow stage' was skipped", StringComparison.Ordinal),
			logger.Logs
		);

		running.TrySetException(new IOException("Simulated late stage failure."));

		await TestPolling
			.UntilAsync(
				() =>
					logger.Logs.Any(log =>
						log.StartsWith("Debug|", StringComparison.Ordinal) && log.Contains("Simulated late stage failure.", StringComparison.Ordinal)
					),

				TimeSpan.FromSeconds(5),
				"The abandoned stage fault should be logged."
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task WaitForTeardownStageAsync_BudgetExhaustedAndStageCompleted_LogsNothing()
	{
		using var logger = new RecordingLogger(LogLevel.Trace);
		BackgroundTaskObserver observer = CreateObserver(logger);

		await observer.WaitForTeardownStageAsync(Task.CompletedTask, TimeSpan.Zero, "stage").ConfigureAwait(false);

		Assert.IsEmpty(logger.Logs);
	}

	[TestMethod]
	public async Task WaitForTeardownStageAsync_StageOutlastsTheBudget_LogsTheTimeoutAndObservesTheStage()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		BackgroundTaskObserver observer = CreateObserver(logger);
		var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		await observer.WaitForTeardownStageAsync(running.Task, TimeSpan.FromMilliseconds(30), "slow stage").ConfigureAwait(false);

		Assert.Contains(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("'slow stage' did not complete within", StringComparison.Ordinal),

			logger.Logs
		);

		running.TrySetException(new IOException("Simulated late stage failure."));

		await TestPolling
			.UntilAsync(
				() =>
					logger.Logs.Any(log =>
						log.StartsWith("Debug|", StringComparison.Ordinal) && log.Contains("Simulated late stage failure.", StringComparison.Ordinal)
					),

				TimeSpan.FromSeconds(5),
				"The abandoned stage fault should be logged."
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task WaitForTeardownStageAsync_StageFaults_LogsTheExceptionOnce()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		BackgroundTaskObserver observer = CreateObserver(logger);

		await observer
			.WaitForTeardownStageAsync(Task.FromException(new IOException("Simulated stage failure.")), TimeSpan.FromSeconds(5), "failing stage")
			.ConfigureAwait(false);

		Assert.ContainsSingle(
			log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("'failing stage' raised an exception", StringComparison.Ordinal),
			logger.Logs
		);
	}

	[TestMethod]
	public async Task WaitForTeardownStageAsync_StageIsALoopThatAlreadyLoggedItsTermination_LogsNoSecondWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);
		BackgroundTaskObserver observer = CreateObserver(logger);
		var loop = Task.FromException(new IOException("Simulated loop failure."));

		observer.ObserveLoop(loop, "server-message dispatcher");

		await TestPolling
			.UntilAsync(() => observer.WasLoopTerminationLogged(loop), TimeSpan.FromSeconds(5), "The loop termination should be recorded.")
			.ConfigureAwait(false);

		int warningsBefore = logger.Logs.Count;

		await observer.WaitForTeardownStageAsync(loop, TimeSpan.FromSeconds(5), "server-message dispatcher").ConfigureAwait(false);

		Assert.HasCount(warningsBefore, logger.Logs);
	}
}
