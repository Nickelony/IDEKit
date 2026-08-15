using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Pins the shared background-task exception policy: cancellation and disposal stay silent, transport
/// failures log at debug level, and anything else logs as a warning so a faulted background task can
/// never go unobserved.
/// </summary>
[TestClass]
public sealed class BackgroundTaskObserverTests
{
	[TestMethod]
	public async Task Observe_TransportFailure_LogsDebugForTheOperation()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		var faultedTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		faultedTask.SetException(new IOException("Simulated transport failure."));

		BackgroundTaskObserver.Observe(logScope, faultedTask.Task, "Transport operation");

		await WaitForLogAsync(logScope, "Transport operation").ConfigureAwait(false);

		string log = logScope.Logs.Single();

		Assert.IsTrue(log.StartsWith("Debug|", StringComparison.Ordinal), log);
		Assert.IsTrue(log.Contains("Transport operation", StringComparison.Ordinal), log);
		Assert.IsTrue(log.Contains("Simulated transport failure.", StringComparison.Ordinal), log);
	}

	[TestMethod]
	public async Task Observe_UnexpectedFailure_LogsWarningForTheOperation()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);
		var faultedTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		faultedTask.SetException(new InvalidOperationException("Simulated unexpected failure."));

		BackgroundTaskObserver.Observe(logScope, faultedTask.Task, "Unexpected operation");

		await WaitForLogAsync(logScope, "Unexpected operation").ConfigureAwait(false);

		string log = logScope.Logs.Single();

		Assert.IsTrue(log.StartsWith("Warn|", StringComparison.Ordinal), log);
		Assert.IsTrue(log.Contains("Unexpected operation", StringComparison.Ordinal), log);
		Assert.IsTrue(log.Contains("Simulated unexpected failure.", StringComparison.Ordinal), log);
	}

	[TestMethod]
	public async Task Observe_CanceledTask_IsSilent()
	{
		using var logScope = new TestLoggerScope(LogLevel.Trace);
		var canceledTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var sentinelTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		canceledTask.SetCanceled();
		sentinelTask.SetException(new InvalidOperationException("Sentinel failure."));

		BackgroundTaskObserver.Observe(logScope, canceledTask.Task, "Canceled operation");
		BackgroundTaskObserver.Observe(logScope, sentinelTask.Task, "Sentinel operation");

		await WaitForLogAsync(logScope, "Sentinel operation").ConfigureAwait(false);

		// Negative check: cancellation never logs; the bounded settle window is deliberate because the
		// observer's completion has no signal.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.IsFalse(
			logScope.Logs.Any(log => log.Contains("Canceled operation", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public async Task Observe_DisposedTask_IsSilent()
	{
		using var logScope = new TestLoggerScope(LogLevel.Trace);
		var disposedTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var sentinelTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		disposedTask.SetException(new ObjectDisposedException("Simulated disposed object."));
		sentinelTask.SetException(new InvalidOperationException("Sentinel failure."));

		BackgroundTaskObserver.Observe(logScope, disposedTask.Task, "Disposed operation");
		BackgroundTaskObserver.Observe(logScope, sentinelTask.Task, "Sentinel operation");

		await WaitForLogAsync(logScope, "Sentinel operation").ConfigureAwait(false);

		// Negative check: disposal races never log; the bounded settle window is deliberate because the
		// observer's completion has no signal.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.IsFalse(
			logScope.Logs.Any(log => log.Contains("Disposed operation", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public async Task Observe_SuccessfulTask_IsSilent()
	{
		using var logScope = new TestLoggerScope(LogLevel.Trace);
		var successfulTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var sentinelTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		successfulTask.SetResult(true);
		sentinelTask.SetException(new InvalidOperationException("Sentinel failure."));

		BackgroundTaskObserver.Observe(logScope, successfulTask.Task, "Successful operation");
		BackgroundTaskObserver.Observe(logScope, sentinelTask.Task, "Sentinel operation");

		await WaitForLogAsync(logScope, "Sentinel operation").ConfigureAwait(false);

		// Negative check with the same bounded settle window as the canceled/disposed cases: the
		// observer's completion has no signal, so absence must be asserted after a delay.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.IsFalse(
			logScope.Logs.Any(log => log.Contains("Successful operation", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	private static async Task WaitForLogAsync(TestLoggerScope logScope, string operationName)
	{
		bool observed = await TestPolling.ForConditionAsync(
			() => logScope.Logs.Any(log => log.Contains(operationName, StringComparison.Ordinal)),
			TimeSpan.FromSeconds(2)).ConfigureAwait(false);

		if (!observed)
			Assert.Fail($"Expected a log entry for '{operationName}'. Captured: {string.Join(Environment.NewLine, logScope.Logs)}");
	}
}
