using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

public sealed partial class LanguageServerClientTests
{
	private const string ReadyCapabilitiesJson = """
		{
		  "capabilities": {
		    "textDocumentSync": 2,
		    "referencesProvider": true
		  }
		}
		""";

	private sealed class ScriptedStartupAttempt
	{
		public required RecordingStream Recording { get; init; }
		public required DeferredPersistentJsonRpcResponseStream Responses { get; init; }
	}

	private static LanguageServerClient CreateScriptedStartupClient(
		List<ScriptedStartupAttempt> attempts,
		LanguageServerClientOptions? options = null,
		Func<CancellationToken, Task>? beforeInitializeRequest = null,
		ILogger? logger = null
	)
	{
		return new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: new ScriptedLanguageServerTransport(attempts),
			hooks: null,
			options: options ?? s_defaultClientOptions,
			logger,

			testHooks: beforeInitializeRequest is null
				? null
				: new LanguageServerClientTestHooks { BeforeInitializeRequest = beforeInitializeRequest }
		);
	}

	/// <summary>
	/// Produces one scripted startup attempt per connection: the connection reads from a deferred response stream and records everything the client
	/// writes to it.
	/// </summary>
	private sealed class ScriptedLanguageServerTransport : ILanguageServerTransport
	{
		private readonly List<ScriptedStartupAttempt> _attempts;

		/// <param name="attempts">The attempt list each connection is appended to.</param>
		public ScriptedLanguageServerTransport(List<ScriptedStartupAttempt> attempts) => _attempts = attempts;

		/// <inheritdoc/>
		public Task<ILanguageServerConnection> ConnectAsync(LanguageServerConnectRequest request, CancellationToken cancellationToken)
		{
			var attempt = new ScriptedStartupAttempt { Recording = new RecordingStream(), Responses = new DeferredPersistentJsonRpcResponseStream() };

			_attempts.Add(attempt);

			return Task.FromResult<ILanguageServerConnection>(new RecordingLanguageServerConnection(attempt.Responses, attempt.Recording));
		}
	}

	private static async Task<ScriptedStartupAttempt> CompleteScriptedStartupAsync(
		Task<bool> startTask,
		List<ScriptedStartupAttempt> attempts,
		int attemptIndex,
		string initializeResultJson
	)
	{
		await TestPolling
			.UntilAsync(
				() => attempts.Count > attemptIndex,
				TestPolling.DefaultTimeout,
				"Timed out waiting for the scripted startup attempt to begin."
			)
			.ConfigureAwait(false);

		ScriptedStartupAttempt attempt = attempts[attemptIndex];
		int requestId = await WaitForRequestIdAsync(attempt.Recording).ConfigureAwait(false);

		attempt.Responses.SetPayload(JsonRpcFrameTestHelpers.BuildResultResponse(requestId, initializeResultJson));

		Assert.IsTrue(await startTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));

		return attempt;
	}

	/// <summary>
	/// Completes one scripted startup attempt with a capability payload that never satisfies the client, so the attempt fails and the client stays
	/// not ready.
	/// </summary>
	/// <param name="startTask">The startup task for the attempt.</param>
	/// <param name="attempts">The attempt list the scripted transport appends to.</param>
	/// <param name="attemptIndex">The zero-based index of the attempt that must fail.</param>
	/// <returns>The failed attempt.</returns>
	private static async Task<ScriptedStartupAttempt> FailScriptedStartupAsync(
		Task<bool> startTask,
		List<ScriptedStartupAttempt> attempts,
		int attemptIndex
	)
	{
		await TestPolling
			.UntilAsync(() => attempts.Count > attemptIndex, TimeSpan.FromSeconds(5), "The scripted startup attempt should begin.")
			.ConfigureAwait(false);

		ScriptedStartupAttempt attempt = attempts[attemptIndex];
		int requestId = await WaitForRequestIdAsync(attempt.Recording).ConfigureAwait(false);

		// The empty capability object advertises no text-document synchronization, so the default requirement
		// fails the handshake.
		attempt.Responses.SetPayload(JsonRpcFrameTestHelpers.BuildResultResponse(requestId, """{ "capabilities": { } }"""));

		Assert.IsFalse(await startTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));

		return attempt;
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WithScriptedTransport_CompletesHandshakeAndBecomesReady()
	{
		List<ScriptedStartupAttempt> attempts = [];

		using LanguageServerClient client = CreateScriptedStartupClient(attempts);

		Task<bool> startTask = client.EnsureStartedAsync(CancellationToken.None);

		ScriptedStartupAttempt attempt = await CompleteScriptedStartupAsync(
				startTask,
				attempts,
				attemptIndex: 0,
				initializeResultJson: ReadyCapabilitiesJson
			)
			.ConfigureAwait(false);

		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(1L, client.ConnectionGeneration);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, client.NegotiatedCapabilities.TextDocumentSyncKind);
		Assert.IsTrue(client.NegotiatedCapabilities.SupportsReferences);

		// The scripted connection records writes asynchronously; wait for the settings push to reach the
		// recorder before pinning the sequence (same synchronization as the handshake test).
		await TestPolling
			.UntilAsync(
				() => attempt.Recording.GetWrittenText().Contains("workspace/didChangeConfiguration", StringComparison.Ordinal),
				TimeSpan.FromSeconds(5),
				"The handshake should push the settings notification after initialization completes."
			)
			.ConfigureAwait(false);

		// The full handshake sequence must reach the server in this exact order.
		List<(string? Method, string BodyJson)> writtenMessages = JsonRpcFrameTestHelpers.ExtractWrittenMessages(attempt.Recording.GetWrittenText());

		Assert.AreSequenceEqual(
			["initialize", "initialized", "workspace/didChangeConfiguration"],
			[.. writtenMessages.Where(message => message.Method is not null).Select(message => message.Method!)]
		);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WhenTheSessionIsInvalidatedDuringHandshake_ReturnsFalseWithoutBecomingReady()
	{
		List<ScriptedStartupAttempt> attempts = [];
		bool invalidated = false;
		LanguageServerClient? client = null;

		try
		{
			client = CreateScriptedStartupClient(
				attempts,
				options: null,

				beforeInitializeRequest: _ =>
				{
					invalidated = true;
					Assert.IsTrue(client!.TryMarkConnectionUnhealthy(client.ConnectionGeneration));
					return Task.CompletedTask;
				}
			);

			Task<bool> startTask = client.EnsureStartedAsync(CancellationToken.None);

			await TestPolling
				.UntilAsync(() => attempts.Count > 0, TestPolling.DefaultTimeout, "Timed out waiting for the scripted startup attempt to begin.")
				.ConfigureAwait(false);

			ScriptedStartupAttempt attempt = attempts[0];
			int requestId = await WaitForRequestIdAsync(attempt.Recording).ConfigureAwait(false);

			attempt.Responses.SetPayload(JsonRpcFrameTestHelpers.BuildResultResponse(requestId, ReadyCapabilitiesJson));

			Assert.IsFalse(await startTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
			Assert.IsTrue(invalidated);
			Assert.IsFalse(client.IsReady);
			Assert.AreEqual(0L, client.ConnectionGeneration);
		}
		finally
		{
			client?.Dispose();
		}
	}

	[TestMethod]
	public async Task EnsureStartedAsync_AfterConnectionInvalidation_RestartsWithNewSessionAndReopensReady()
	{
		List<ScriptedStartupAttempt> attempts = [];

		using LanguageServerClient client = CreateScriptedStartupClient(attempts);

		Task<bool> firstStartTask = client.EnsureStartedAsync(CancellationToken.None);

		await CompleteScriptedStartupAsync(startTask: firstStartTask, attempts, attemptIndex: 0, initializeResultJson: ReadyCapabilitiesJson)
			.ConfigureAwait(false);

		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(1L, client.ConnectionGeneration);

		Assert.IsTrue(client.TryMarkConnectionUnhealthy(client.ConnectionGeneration));
		Assert.IsFalse(client.IsReady);

		Task<bool> restartTask = client.EnsureStartedAsync(CancellationToken.None);

		await CompleteScriptedStartupAsync(startTask: restartTask, attempts, attemptIndex: 1, initializeResultJson: ReadyCapabilitiesJson)
			.ConfigureAwait(false);

		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(2L, client.ConnectionGeneration);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, client.NegotiatedCapabilities.TextDocumentSyncKind);

		// Both scripted sessions received an initialize request; only the new one may still accept requests.
		Assert.AreEqual(1, CountSentMethods(attempts[0].Recording.GetWrittenText(), "initialize"));
		Assert.AreEqual(1, CountSentMethods(attempts[1].Recording.GetWrittenText(), "initialize"));
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WhenSyncIsNotRequired_AcceptsServerWithoutTextSynchronization()
	{
		List<ScriptedStartupAttempt> attempts = [];

		LanguageServerClientOptions options = LanguageServerClientOptions.Default with
		{
			RequireTextDocumentSynchronization = false,
			ShutdownRequestTimeout = TimeSpan.FromMilliseconds(1500),
			DisposeWaitTimeout = TimeSpan.FromMilliseconds(2000),
		};

		using LanguageServerClient client = CreateScriptedStartupClient(attempts, options);

		Task<bool> startTask = client.EnsureStartedAsync(CancellationToken.None);

		await CompleteScriptedStartupAsync(startTask, attempts, attemptIndex: 0, initializeResultJson: """{ "capabilities": { } }""")
			.ConfigureAwait(false);

		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(TextDocumentSyncKind.None, client.NegotiatedCapabilities.TextDocumentSyncKind);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WhenServerAdvertisesNoSyncAndSyncIsRequired_FailsStartup()
	{
		List<ScriptedStartupAttempt> attempts = [];

		using LanguageServerClient client = CreateScriptedStartupClient(attempts);

		Task<bool> startTask = client.EnsureStartedAsync(CancellationToken.None);

		await TestPolling
			.UntilAsync(() => attempts.Count > 0, TimeSpan.FromSeconds(5), "The scripted startup attempt should begin.")
			.ConfigureAwait(false);

		ScriptedStartupAttempt attempt = attempts[0];
		int requestId = await WaitForRequestIdAsync(attempt.Recording).ConfigureAwait(false);

		attempt.Responses.SetPayload(JsonRpcFrameTestHelpers.BuildResultResponse(requestId, """{ "capabilities": { "textDocumentSync": 0 } }"""));

		Assert.IsFalse(await startTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
		Assert.IsFalse(client.IsReady);
		Assert.IsNull(client.Composition.State.ActiveSession);
	}

	[TestMethod]
	public void CaptureServerCapabilitiesForGeneration_AfterConnectionInvalidation_DoesNotRepublishCapabilities()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 12,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);

		InitializeResponse initializeResponse = DeserializeInitializeResponse(ReadyCapabilitiesJson);

		CaptureServerCapabilities(client, initializeResponse);

		Assert.AreEqual(TextDocumentSyncKind.Incremental, client.NegotiatedCapabilities.TextDocumentSyncKind);
		Assert.IsTrue(client.NegotiatedCapabilities.SupportsReferences);

		Assert.IsTrue(client.TryMarkConnectionUnhealthy(client.ConnectionGeneration));
		Assert.AreEqual(TextDocumentSyncKind.None, client.NegotiatedCapabilities.TextDocumentSyncKind);

		// A late initialize completion for the invalidated generation must not restore the capabilities.
		CaptureServerCapabilities(client, initializeResponse);

		Assert.AreEqual(TextDocumentSyncKind.None, client.NegotiatedCapabilities.TextDocumentSyncKind);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsReferences);
	}

	[TestMethod]
	public async Task DiagnosticsCoalescer_DropsPayloadsQueuedBeforeConnectionInvalidation()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 5,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		int publishedCount = 0;

		SetActiveSession(client, session);
		SetReadyState(client, true);

		// The server-message dispatcher must run so a payload that survives the invalidation gate would actually reach the
		// subscriber; without it this test could not fail even if the drop logic were removed.
		StartServerMessageDispatcher(client);

		client.DiagnosticsPublished += (_, _) => publishedCount++;

		client.Composition.Router.QueueDiagnosticsPublished(
			GetConnectionGeneration(session),
			CreateDiagnosticsParameters(TestPaths.FileUri("queued.ext"), "Queued warning.")
		);

		Assert.IsTrue(client.TryMarkConnectionUnhealthy(GetConnectionGeneration(session)));

		Task diagnosticsCoalescerTask = client.Composition.Router.DiagnosticsCoalescer.RunAsync();

		await TestPolling
			.UntilAsync(() => client.Composition.Router.IsDrained, TestPolling.DefaultTimeout, "The router should deliver every queued payload.")
			.ConfigureAwait(false);

		client.Composition.Lifetime.Cancel();
		await diagnosticsCoalescerTask.ConfigureAwait(false);

		// A payload accepted before invalidation must not reach subscribers after the connection was marked unavailable.
		Assert.AreEqual(0, publishedCount);
	}

	private static int CountSentMethods(string writtenText, string method)
	{
		int count = 0;

		foreach ((string? Method, string BodyJson) message in JsonRpcFrameTestHelpers.ExtractWrittenMessages(writtenText))
		{
			if (string.Equals(message.Method, method, StringComparison.Ordinal))
				count++;
		}

		return count;
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WhenTheServerNeverAnswersInitialize_CompletesWithinTheConfiguredTimeout()
	{
		List<ScriptedStartupAttempt> attempts = [];

		await using LanguageServerClient client = CreateScriptedStartupClient(
			attempts,

			LanguageServerClientOptions.Default with
			{
				InitializeRequestTimeout = TimeSpan.FromMilliseconds(200),
			}
		);

		Task<bool> startTask = client.EnsureStartedAsync(CancellationToken.None);

		// The scripted server answers nothing, so the configured initialization timeout is the only bound on the
		// startup attempt; the bounded wait fails red with a timeout instead of blocking forever when the
		// initialize request stops honoring it.
		Assert.IsFalse(await startTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
		Assert.IsFalse(client.IsReady);
		Assert.IsNotNull(client.LastStartupException);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WithConcurrentCallers_StartsOneConnectionAndReportsReadyForBoth()
	{
		List<ScriptedStartupAttempt> attempts = [];

		using LanguageServerClient client = CreateScriptedStartupClient(attempts);

		Task<bool> firstStartTask = client.EnsureStartedAsync(CancellationToken.None);

		// The first caller holds the startup gate by the time the scripted attempt exists, so the second caller is
		// deterministically queued behind it rather than racing the transport connect.
		await TestPolling
			.UntilAsync(() => attempts.Count == 1, TimeSpan.FromSeconds(5), "The first scripted attempt should begin.")
			.ConfigureAwait(false);

		Task<bool> secondStartTask = client.EnsureStartedAsync(CancellationToken.None);

		await CompleteScriptedStartupAsync(startTask: firstStartTask, attempts, attemptIndex: 0, initializeResultJson: ReadyCapabilitiesJson)
			.ConfigureAwait(false);

		// The queued caller observes the ready session the first caller published instead of opening a second
		// connection, so exactly one initialize request reaches the server.
		Assert.IsTrue(await secondStartTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
		Assert.HasCount(1, attempts);
		Assert.AreEqual(1L, client.ConnectionGeneration);
		Assert.AreEqual(1, CountSentMethods(attempts[0].Recording.GetWrittenText(), "initialize"));
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WhenAWaitingCallerIsCanceled_KeepsTheStartThatOtherCallersAreRunning()
	{
		List<ScriptedStartupAttempt> attempts = [];
		var gateReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		using LanguageServerClient client = CreateScriptedStartupClient(
			attempts,

			beforeInitializeRequest: async _ =>
			{
				gateReached.TrySetResult();
				await releaseGate.Task.ConfigureAwait(false);
			}
		);

		// The first caller holds the startup gate and is parked before the initialize request, with its session already active.
		Task<bool> firstStartTask = client.EnsureStartedAsync(CancellationToken.None);

		await gateReached.Task.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Task<bool> keptWaiterStartTask = client.EnsureStartedAsync(CancellationToken.None);

		using var cancelWaiter = new CancellationTokenSource();
		Task<bool> canceledWaiterStartTask = client.EnsureStartedAsync(cancelWaiter.Token);

		Assert.IsFalse(canceledWaiterStartTask.IsCompleted, "The second caller must be waiting at the startup gate.");

		await cancelWaiter.CancelAsync().ConfigureAwait(false);

		await Assert.ThrowsAsync<OperationCanceledException>(() => canceledWaiterStartTask).ConfigureAwait(false);

		Assert.IsNull(client.LastStartupException, "A caller that never held the gate has no attempt to report.");

		releaseGate.SetResult();

		await CompleteScriptedStartupAsync(startTask: firstStartTask, attempts, attemptIndex: 0, initializeResultJson: ReadyCapabilitiesJson)
			.ConfigureAwait(false);

		Assert.IsTrue(await keptWaiterStartTask.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false));
		Assert.IsTrue(client.IsReady);
		Assert.IsNull(client.LastStartupException);
		Assert.HasCount(1, attempts);
		Assert.AreEqual(1L, client.ConnectionGeneration);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_AfterAFailedAttempt_RetriesWithAFreshSessionAndBecomesReady()
	{
		List<ScriptedStartupAttempt> attempts = [];

		using LanguageServerClient client = CreateScriptedStartupClient(attempts);

		await FailScriptedStartupAsync(client.EnsureStartedAsync(CancellationToken.None), attempts, 0).ConfigureAwait(false);

		Assert.IsFalse(client.IsReady);

		// A failed attempt must not latch the client: the next call drives a fresh session over a new connection.
		await CompleteScriptedStartupAsync(
				startTask: client.EnsureStartedAsync(CancellationToken.None),
				attempts,
				attemptIndex: 1,
				initializeResultJson: ReadyCapabilitiesJson
			)
			.ConfigureAwait(false);

		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, client.NegotiatedCapabilities.TextDocumentSyncKind);
		Assert.HasCount(2, attempts);
		Assert.AreEqual(1, CountSentMethods(attempts[0].Recording.GetWrittenText(), "initialize"));
		Assert.AreEqual(1, CountSentMethods(attempts[1].Recording.GetWrittenText(), "initialize"));
	}

	[TestMethod]
	public async Task EnsureStartedAsync_AfterAFailedAttempt_ClearsTheLastStartupExceptionOnTheNextSuccessfulAttempt()
	{
		List<ScriptedStartupAttempt> attempts = [];

		using LanguageServerClient client = CreateScriptedStartupClient(attempts);

		await FailScriptedStartupAsync(client.EnsureStartedAsync(CancellationToken.None), attempts, 0).ConfigureAwait(false);

		Assert.IsNotNull(client.LastStartupException);

		await CompleteScriptedStartupAsync(
				startTask: client.EnsureStartedAsync(CancellationToken.None),
				attempts,
				attemptIndex: 1,
				initializeResultJson: ReadyCapabilitiesJson
			)
			.ConfigureAwait(false);

		// The diagnostic surface reports only the most recent attempt, so a successful retry clears the failure.
		Assert.IsNull(client.LastStartupException);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_OnRestart_PushesTheConfigurationToTheNewSession()
	{
		List<ScriptedStartupAttempt> attempts = [];

		using LanguageServerClient client = CreateScriptedStartupClient(attempts);

		await CompleteScriptedStartupAsync(
				startTask: client.EnsureStartedAsync(CancellationToken.None),
				attempts,
				attemptIndex: 0,
				initializeResultJson: ReadyCapabilitiesJson
			)
			.ConfigureAwait(false);

		Assert.IsTrue(client.TryMarkConnectionUnhealthy(client.ConnectionGeneration));

		await CompleteScriptedStartupAsync(
				startTask: client.EnsureStartedAsync(CancellationToken.None),
				attempts,
				attemptIndex: 1,
				initializeResultJson: ReadyCapabilitiesJson
			)
			.ConfigureAwait(false);

		// Each session owns its own handshake, so the restarted session must receive the initialize pair and the
		// settings push again instead of reusing the first session's push.
		await TestPolling
			.UntilAsync(
				() => attempts[1].Recording.GetWrittenText().Contains("workspace/didChangeConfiguration", StringComparison.Ordinal),
				TimeSpan.FromSeconds(5),
				"The restarted session should receive the settings push."
			)
			.ConfigureAwait(false);

		Assert.AreSequenceEqual(
			["initialize", "initialized", "workspace/didChangeConfiguration"],

			[
				.. JsonRpcFrameTestHelpers
					.ExtractWrittenMessages(attempts[1].Recording.GetWrittenText())
					.Where(message => message.Method is not null)
					.Select(message => message.Method!),
			]
		);
	}
}
