using System.Collections.Concurrent;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class ServerMessageDispatcherTests
{
	private static readonly DiagnosticsDocumentKey s_firstDocument = DiagnosticsDocumentKey.FromUri("untitled:First");
	private static readonly DiagnosticsDocumentKey s_secondDocument = DiagnosticsDocumentKey.FromUri("untitled:Second");

	[TestMethod]
	public async Task Run_DeliversTheSemanticTokensRefreshThenTheDiagnosticRefreshThenTheDiagnostics()
	{
		var callbacks = new RecordingCallbacks();
		ServerMessageDispatcher dispatcher = callbacks.CreateDispatcher();

		dispatcher.QueueDiagnostics(s_firstDocument, Queued(1, "diagnostics"));
		dispatcher.QueueDiagnosticRefreshRequested();
		dispatcher.QueueSemanticTokensRefreshRequested();

		await RunUntilIdleAsync(dispatcher).ConfigureAwait(false);

		Assert.AreSequenceEqual(["semantic", "refresh", "diagnostics:untitled:First:diagnostics"], [.. callbacks.Events]);
	}

	[TestMethod]
	public async Task Queue_CollapsesRepeatedRefreshRequestsIntoOneDeliveryEach()
	{
		var callbacks = new RecordingCallbacks();
		ServerMessageDispatcher dispatcher = callbacks.CreateDispatcher();

		for (int i = 0; i < 3; i++)
		{
			dispatcher.QueueSemanticTokensRefreshRequested();
			dispatcher.QueueDiagnosticRefreshRequested();
		}

		await RunUntilIdleAsync(dispatcher).ConfigureAwait(false);

		Assert.AreSequenceEqual(["semantic", "refresh"], [.. callbacks.Events]);
	}

	[TestMethod]
	public async Task Queue_AfterTheFlagWasDelivered_DeliversTheNextRequest()
	{
		var callbacks = new RecordingCallbacks();
		ServerMessageDispatcher dispatcher = callbacks.CreateDispatcher();

		var loop = Task.Run(dispatcher.RunAsync);

		dispatcher.QueueSemanticTokensRefreshRequested();

		await TestPolling.UntilAsync(() => callbacks.Events.Count == 1 && dispatcher.IsIdle, TestPolling.DefaultTimeout).ConfigureAwait(false);

		dispatcher.QueueSemanticTokensRefreshRequested();

		await TestPolling.UntilAsync(() => callbacks.Events.Count == 2 && dispatcher.IsIdle, TestPolling.DefaultTimeout).ConfigureAwait(false);

		dispatcher.Complete();

		await loop.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task QueueDiagnostics_KeepsTheNewestPayloadOfEachDocument()
	{
		var callbacks = new RecordingCallbacks();

		ServerMessageDispatcher dispatcher = callbacks.CreateDispatcher();

		dispatcher.QueueDiagnostics(s_firstDocument, Queued(1, "first, replaced"));
		dispatcher.QueueDiagnostics(s_secondDocument, Queued(1, "second"));
		dispatcher.QueueDiagnostics(s_firstDocument, Queued(1, "first, newest"));

		await RunUntilIdleAsync(dispatcher).ConfigureAwait(false);

		Assert.AreSequenceEqual(
			["diagnostics:untitled:First:first, newest", "diagnostics:untitled:Second:second"],
			[.. callbacks.Events],
			Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder
		);
	}

	[TestMethod]
	public async Task Run_DropsThePayloadOfAGenerationThatNoLongerAcceptsServerMessages()
	{
		var callbacks = new RecordingCallbacks();

		ServerMessageDispatcher dispatcher = callbacks.CreateDispatcher(static generation => generation == 2);

		dispatcher.QueueDiagnostics(s_firstDocument, Queued(1, "stale"));
		dispatcher.QueueDiagnostics(s_secondDocument, Queued(2, "current"));

		await RunUntilIdleAsync(dispatcher).ConfigureAwait(false);

		Assert.AreSequenceEqual(["diagnostics:untitled:Second:current"], [.. callbacks.Events]);
	}

	[TestMethod]
	public async Task Queue_AfterComplete_DropsEveryKindOfServerMessage()
	{
		var callbacks = new RecordingCallbacks();
		ServerMessageDispatcher dispatcher = callbacks.CreateDispatcher();

		dispatcher.Complete();
		dispatcher.QueueSemanticTokensRefreshRequested();
		dispatcher.QueueDiagnosticRefreshRequested();
		dispatcher.QueueDiagnostics(s_firstDocument, Queued(1, "late"));

		Assert.IsTrue(dispatcher.IsIdle);

		await Task.Run(dispatcher.RunAsync).WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsEmpty(callbacks.Events);
	}

	[TestMethod]
	public async Task Complete_DropsTheCallbacksThatAreStillQueued()
	{
		var callbacks = new RecordingCallbacks();
		ServerMessageDispatcher dispatcher = callbacks.CreateDispatcher();

		dispatcher.QueueSemanticTokensRefreshRequested();
		dispatcher.QueueDiagnosticRefreshRequested();
		dispatcher.QueueDiagnostics(s_firstDocument, Queued(1, "queued"));

		Assert.IsFalse(dispatcher.IsIdle);

		dispatcher.Complete();

		Assert.IsTrue(dispatcher.IsIdle);

		await Task.Run(dispatcher.RunAsync).WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsEmpty(callbacks.Events);
	}

	[TestMethod]
	public async Task RunAsync_EndsWhenTheLifetimeIsCanceled()
	{
		using var lifetime = new CancellationTokenSource();

		var callbacks = new RecordingCallbacks();
		ServerMessageDispatcher dispatcher = callbacks.CreateDispatcher(lifetimeToken: lifetime.Token);

		var loop = Task.Run(dispatcher.RunAsync);

		await lifetime.CancelAsync().ConfigureAwait(false);
		await loop.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsTrue(loop.IsCompletedSuccessfully);
	}

	[TestMethod]
	public async Task IsIdle_IsFalseWhileACallbackIsBeingDelivered()
	{
		var deliveryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseDelivery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var dispatcher = new ServerMessageDispatcher(
			canAcceptServerMessagesForGeneration: static _ => true,

			invokeSemanticTokensRefreshRequested: () =>
			{
				deliveryEntered.TrySetResult();
				releaseDelivery.Task.GetAwaiter().GetResult();
			},

			invokeDiagnosticRefreshRequested: static () => { },
			invokeDiagnosticsPublished: static (_, _) => { },
			lifetimeToken: CancellationToken.None
		);

		dispatcher.QueueSemanticTokensRefreshRequested();

		var loop = Task.Run(dispatcher.RunAsync);

		try
		{
			await deliveryEntered.Task.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

			// The flag has been taken, so only the round in flight keeps the dispatcher from being idle.
			Assert.IsFalse(dispatcher.IsIdle);
		}
		finally
		{
			releaseDelivery.TrySetResult();
		}

		await TestPolling.UntilAsync(() => dispatcher.IsIdle, TestPolling.DefaultTimeout).ConfigureAwait(false);

		dispatcher.Complete();

		await loop.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task RunAsync_WhenADeliveryThrows_FaultsTheDispatcherAndLeavesItIdle()
	{
		var dispatcher = new ServerMessageDispatcher(
			canAcceptServerMessagesForGeneration: static _ => true,
			invokeSemanticTokensRefreshRequested: static () => throw new InvalidOperationException("Simulated delivery failure."),
			invokeDiagnosticRefreshRequested: static () => { },
			invokeDiagnosticsPublished: static (_, _) => { },
			lifetimeToken: CancellationToken.None
		);

		dispatcher.QueueSemanticTokensRefreshRequested();

		var loop = Task.Run(dispatcher.RunAsync);

		await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(async () => await loop.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.IsTrue(dispatcher.IsIdle);
	}

	private static QueuedDiagnostics Queued(long connectionGeneration, string message) =>
		new(connectionGeneration, ServerMessageTestHelpers.CreateDiagnostics("untitled:Ignored", message));

	[TestMethod]
	public async Task QueueDiagnostics_WhileRoundsRun_KeepsTheLoopAliveAndDeliversEveryDocument()
	{
		const int DocumentCount = 64;
		const int DeliveredTarget = 5000;

		var deliveredDocuments = new ConcurrentDictionary<string, byte>();
		int deliveredCount = 0;

		var dispatcher = new ServerMessageDispatcher(
			canAcceptServerMessagesForGeneration: static _ => true,
			invokeSemanticTokensRefreshRequested: static () => { },
			invokeDiagnosticRefreshRequested: static () => { },

			invokeDiagnosticsPublished: (key, _) =>
			{
				deliveredDocuments[key.Value] = 0;
				Interlocked.Increment(ref deliveredCount);
			},

			lifetimeToken: CancellationToken.None
		);

		DiagnosticsDocumentKey[] documents =
		[
			.. Enumerable.Range(0, DocumentCount).Select(index => DiagnosticsDocumentKey.FromUri("untitled:Document" + index)),
		];

		QueuedDiagnostics queued = Queued(1, "warning");

		var loop = Task.Run(dispatcher.RunAsync);
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();

		// The payloads arrive from another thread for as long as the loop delivers them, so rounds and queue calls overlap many times. A round
		// that throws ends the loop, which stops the writer and fails the assertions below.
		await Task.Run(() =>
			{
				for (
					int next = 0;
					!loop.IsCompleted && Volatile.Read(ref deliveredCount) < DeliveredTarget && stopwatch.Elapsed < TestPolling.DefaultTimeout;
					next++
				)
				{
					dispatcher.QueueDiagnostics(documents[next % DocumentCount], queued);
				}
			})
			.ConfigureAwait(false);

		await TestPolling.UntilAsync(() => loop.IsCompleted || dispatcher.IsIdle, TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsFalse(loop.IsCompleted, "The dispatcher loop ended while the client was still running: " + loop.Exception);

		dispatcher.Complete();
		await loop.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.HasCount(DocumentCount, deliveredDocuments);
	}

	private static async Task RunUntilIdleAsync(ServerMessageDispatcher dispatcher)
	{
		var loop = Task.Run(dispatcher.RunAsync);

		await TestPolling.UntilAsync(() => dispatcher.IsIdle, TestPolling.DefaultTimeout).ConfigureAwait(false);

		dispatcher.Complete();

		await loop.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);
	}

	private sealed class RecordingCallbacks
	{
		internal ConcurrentQueue<string> Events { get; } = new();

		internal ServerMessageDispatcher CreateDispatcher(
			Func<long, bool>? canAcceptServerMessagesForGeneration = null,
			CancellationToken lifetimeToken = default
		)
		{
			return new ServerMessageDispatcher(
				canAcceptServerMessagesForGeneration: canAcceptServerMessagesForGeneration ?? (static _ => true),
				invokeSemanticTokensRefreshRequested: () => Events.Enqueue("semantic"),
				invokeDiagnosticRefreshRequested: () => Events.Enqueue("refresh"),

				invokeDiagnosticsPublished: (documentKey, parameters) =>
					Events.Enqueue("diagnostics:" + documentKey.Value + ":" + ServerMessageTestHelpers.FirstMessage(parameters)),

				lifetimeToken
			);
		}
	}
}
