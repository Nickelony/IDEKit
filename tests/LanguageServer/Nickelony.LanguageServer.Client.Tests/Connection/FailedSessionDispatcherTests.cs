using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class FailedSessionDispatcherTests
{
	public TestContext TestContext { get; set; } = null!;

	private static ConnectionSession CreateSession(long generation) =>
		ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation, process: null);

	[TestMethod]
	public void QueuedDisposalTask_BeforeAnyDispatch_IsCompleted()
	{
		var dispatcher = new FailedSessionDispatcher(static _ => Task.CompletedTask, NullLogger.Instance);
		Assert.IsTrue(dispatcher.QueuedDisposalTask.IsCompletedSuccessfully);
	}

	[TestMethod]
	public async Task Dispatch_DisposesEachDispatchedSessionOnceInDispatchOrder()
	{
		var disposed = new ConcurrentQueue<long>();

		var dispatcher = new FailedSessionDispatcher(
			session =>
			{
				disposed.Enqueue(session.ConnectionGeneration);
				return Task.CompletedTask;
			},

			NullLogger.Instance
		);

		dispatcher.Dispatch(CreateSession(1), "first");
		dispatcher.Dispatch(CreateSession(2), "second");

		await dispatcher.QueuedDisposalTask.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreSequenceEqual([1, 2], [.. disposed]);
	}

	[TestMethod]
	public async Task Dispatch_WhileAnEarlierDisposalIsBlocked_ReturnsAndStartsTheLaterDisposalOnlyAfterTheEarlierOneFinishes()
	{
		var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var dispatcher = new FailedSessionDispatcher(
			session =>
			{
				if (session.ConnectionGeneration != 1)
				{
					secondStarted.TrySetResult();
					return Task.CompletedTask;
				}

				firstStarted.TrySetResult();
				return releaseFirst.Task;
			},

			NullLogger.Instance
		);

		dispatcher.Dispatch(CreateSession(1), "first");

		await firstStarted.Task.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);

		dispatcher.Dispatch(CreateSession(2), "second");

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsFalse(secondStarted.Task.IsCompleted, "The later disposal must wait for the earlier one.");
		Assert.IsFalse(dispatcher.QueuedDisposalTask.IsCompleted);

		releaseFirst.SetResult();

		await secondStarted.Task.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);
		await dispatcher.QueuedDisposalTask.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task Dispatch_WhenADisposalThrows_LogsItAndStillDisposesTheLaterSession()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		var disposed = new ConcurrentQueue<long>();

		var dispatcher = new FailedSessionDispatcher(
			session =>
			{
				disposed.Enqueue(session.ConnectionGeneration);
				return session.ConnectionGeneration == 1 ? throw new InvalidOperationException("The disposal failed.") : Task.CompletedTask;
			},

			logger
		);

		dispatcher.Dispatch(CreateSession(1), "first failure");
		Task firstDisposal = dispatcher.QueuedDisposalTask;
		dispatcher.Dispatch(CreateSession(2), "second failure");

		await firstDisposal.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);
		await dispatcher.QueuedDisposalTask.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreSequenceEqual([1, 2], [.. disposed]);

		Assert.Contains(
			static log =>
				log.StartsWith("Debug|", StringComparison.Ordinal)
				&& log.Contains("Failed to dispose detached", StringComparison.Ordinal)
				&& log.Contains("first failure", StringComparison.Ordinal),

			logger.Logs
		);
	}
}
