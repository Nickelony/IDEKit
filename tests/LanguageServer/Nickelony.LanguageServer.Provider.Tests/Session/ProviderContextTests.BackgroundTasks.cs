using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Provider.Tests;

public sealed partial class ProviderContextTests
{
	[TestMethod]
	public async Task Observe_ConnectionFailure_LogsDebugForTheOperation()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		var faultedTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		faultedTask.SetException(new IOException("Simulated connection failure."));

		Observe(logger, faultedTask.Task, "Connection operation");

		await WaitForLogAsync(logger, "Connection operation").ConfigureAwait(false);

		string log = logger.Logs.Single();

		Assert.IsTrue(log.StartsWith("Debug|", StringComparison.Ordinal), log);
		Assert.IsTrue(log.Contains("Connection operation", StringComparison.Ordinal), log);
		Assert.IsTrue(log.Contains("Simulated connection failure.", StringComparison.Ordinal), log);
	}

	[TestMethod]
	public async Task Observe_UnexpectedFailure_LogsWarningForTheOperation()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);
		var faultedTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		faultedTask.SetException(new InvalidOperationException("Simulated unexpected failure."));

		Observe(logger, faultedTask.Task, "Unexpected operation");

		await WaitForLogAsync(logger, "Unexpected operation").ConfigureAwait(false);

		string log = logger.Logs.Single();

		Assert.IsTrue(log.StartsWith("Warn|", StringComparison.Ordinal), log);
		Assert.IsTrue(log.Contains("Unexpected operation", StringComparison.Ordinal), log);
		Assert.IsTrue(log.Contains("Simulated unexpected failure.", StringComparison.Ordinal), log);
	}

	[TestMethod]
	public async Task Observe_CanceledTask_IsSilent()
	{
		using var logger = new RecordingLogger(LogLevel.Trace);
		var canceledTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var sentinelTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		canceledTask.SetCanceled(TestContext.CancellationToken);
		sentinelTask.SetException(new InvalidOperationException("Sentinel failure."));

		Observe(logger, canceledTask.Task, "Canceled operation");
		Observe(logger, sentinelTask.Task, "Sentinel operation");

		await WaitForLogAsync(logger, "Sentinel operation").ConfigureAwait(false);

		// Negative check: cancellation never logs; the bounded settle window is deliberate because the
		// observer's completion has no signal.
		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.DoesNotContain(
			log => log.Contains("Canceled operation", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task Observe_DisposedTask_IsSilent()
	{
		using var logger = new RecordingLogger(LogLevel.Trace);
		var disposedTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var sentinelTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		disposedTask.SetException(new ObjectDisposedException("Simulated disposed object."));
		sentinelTask.SetException(new InvalidOperationException("Sentinel failure."));

		ProviderContext context = TestProviderContext.Create(client: null, logger);

		context.Disposal.Signal();
		context.ObserveBackgroundTask(disposedTask.Task, "Disposed operation");

		Observe(logger, sentinelTask.Task, "Sentinel operation");

		await WaitForLogAsync(logger, "Sentinel operation").ConfigureAwait(false);

		// Negative check: disposal races never log; the bounded settle window is deliberate because the
		// observer's completion has no signal.
		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.DoesNotContain(
			log => log.Contains("Disposed operation", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task Observe_ADisposedObjectWhileTheProviderIsLive_LogsDebugForTheOperation()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		var faultedTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		faultedTask.SetException(new ObjectDisposedException("Simulated disposed client."));

		Observe(logger, faultedTask.Task, "Disposed client operation");

		await WaitForLogAsync(logger, "Disposed client operation").ConfigureAwait(false);

		string log = logger.Logs.Single();

		Assert.IsTrue(log.StartsWith("Debug|", StringComparison.Ordinal), log);
	}

	[TestMethod]
	public async Task Observe_SuccessfulTask_IsSilent()
	{
		using var logger = new RecordingLogger(LogLevel.Trace);
		var successfulTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var sentinelTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		successfulTask.SetResult(true);
		sentinelTask.SetException(new InvalidOperationException("Sentinel failure."));

		Observe(logger, successfulTask.Task, "Successful operation");
		Observe(logger, sentinelTask.Task, "Sentinel operation");

		await WaitForLogAsync(logger, "Sentinel operation").ConfigureAwait(false);

		// Negative check with the same bounded settle window as the canceled/disposed cases: the
		// observer's completion has no signal, so absence must be asserted after a delay.
		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.DoesNotContain(
			log => log.Contains("Successful operation", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	private static void Observe(ILogger logger, Task task, string operation) =>
		TestProviderContext.Create(client: null, logger).ObserveBackgroundTask(task, operation);

	private static async Task WaitForLogAsync(RecordingLogger logger, string operationName)
	{
		bool observed = await TestPolling
			.ForConditionAsync(() => logger.Logs.Any(log => log.Contains(operationName, StringComparison.Ordinal)), TimeSpan.FromSeconds(2))
			.ConfigureAwait(false);

		if (!observed)
			Assert.Fail($"Expected a log entry for '{operationName}'. Captured: {string.Join(Environment.NewLine, logger.Logs)}");
	}

	public TestContext? TestContext { get; set; }
}
