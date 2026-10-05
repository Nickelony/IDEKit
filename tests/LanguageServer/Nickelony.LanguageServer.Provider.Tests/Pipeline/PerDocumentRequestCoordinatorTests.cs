using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the per-document request runner's supersession, failure classification and bounded fan-out directly.</summary>
[TestClass]
public sealed class PerDocumentRequestCoordinatorTests
{
	private sealed class TestHost
	{
		internal TestHost(ILogger? logger = null, int maxConcurrentRequests = 4)
		{
			Runner = new(
				Documents,
				Disposal,
				maxConcurrentRequests,
				requestKind: "widget",
				cachedNoun: "widgets",
				providerDisplayName: "Test",
				options: LanguageServerFeatureProviderOptions.Default,
				logger: logger ?? NullLogger.Instance
			);
		}

		internal DocumentTracker Documents { get; } = new();

		internal ProviderLifetime Disposal { get; } = new();

		internal PerDocumentRequestCoordinator Runner { get; }

		internal DocumentSnapshot Open(string fileName)
		{
			string filePath = TestPaths.Script(fileName);

			Documents.Synchronize(filePath, "content", leases: DocumentLeaseKinds.Open);

			return Documents.GetDocumentSnapshot(filePath) ?? throw new InvalidOperationException("The document was not tracked.");
		}
	}

	[TestMethod]
	public async Task RunAsync_PassesAFreshTokenThatCancelCancels()
	{
		var fixture = new TestHost();

		DocumentSnapshot document = fixture.Open("a.lua");

		var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Task run = fixture.Runner.RunAsync(
			document,

			async token =>
			{
				started.SetResult(token);
				await release.Task.ConfigureAwait(false);
				return true;
			},

			CancellationToken.None
		);

		CancellationToken observed = await started.Task.ConfigureAwait(false);

		Assert.IsFalse(observed.IsCancellationRequested);

		fixture.Runner.Cancel(document.FilePath);

		Assert.IsTrue(observed.IsCancellationRequested);

		release.SetResult();
		await run.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task RunAsync_ForTheSamePath_CancelsThePreviousRequestAndBothSettleQuietly()
	{
		var fixture = new TestHost();
		DocumentSnapshot document = fixture.Open("a.lua");

		var firstToken = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);

		Task first = fixture.Runner.RunAsync(
			document,

			async token =>
			{
				firstToken.SetResult(token);
				await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
				return true;
			},

			CancellationToken.None
		);

		CancellationToken observed = await firstToken.Task.ConfigureAwait(false);

		await fixture.Runner.RunAsync(document, static _ => Task.FromResult(true), CancellationToken.None).ConfigureAwait(false);
		await first.ConfigureAwait(false);

		Assert.IsTrue(observed.IsCancellationRequested);
	}

	[TestMethod]
	public async Task RunAsync_WhenTheCallerCancels_PropagatesTheCancellation()
	{
		var fixture = new TestHost();
		DocumentSnapshot document = fixture.Open("a.lua");

		using var callerSource = new CancellationTokenSource();

		callerSource.Cancel();

		await Assert
			.ThrowsAsync<OperationCanceledException>(() =>
				fixture.Runner.RunAsync(
					document,

					static async token =>
					{
						await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
						return true;
					},

					callerSource.Token
				)
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task RunAsync_WhenTheRequestThrows_LogsAWarningNamingTheRequest()
	{
		using var logger = new RecordingLogger();
		var fixture = new TestHost(logger);

		DocumentSnapshot document = fixture.Open("a.lua");

		await fixture
			.Runner.RunAsync(document, static _ => throw new InvalidOperationException("request failure"), CancellationToken.None)
			.ConfigureAwait(false);

		RecordingLogger.LogEntry entry = logger.Entries.Single();

		Assert.AreEqual(LogLevel.Warning, entry.Level);
		Assert.Contains("Test widget request failed", entry.Message);
		Assert.Contains("keeping the previously cached widgets", entry.Message);
	}

	[TestMethod]
	[DataRow(typeof(IOException))]
	[DataRow(typeof(LanguageServerConnectionUnavailableException))]
	public async Task RunAsync_WhenTheConnectionFailsBehindTheRequestRunner_SettlesWithoutALog(Type failure)
	{
		using var logger = new RecordingLogger();
		var fixture = new TestHost(logger);

		var client = new FakeLanguageServerClient { SendRequestHandler = (_, _, _) => throw (Exception)Activator.CreateInstance(failure)! };
		(_, RequestRunner requestRunner) = TestProviderContext.CreateWithRunner(client);

		DocumentSnapshot document = fixture.Open("a.lua");

		await fixture
			.Runner.RunAsync(
				document,

				async token =>
				{
					await requestRunner
						.SendAsync(method: "textDocument/hover", parameters: new object(), fallbackValue: "fallback", cancellationToken: token)
						.ConfigureAwait(false);

					return true;
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		// The runner turns a connection failure into its fallback value, so the coordinator has nothing to classify or log.
		Assert.IsEmpty(logger.Entries);
		Assert.AreEqual(2, client.SendRequestCount);
	}

	[TestMethod]
	public async Task RunAsync_WhenTheRequestFailsOnTheConnection_LogsAtDebug()
	{
		using var logger = new RecordingLogger();
		var fixture = new TestHost(logger);

		DocumentSnapshot document = fixture.Open("a.lua");

		await fixture.Runner.RunAsync(document, static _ => throw new IOException("pipe closed"), CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(LogLevel.Debug, logger.Entries.Single().Level);
	}

	[TestMethod]
	public async Task RunAsync_WhenTheClientIsDisposedUnderALiveProvider_LogsAtDebug()
	{
		using var logger = new RecordingLogger();
		var fixture = new TestHost(logger);

		DocumentSnapshot document = fixture.Open("a.lua");

		await fixture
			.Runner.RunAsync(document, static _ => throw new ObjectDisposedException("client"), CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual(LogLevel.Debug, logger.Entries.Single().Level);
	}

	[TestMethod]
	public async Task RunAsync_WhenTheRequestRacesDisposal_LogsNothing()
	{
		using var logger = new RecordingLogger();
		var fixture = new TestHost(logger);

		DocumentSnapshot document = fixture.Open("a.lua");

		fixture.Disposal.Signal();

		await fixture
			.Runner.RunAsync(document, static _ => throw new ObjectDisposedException("client"), CancellationToken.None)
			.ConfigureAwait(false);

		Assert.IsEmpty(logger.Entries);
	}

	[TestMethod]
	public async Task RunAsync_AfterCancelAll_StartsTheRequestWithACanceledToken()
	{
		var fixture = new TestHost();
		DocumentSnapshot document = fixture.Open("a.lua");

		fixture.Runner.CancelAll();

		bool wasCanceled = false;

		await fixture
			.Runner.RunAsync(
				document,

				token =>
				{
					wasCanceled = token.IsCancellationRequested;
					return Task.FromResult(true);
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.IsTrue(wasCanceled);
	}

	[TestMethod]
	public async Task RunAsync_WhenTheRequestDoesNotSettle_RunsItAgainOncePerRetryDelay()
	{
		var fixture = new TestHost();
		DocumentSnapshot document = fixture.Open("a.lua");

		int attempts = 0;

		await fixture
			.Runner.RunAsync(
				document,

				_ =>
				{
					attempts++;
					return Task.FromResult(false);
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.AreEqual(1 + PerDocumentRequestCoordinator.RetryDelays.Count, attempts);
	}

	[TestMethod]
	public async Task RunAsync_WhenARetrySettles_StopsRetrying()
	{
		var fixture = new TestHost();
		DocumentSnapshot document = fixture.Open("a.lua");

		int attempts = 0;

		await fixture.Runner.RunAsync(document, _ => Task.FromResult(++attempts == 2), CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(2, attempts);
	}

	[TestMethod]
	public async Task RunAsync_WhenTheDocumentChangesBeforeTheRetry_DoesNotRetry()
	{
		var fixture = new TestHost();
		DocumentSnapshot document = fixture.Open("a.lua");

		int attempts = 0;

		await fixture
			.Runner.RunAsync(
				document,

				_ =>
				{
					attempts++;

					// The change advances the tracked version, so the request was for older text and the change brings its own request.
					fixture.Documents.Synchronize(document.FilePath, "changed content", leases: DocumentLeaseKinds.None);
					return Task.FromResult(false);
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.AreEqual(1, attempts);
	}

	[TestMethod]
	public async Task RunForOpenDocumentsAsync_RunsTheRequestForEveryOpenDocument()
	{
		var fixture = new TestHost();

		fixture.Open("a.lua");
		fixture.Open("b.lua");
		fixture.Open("c.lua");

		List<string> visited = [];

		await fixture
			.Runner.RunForOpenDocumentsAsync(
				(document, _) =>
				{
					lock (visited)
						visited.Add(Path.GetFileName(document.FilePath));

					return Task.CompletedTask;
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.AreSequenceEqual(["a.lua", "b.lua", "c.lua"], visited, Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
	}

	[TestMethod]
	public async Task RunForOpenDocumentsAsync_RunsNoMoreThanTheBoundAtOnce()
	{
		var fixture = new TestHost(maxConcurrentRequests: 2);

		for (int i = 0; i < 5; i++)
			fixture.Open($"doc{i}.lua");

		int running = 0;
		int highWater = 0;
		int started = 0;

		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Task fanOut = fixture.Runner.RunForOpenDocumentsAsync(
			async (_, _) =>
			{
				int now = Interlocked.Increment(ref running);

				InterlockedMax(ref highWater, now);
				Interlocked.Increment(ref started);

				await release.Task.ConfigureAwait(false);

				Interlocked.Decrement(ref running);
			},

			CancellationToken.None
		);

		await TestPolling
			.UntilAsync(() => Volatile.Read(ref started) == 2, TestPolling.DefaultTimeout, "The bound must admit two requests.")
			.ConfigureAwait(false);

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.AreEqual(2, Volatile.Read(ref started));

		release.SetResult();
		await fanOut.ConfigureAwait(false);

		Assert.AreEqual(5, Volatile.Read(ref started));
		Assert.AreEqual(2, Volatile.Read(ref highWater));
	}

	[TestMethod]
	public async Task RunForOpenDocumentsAsync_SkipsADocumentThatClosedWhileItWaitedForTheBound()
	{
		var fixture = new TestHost(maxConcurrentRequests: 1);

		fixture.Open("a.lua");
		fixture.Open("b.lua");

		List<string> visited = [];

		var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Task fanOut = fixture.Runner.RunForOpenDocumentsAsync(
			async (document, _) =>
			{
				lock (visited)
					visited.Add(Path.GetFileName(document.FilePath));

				firstStarted.TrySetResult();
				await release.Task.ConfigureAwait(false);
			},

			CancellationToken.None
		);

		await firstStarted.Task.ConfigureAwait(false);

		string waitingPath = TestPaths.Script(visited[0] == "a.lua" ? "b.lua" : "a.lua");

		fixture.Documents.Close(waitingPath);
		release.SetResult();

		await fanOut.ConfigureAwait(false);

		Assert.HasCount(1, visited);
	}

	[TestMethod]
	public async Task RunForOpenDocumentsAsync_GivesEachFanOutItsOwnBound()
	{
		var fixture = new TestHost(maxConcurrentRequests: 1);

		fixture.Open("a.lua");

		int started = 0;

		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Task Request(DocumentSnapshot document, CancellationToken token)
		{
			Interlocked.Increment(ref started);
			return release.Task;
		}

		Task firstFanOut = fixture.Runner.RunForOpenDocumentsAsync(Request, CancellationToken.None);
		Task secondFanOut = fixture.Runner.RunForOpenDocumentsAsync(Request, CancellationToken.None);

		await TestPolling
			.UntilAsync(() => Volatile.Read(ref started) == 2, TestPolling.DefaultTimeout, "Two fan-outs must not share one bound.")
			.ConfigureAwait(false);

		release.SetResult();
		await Task.WhenAll(firstFanOut, secondFanOut).ConfigureAwait(false);
	}

	private static void InterlockedMax(ref int location, int value)
	{
		int current = Volatile.Read(ref location);

		while (value > current)
		{
			int observed = Interlocked.CompareExchange(ref location, value, current);

			if (observed == current)
				return;

			current = observed;
		}
	}
}
