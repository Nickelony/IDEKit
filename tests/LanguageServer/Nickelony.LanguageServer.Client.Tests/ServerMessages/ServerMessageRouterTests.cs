using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class ServerMessageRouterTests
{
	private const string FirstDocument = "untitled:First";
	private const string SecondDocument = "untitled:Second";

	private readonly object _eventSender = new();

	[TestMethod]
	public async Task QueueDiagnosticsPublished_DeliversTheNewestPayloadToEverySubscriber()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		ServerMessageRouter router = CreateRouter(logger);

		var firstSubscriber = new ConcurrentQueue<string?>();
		var secondSubscriber = new ConcurrentQueue<string?>();
		var senders = new ConcurrentQueue<object?>();

		router.AddDiagnosticsSubscriber(
			(sender, args) =>
			{
				senders.Enqueue(sender);
				firstSubscriber.Enqueue(ServerMessageTestHelpers.FirstMessage(args.Parameters));
			}
		);

		router.AddDiagnosticsSubscriber((_, args) => secondSubscriber.Enqueue(ServerMessageTestHelpers.FirstMessage(args.Parameters)));
		router.EnsureServerMessageAndDiagnosticsLoopsRunning();

		router.QueueDiagnosticsPublished(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(FirstDocument, "first"));
		router.QueueDiagnosticsPublished(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(FirstDocument, "newest"));

		await TestPolling.UntilAsync(() => router.IsDrained, TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.AreEqual("newest", firstSubscriber.Last());
		Assert.AreEqual("newest", secondSubscriber.Last());
		Assert.IsTrue(senders.All(sender => ReferenceEquals(sender, _eventSender)));

		await CompleteAsync(router).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task QueueDiagnosticsPublished_DropsThePayloadOfAGenerationThatNoLongerAcceptsServerMessages()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		ServerMessageRouter router = CreateRouter(logger, canAcceptServerMessagesForGeneration: static generation => generation == 2);

		var received = new ConcurrentQueue<string?>();

		router.AddDiagnosticsSubscriber((_, args) => received.Enqueue(ServerMessageTestHelpers.FirstMessage(args.Parameters)));
		router.EnsureServerMessageAndDiagnosticsLoopsRunning();

		router.QueueDiagnosticsPublished(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(FirstDocument, "stale"));
		router.QueueDiagnosticsPublished(connectionGeneration: 2, ServerMessageTestHelpers.CreateDiagnostics(SecondDocument, "current"));

		await TestPolling.UntilAsync(() => router.IsDrained, TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.AreEqual("current", Assert.ContainsSingle(received));

		await CompleteAsync(router).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ASubscriberThatThrows_IsLoggedAndDoesNotStopTheLaterSubscribersOrTheLaterPayloads()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		ServerMessageRouter router = CreateRouter(logger);

		var received = new ConcurrentQueue<string?>();

		router.AddDiagnosticsSubscriber(static (_, _) => throw new InvalidOperationException("Simulated subscriber failure."));
		router.AddDiagnosticsSubscriber((_, args) => received.Enqueue(ServerMessageTestHelpers.FirstMessage(args.Parameters)));

		router.EnsureServerMessageAndDiagnosticsLoopsRunning();

		router.QueueDiagnosticsPublished(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(FirstDocument, "one"));

		await TestPolling.UntilAsync(() => router.IsDrained && received.Count == 1, TestPolling.DefaultTimeout).ConfigureAwait(false);

		router.QueueDiagnosticsPublished(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(FirstDocument, "two"));

		await TestPolling.UntilAsync(() => router.IsDrained && received.Count == 2, TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.AreEqual("one|two", string.Join("|", received));

		Assert.Contains(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("Diagnostics handler threw")
				&& log.Contains("Simulated subscriber failure."),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		Assert.IsFalse(router.DiagnosticsCoalescerTask.IsCompleted);
		Assert.IsFalse(router.ServerMessageDispatcherTask.IsCompleted);

		await CompleteAsync(router).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task RefreshRequests_ReachOnlyTheSubscribersOfTheirKind_EvenWhenOneSubscriberThrows()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		ServerMessageRouter router = CreateRouter(logger);

		int semanticTokensRefreshes = 0;
		int diagnosticRefreshes = 0;

		router.AddSemanticTokensRefreshSubscriber(static (_, _) => throw new InvalidOperationException("Simulated subscriber failure."));
		router.AddSemanticTokensRefreshSubscriber((_, _) => Interlocked.Increment(ref semanticTokensRefreshes));

		router.AddDiagnosticRefreshSubscriber((_, _) => Interlocked.Increment(ref diagnosticRefreshes));

		router.EnsureServerMessageDispatcherRunning();

		router.QueueSemanticTokensRefreshRequested();

		await TestPolling
			.UntilAsync(() => router.IsDrained && Volatile.Read(ref semanticTokensRefreshes) == 1, TestPolling.DefaultTimeout)
			.ConfigureAwait(false);

		Assert.AreEqual(0, Volatile.Read(ref diagnosticRefreshes));

		router.QueueDiagnosticRefreshRequested();

		await TestPolling
			.UntilAsync(() => router.IsDrained && Volatile.Read(ref diagnosticRefreshes) == 1, TestPolling.DefaultTimeout)
			.ConfigureAwait(false);

		Assert.AreEqual(1, Volatile.Read(ref semanticTokensRefreshes));

		Assert.Contains(
			log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("Semantic tokens refresh request handler threw"),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		await CompleteAsync(router).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task IsDrained_IsFalseWhileASubscriberRunsAndTrueAfterItReturns()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		ServerMessageRouter router = CreateRouter(logger);

		var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		router.AddDiagnosticsSubscriber(
			(_, _) =>
			{
				handlerEntered.TrySetResult();
				releaseHandler.Task.GetAwaiter().GetResult();
			}
		);

		try
		{
			router.EnsureServerMessageAndDiagnosticsLoopsRunning();
			router.QueueDiagnosticsPublished(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(FirstDocument, "blocked"));

			await handlerEntered.Task.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

			Assert.IsFalse(router.IsDrained);
		}
		finally
		{
			releaseHandler.TrySetResult();
		}

		await TestPolling.UntilAsync(() => router.IsDrained, TestPolling.DefaultTimeout).ConfigureAwait(false);
		await CompleteAsync(router).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task Completion_DropsTheQueuedPayloadsAndLetsBothLoopsEnd()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		ServerMessageRouter router = CreateRouter(logger);

		var received = new ConcurrentQueue<string?>();
		var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		router.AddDiagnosticsSubscriber(
			(_, args) =>
			{
				received.Enqueue(ServerMessageTestHelpers.FirstMessage(args.Parameters));
				handlerEntered.TrySetResult();
				releaseHandler.Task.GetAwaiter().GetResult();
			}
		);

		try
		{
			router.EnsureServerMessageAndDiagnosticsLoopsRunning();
			router.QueueDiagnosticsPublished(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(FirstDocument, "started"));

			await handlerEntered.Task.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

			// The second payload travels through both loops and waits in the subscriber, behind the handler that is still running.
			router.QueueDiagnosticsPublished(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(SecondDocument, "queued"));

			await TestPolling
				.UntilAsync(() => router.DiagnosticsCoalescer.IsIdle && router.ServerMessageDispatcher.IsIdle, TestPolling.DefaultTimeout)
				.ConfigureAwait(false);

			router.CompleteSignalChannels();
			router.CompleteSubscribers();
			router.QueueDiagnosticsPublished(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(SecondDocument, "late"));
			router.QueueSemanticTokensRefreshRequested();
			router.QueueDiagnosticRefreshRequested();
		}
		finally
		{
			releaseHandler.TrySetResult();
		}

		await Task.WhenAll(router.DiagnosticsCoalescerTask, router.ServerMessageDispatcherTask)
			.WaitAsync(TestPolling.DefaultTimeout)
			.ConfigureAwait(false);

		// A completed set stops reporting its subscription, so the router cannot say when a dropped payload would have been delivered:
		// the handler that finished after the release would deliver it within the window.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.AreEqual("started", Assert.ContainsSingle(received));
	}

	[TestMethod]
	public async Task EnsureServerMessageDispatcherRunning_WhenTheDispatcherTaskFaulted_StartsANewDispatcher()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		ServerMessageRouter router = CreateRouter(logger);

		var faultedLoop = Task.FromException(new IOException("Simulated server-message dispatcher failure."));

		router.ServerMessageDispatcherTask = faultedLoop;
		router.EnsureServerMessageDispatcherRunning();

		Assert.AreNotSame(faultedLoop, router.ServerMessageDispatcherTask);

		Assert.IsFalse(
			router.ServerMessageDispatcherTask.IsCompleted,
			"The restart must recreate the server-message dispatcher instead of keeping the faulted task."
		);

		await CompleteAsync(router).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task EnsureServerMessageAndDiagnosticsLoopsRunning_ForgetsTheRecordedEndOfEachReplacedLoop()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		BackgroundTaskObserver observer = CreateObserver(logger);
		ServerMessageRouter router = CreateRouter(logger, observer);

		var faultedServerMessageDispatcher = Task.FromException(new IOException("Simulated server-message dispatcher failure."));
		var faultedDiagnosticsCoalescer = Task.FromException(new IOException("Simulated diagnostics coalescer failure."));

		router.ServerMessageDispatcherTask = faultedServerMessageDispatcher;
		router.DiagnosticsCoalescerTask = faultedDiagnosticsCoalescer;

		observer.ObserveLoop(faultedServerMessageDispatcher, "server-message dispatcher");
		observer.ObserveLoop(faultedDiagnosticsCoalescer, "diagnostics coalescer");

		await TestPolling
			.UntilAsync(
				() =>
					observer.WasLoopTerminationLogged(faultedServerMessageDispatcher)
					&& observer.WasLoopTerminationLogged(faultedDiagnosticsCoalescer),

				TestPolling.DefaultTimeout,
				"The observer should record the end of both loops."
			)
			.ConfigureAwait(false);

		router.EnsureServerMessageAndDiagnosticsLoopsRunning();

		Assert.IsFalse(observer.WasLoopTerminationLogged(faultedServerMessageDispatcher));
		Assert.IsFalse(observer.WasLoopTerminationLogged(faultedDiagnosticsCoalescer));

		await CompleteAsync(router).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task EnsureServerMessageAndDiagnosticsLoopsRunning_RestartsLoopsThatEndedWhileTheClientIsActive()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		int unhealthyMarks = 0;

		ServerMessageRouter router = CreateRouter(
			logger,
			CreateObserver(logger, markConnectionUnhealthy: () => Interlocked.Increment(ref unhealthyMarks))
		);

		router.EnsureServerMessageAndDiagnosticsLoopsRunning();

		Task firstServerMessageDispatcherTask = router.ServerMessageDispatcherTask;
		Task firstDiagnosticsCoalescerTask = router.DiagnosticsCoalescerTask;

		Assert.IsFalse(firstServerMessageDispatcherTask.IsCompleted);
		Assert.IsFalse(firstDiagnosticsCoalescerTask.IsCompleted);

		// Completing the wake signals ends both loops without the client being disposed, as an unexpected end does.
		router.CompleteSignalChannels();

		await Task.WhenAll(firstServerMessageDispatcherTask, firstDiagnosticsCoalescerTask)
			.WaitAsync(TestPolling.DefaultTimeout)
			.ConfigureAwait(false);

		// The observer reports from a continuation of each loop task, and that continuation can run after the awaiting test resumed.
		await TestPolling
			.UntilAsync(
				() =>
					Volatile.Read(ref unhealthyMarks) == 2
					&& logger.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("'server-message dispatcher'"))
					&& logger.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("'diagnostics coalescer'")),

				TestPolling.DefaultTimeout,
				string.Join(Environment.NewLine, logger.Logs)
			)
			.ConfigureAwait(false);

		router.EnsureServerMessageAndDiagnosticsLoopsRunning();

		Assert.AreNotSame(firstServerMessageDispatcherTask, router.ServerMessageDispatcherTask);
		Assert.AreNotSame(firstDiagnosticsCoalescerTask, router.DiagnosticsCoalescerTask);

		await Task.WhenAll(router.ServerMessageDispatcherTask, router.DiagnosticsCoalescerTask)
			.WaitAsync(TestPolling.DefaultTimeout)
			.ConfigureAwait(false);
	}

	private static async Task CompleteAsync(ServerMessageRouter router)
	{
		router.CompleteSignalChannels();
		router.CompleteSubscribers();

		await Task.WhenAll(router.DiagnosticsCoalescerTask, router.ServerMessageDispatcherTask)
			.WaitAsync(TestPolling.DefaultTimeout)
			.ConfigureAwait(false);
	}

	private static BackgroundTaskObserver CreateObserver(RecordingLogger logger, Action? markConnectionUnhealthy = null)
	{
		return new BackgroundTaskObserver(
			new ClientLifetime(TimeSpan.FromSeconds(5), workspaceDisplayText: TestPaths.Root, logger),
			markConnectionUnhealthy: markConnectionUnhealthy ?? (static () => { }),
			logger
		);
	}

	private ServerMessageRouter CreateRouter(
		RecordingLogger logger,
		BackgroundTaskObserver? backgroundTaskObserver = null,
		Func<long, bool>? canAcceptServerMessagesForGeneration = null
	)
	{
		return new ServerMessageRouter(
			eventSender: _eventSender,
			canAcceptServerMessagesForGeneration: canAcceptServerMessagesForGeneration ?? (static _ => true),
			testHooks: LanguageServerClientTestHooks.None,
			backgroundTaskObserver: backgroundTaskObserver ?? CreateObserver(logger),
			logger,
			lifetimeToken: CancellationToken.None
		);
	}
}
