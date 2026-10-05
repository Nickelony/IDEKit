using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class ClientLifetimeTests
{
	private static ClientLifetime Create(ILogger? logger = null, TimeSpan? disposeWaitTimeout = null)
	{
		return new(
			disposeWaitTimeout ?? TimeSpan.FromSeconds(5),
			workspaceDisplayText: TestPaths.Root,
			logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance
		);
	}

	[TestMethod]
	public void NewLifetime_IsNotDisposedAndHasAnUncanceledToken()
	{
		ClientLifetime lifetime = Create();

		Assert.IsFalse(lifetime.IsDisposed);
		Assert.IsFalse(lifetime.Token.IsCancellationRequested);
		Assert.AreEqual(TimeSpan.FromSeconds(5), lifetime.DisposeWaitTimeout);
	}

	[TestMethod]
	public async Task BeginDisposeAsync_FirstCaller_MarksDisposedBeforeTheTeardownRunsAndRunsItOnce()
	{
		ClientLifetime lifetime = Create();
		int runs = 0;
		bool disposedDuringTeardown = false;

		await lifetime
			.BeginDisposeAsync(() =>
			{
				runs++;
				disposedDuringTeardown = lifetime.IsDisposed;
				return Task.CompletedTask;
			})
			.ConfigureAwait(false);

		Assert.AreEqual(1, runs);
		Assert.IsTrue(disposedDuringTeardown);
		Assert.IsTrue(lifetime.IsDisposed);
	}

	[TestMethod]
	public async Task BeginDisposeAsync_LaterCaller_WaitsForTheTeardownTheFirstCallerStartedAndDoesNotRunItAgain()
	{
		ClientLifetime lifetime = Create();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		int runs = 0;

		Task first = lifetime.BeginDisposeAsync(async () =>
		{
			runs++;
			await release.Task.ConfigureAwait(false);
		});

		Task second = lifetime.BeginDisposeAsync(() =>
		{
			runs++;
			return Task.CompletedTask;
		});

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.IsFalse(second.IsCompleted, "A later caller must wait for the teardown that is still running.");

		release.SetResult();
		await Task.WhenAll(first, second).WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.AreEqual(1, runs);
	}

	[TestMethod]
	public async Task BeginDisposeAsync_TeardownThrows_FaultsTheFirstCallerAndStillCompletesLaterCallers()
	{
		ClientLifetime lifetime = Create();

		Task first = lifetime.BeginDisposeAsync(static () => throw new InvalidOperationException("Simulated teardown failure."));
		Task second = lifetime.BeginDisposeAsync(static () => Task.CompletedTask);

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => first).ConfigureAwait(false);
		await second.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsTrue(lifetime.IsDisposed);
	}

	[TestMethod]
	public void Cancel_CancelsTheToken()
	{
		ClientLifetime lifetime = Create();

		lifetime.Cancel();

		Assert.IsTrue(lifetime.Token.IsCancellationRequested);
	}

	[TestMethod]
	public void Cancel_AfterRelease_DoesNotThrow()
	{
		ClientLifetime lifetime = Create();

		lifetime.Release();
		lifetime.Cancel();

		Assert.IsFalse(lifetime.Token.IsCancellationRequested);
	}

	[TestMethod]
	public void Cancel_ThrowingCancellationCallback_LogsAndDoesNotThrow()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		ClientLifetime lifetime = Create(logger);

		using CancellationTokenRegistration registration = lifetime.Token.Register(static () =>
			throw new InvalidOperationException("Simulated callback failure.")
		);

		lifetime.Cancel();

		Assert.IsTrue(lifetime.Token.IsCancellationRequested);

		Assert.Contains(
			log => log.StartsWith("Debug|", StringComparison.Ordinal) && log.Contains($"workspace '{TestPaths.Root}'", StringComparison.Ordinal),
			logger.Logs
		);
	}

	[TestMethod]
	public void GetRemainingDisposeBudget_SubtractsTheElapsedTimeAndNeverGoesBelowZero()
	{
		ClientLifetime lifetime = Create(disposeWaitTimeout: TimeSpan.FromMilliseconds(40));
		var stopwatch = Stopwatch.StartNew();

		Assert.IsLessThanOrEqualTo(TimeSpan.FromMilliseconds(40), lifetime.GetRemainingDisposeBudget(stopwatch));

		Thread.Sleep(80);

		Assert.AreEqual(TimeSpan.Zero, lifetime.GetRemainingDisposeBudget(stopwatch));
	}
}
