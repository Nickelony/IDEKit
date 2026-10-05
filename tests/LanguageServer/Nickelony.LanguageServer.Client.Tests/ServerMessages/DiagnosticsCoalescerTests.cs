using System.Collections.Concurrent;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class DiagnosticsCoalescerTests
{
	private const string FirstDocument = "untitled:First";
	private const string SecondDocument = "untitled:Second";

	[TestMethod]
	public async Task Queue_BeforeTheCoalescerRuns_ForwardsOnlyTheNewestPayloadOfEachDocument()
	{
		var forwarded = new ConcurrentQueue<QueuedDiagnostics>();
		var coalescer = new DiagnosticsCoalescer(static _ => true, (_, queued) => forwarded.Enqueue(queued), CancellationToken.None);

		coalescer.Queue(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(FirstDocument, "first, replaced"));
		coalescer.Queue(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(SecondDocument, "second"));
		coalescer.Queue(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(FirstDocument, "first, newest"));

		await RunUntilIdleAsync(coalescer).ConfigureAwait(false);

		Assert.AreEqual("first, newest|second", Messages(forwarded));
	}

	[TestMethod]
	public async Task Queue_KeepsOneSlotPerGeneration_AndDropsThePayloadOfAGenerationThatNoLongerAcceptsServerMessages()
	{
		var forwarded = new ConcurrentQueue<QueuedDiagnostics>();

		var coalescer = new DiagnosticsCoalescer(
			static generation => generation == 2,
			(_, queued) => forwarded.Enqueue(queued),
			CancellationToken.None
		);

		// The stale payload arrives last and must not replace the current one.
		coalescer.Queue(connectionGeneration: 2, ServerMessageTestHelpers.CreateDiagnostics(FirstDocument, "current"));
		coalescer.Queue(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(FirstDocument, "stale"));

		await RunUntilIdleAsync(coalescer).ConfigureAwait(false);

		QueuedDiagnostics only = Assert.ContainsSingle(forwarded);

		Assert.AreEqual(2, only.ConnectionGeneration);
		Assert.AreEqual("current", ServerMessageTestHelpers.FirstMessage(only.Parameters));
	}

	[TestMethod]
	public async Task Queue_WithoutAUri_ForwardsEveryPayloadBecauseNoDocumentCoalescesThem()
	{
		var forwarded = new ConcurrentQueue<QueuedDiagnostics>();
		var coalescer = new DiagnosticsCoalescer(static _ => true, (_, queued) => forwarded.Enqueue(queued), CancellationToken.None);

		coalescer.Queue(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(uri: null, "one"));
		coalescer.Queue(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(uri: "  ", "two"));
		coalescer.Queue(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(uri: "  ", "three"));

		await RunUntilIdleAsync(coalescer).ConfigureAwait(false);

		Assert.AreEqual("one|three|two", Messages(forwarded));
	}

	[TestMethod]
	public async Task Queue_DetachesThePayloadFromTheDiagnosticsOfTheCaller()
	{
		var forwarded = new ConcurrentQueue<QueuedDiagnostics>();
		var coalescer = new DiagnosticsCoalescer(static _ => true, (_, queued) => forwarded.Enqueue(queued), CancellationToken.None);

		var callerDiagnostics = new List<DiagnosticPayload> { new(Range: null, Severity: null, Message: "kept", Source: "test", Code: null) };

		coalescer.Queue(connectionGeneration: 1, new PublishDiagnosticsParams(FirstDocument, Version: null, callerDiagnostics));

		callerDiagnostics.Clear();

		await RunUntilIdleAsync(coalescer).ConfigureAwait(false);

		Assert.AreEqual("kept", Messages(forwarded));
	}

	[TestMethod]
	public async Task Queue_AfterComplete_DropsThePayload()
	{
		var forwarded = new ConcurrentQueue<QueuedDiagnostics>();
		var coalescer = new DiagnosticsCoalescer(static _ => true, (_, queued) => forwarded.Enqueue(queued), CancellationToken.None);

		coalescer.Complete();
		coalescer.Queue(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(FirstDocument, "late"));

		Assert.IsTrue(coalescer.IsIdle);

		await Task.Run(coalescer.RunAsync).WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsEmpty(forwarded);
	}

	[TestMethod]
	public async Task Complete_DropsThePayloadsThatAreStillQueued()
	{
		var forwarded = new ConcurrentQueue<QueuedDiagnostics>();
		var coalescer = new DiagnosticsCoalescer(static _ => true, (_, queued) => forwarded.Enqueue(queued), CancellationToken.None);

		coalescer.Queue(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(FirstDocument, "queued"));

		Assert.IsFalse(coalescer.IsIdle);

		coalescer.Complete();

		Assert.IsTrue(coalescer.IsIdle);

		await Task.Run(coalescer.RunAsync).WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsEmpty(forwarded);
	}

	[TestMethod]
	public async Task RunAsync_EndsWhenTheLifetimeIsCanceled()
	{
		using var lifetime = new CancellationTokenSource();

		var coalescer = new DiagnosticsCoalescer(static _ => true, static (_, _) => { }, lifetime.Token);

		var loop = Task.Run(coalescer.RunAsync);

		await lifetime.CancelAsync().ConfigureAwait(false);
		await loop.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsTrue(loop.IsCompletedSuccessfully);
	}

	[TestMethod]
	public async Task IsIdle_IsFalseWhileAPayloadIsBeingForwarded()
	{
		var forwardEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseForward = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var coalescer = new DiagnosticsCoalescer(
			static _ => true,

			(_, _) =>
			{
				forwardEntered.TrySetResult();
				releaseForward.Task.GetAwaiter().GetResult();
			},

			CancellationToken.None
		);

		coalescer.Queue(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(FirstDocument, "forwarded"));

		var loop = Task.Run(coalescer.RunAsync);

		try
		{
			await forwardEntered.Task.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

			// The payload has left the pending set, so only the in-flight pass keeps the coalescer from being idle.
			Assert.IsFalse(coalescer.IsIdle);
		}
		finally
		{
			releaseForward.TrySetResult();
		}

		await TestPolling.UntilAsync(() => coalescer.IsIdle, TestPolling.DefaultTimeout).ConfigureAwait(false);

		coalescer.Complete();

		await loop.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task RunAsync_WhenForwardingThrows_FaultsTheCoalescerAndLeavesItIdle()
	{
		var coalescer = new DiagnosticsCoalescer(
			static _ => true,
			static (_, _) => throw new InvalidOperationException("Simulated forward failure."),
			CancellationToken.None
		);

		coalescer.Queue(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics(FirstDocument, "forwarded"));

		var loop = Task.Run(coalescer.RunAsync);

		await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(async () => await loop.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.IsTrue(coalescer.IsIdle);
	}

	[TestMethod]
	public async Task Queue_WhilePassesRun_KeepsTheLoopAliveAndForwardsEveryDocument()
	{
		const int DocumentCount = 64;
		const int ForwardedTarget = 5000;

		var forwardedDocuments = new ConcurrentDictionary<string, byte>();
		int forwardedCount = 0;

		var coalescer = new DiagnosticsCoalescer(
			static _ => true,

			(key, _) =>
			{
				forwardedDocuments[key.Value] = 0;
				Interlocked.Increment(ref forwardedCount);
			},

			CancellationToken.None
		);

		var loop = Task.Run(coalescer.RunAsync);
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();

		// The payloads arrive from another thread for as long as the loop forwards them, so passes and queue calls overlap many times. A pass
		// that throws ends the loop, which stops the writer and fails the assertions below.
		await Task.Run(() =>
			{
				for (
					int next = 0;
					!loop.IsCompleted && Volatile.Read(ref forwardedCount) < ForwardedTarget && stopwatch.Elapsed < TestPolling.DefaultTimeout;
					next++
				)
				{
					coalescer.Queue(
						connectionGeneration: 1,
						ServerMessageTestHelpers.CreateDiagnostics("untitled:Document" + (next % DocumentCount), "warning")
					);
				}
			})
			.ConfigureAwait(false);

		await TestPolling.UntilAsync(() => loop.IsCompleted || coalescer.IsIdle, TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsFalse(loop.IsCompleted, "The coalescer loop ended while the client was still running: " + loop.Exception);

		coalescer.Complete();
		await loop.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.HasCount(DocumentCount, forwardedDocuments);
	}

	private static async Task RunUntilIdleAsync(DiagnosticsCoalescer coalescer)
	{
		var loop = Task.Run(coalescer.RunAsync);

		await TestPolling.UntilAsync(() => coalescer.IsIdle, TestPolling.DefaultTimeout).ConfigureAwait(false);

		coalescer.Complete();

		await loop.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);
	}

	private static string Messages(IEnumerable<QueuedDiagnostics> forwarded)
	{
		return string.Join(
			"|",
			forwarded.Select(queued => ServerMessageTestHelpers.FirstMessage(queued.Parameters)).OrderBy(message => message, StringComparer.Ordinal)
		);
	}
}
