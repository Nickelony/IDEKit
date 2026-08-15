using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

public partial class LanguageServerClientTests
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

	private static LanguageServerClient CreateScriptedStartupClient(List<ScriptedStartupAttempt> attempts, LanguageServerClientOptions? options = null,
		Func<CancellationToken, Task>? beforeInitializeRequest = null)
	{
		return new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", CreateScriptedOptions(attempts, options),
			logger: null,
			testHooks: beforeInitializeRequest is null ? null : new ClientTestHooks
			{
				BeforeInitializeRequest = beforeInitializeRequest
			});
	}

	/// <summary>
	/// Rebuilds the supplied options with the scripted transport attached.
	/// </summary>
	/// <remarks>
	/// The transport replaces the server process for these tests, and the shared default options instance must not
	/// be mutated, so every value the tests rely on is copied onto a fresh instance.
	/// </remarks>
	/// <param name="attempts">The attempt list the scripted transport appends to.</param>
	/// <param name="options">The options to copy, or <see langword="null"/> for the shared test defaults.</param>
	/// <returns>The options carrying the scripted transport.</returns>
	private static LanguageServerClientOptions CreateScriptedOptions(List<ScriptedStartupAttempt> attempts, LanguageServerClientOptions? options)
	{
		LanguageServerClientOptions source = options ?? s_defaultClientOptions;

		return new LanguageServerClientOptions
		{
			SettingsProvider = source.SettingsProvider,
			InitializeTimeout = source.InitializeTimeout,
			ShutdownRequestTimeout = source.ShutdownRequestTimeout,
			DisposeWaitTimeout = source.DisposeWaitTimeout,
			ClientCapabilitiesProvider = source.ClientCapabilitiesProvider,
			InitializationOptionsProvider = source.InitializationOptionsProvider,
			RequireTextDocumentSynchronization = source.RequireTextDocumentSynchronization,
			ServerArguments = source.ServerArguments,
			ServerWorkingDirectory = source.ServerWorkingDirectory,
			EnvironmentVariables = source.EnvironmentVariables,
			Transport = new ScriptedLanguageServerTransport(attempts)
		};
	}

	/// <summary>
	/// Produces one scripted startup attempt per connection: the connection reads from a deferred response stream
	/// and records everything the client writes to it.
	/// </summary>
	private sealed class ScriptedLanguageServerTransport : ILanguageServerTransport
	{
		private readonly List<ScriptedStartupAttempt> _attempts;

		/// <summary>
		/// Initializes a new instance of the <see cref="ScriptedLanguageServerTransport"/> class.
		/// </summary>
		/// <param name="attempts">The attempt list each connection is appended to.</param>
		public ScriptedLanguageServerTransport(List<ScriptedStartupAttempt> attempts)
			=> _attempts = attempts;

		/// <inheritdoc/>
		public Task<ILanguageServerConnection> ConnectAsync(LanguageServerTransportContext context, CancellationToken cancellationToken)
		{
			var attempt = new ScriptedStartupAttempt
			{
				Recording = new RecordingStream(),
				Responses = new DeferredPersistentJsonRpcResponseStream()
			};

			_attempts.Add(attempt);

			return Task.FromResult<ILanguageServerConnection>(
				new TestLanguageServerConnection(attempt.Responses, attempt.Recording));
		}
	}

	private static async Task<ScriptedStartupAttempt> CompleteScriptedStartupAsync(
		Task<bool> startTask,
		List<ScriptedStartupAttempt> attempts,
		int attemptIndex,
		string initializeResultJson)
	{
		await TestPolling.UntilAsync(
			() => attempts.Count > attemptIndex,
			TestPolling.DefaultTimeout,
			"Timed out waiting for the scripted startup attempt to begin.").ConfigureAwait(false);

		ScriptedStartupAttempt attempt = attempts[attemptIndex];
		int requestId = await WaitForRequestIdAsync(attempt.Recording).ConfigureAwait(false);

		attempt.Responses.SetPayload(JsonRpcFrameTestHelper.BuildResultResponse(requestId, initializeResultJson));

		Assert.IsTrue(await startTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));

		return attempt;
	}

	/// <summary>
	/// Completes one scripted startup attempt with a capability payload that never satisfies the client, so the
	/// attempt fails and the client stays not ready.
	/// </summary>
	/// <param name="startTask">The startup task for the attempt.</param>
	/// <param name="attempts">The attempt list the scripted transport appends to.</param>
	/// <param name="attemptIndex">The zero-based index of the attempt that must fail.</param>
	/// <returns>The failed attempt.</returns>
	private static async Task<ScriptedStartupAttempt> FailScriptedStartupAsync(Task<bool> startTask, List<ScriptedStartupAttempt> attempts, int attemptIndex)
	{
		await TestPolling.UntilAsync(
			() => attempts.Count > attemptIndex,
			TimeSpan.FromSeconds(5),
			"The scripted startup attempt should begin.").ConfigureAwait(false);

		ScriptedStartupAttempt attempt = attempts[attemptIndex];
		int requestId = await WaitForRequestIdAsync(attempt.Recording).ConfigureAwait(false);

		// The empty capability object advertises no text-document synchronization, so the default requirement
		// fails the handshake.
		attempt.Responses.SetPayload(JsonRpcFrameTestHelper.BuildResultResponse(requestId, """{ "capabilities": { } }"""));

		Assert.IsFalse(await startTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));

		return attempt;
	}

	[TestMethod]
	public async Task StartAsync_WithScriptedTransportSession_CompletesHandshakeAndBecomesReady()
	{
		List<ScriptedStartupAttempt> attempts = [];

		using var client = CreateScriptedStartupClient(attempts);

		Task<bool> startTask = client.StartAsync(CancellationToken.None);

		ScriptedStartupAttempt attempt = await CompleteScriptedStartupAsync(startTask, attempts, 0, ReadyCapabilitiesJson).ConfigureAwait(false);

		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(1L, client.TransportGeneration);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, client.TextDocumentSyncKind);
		Assert.IsTrue(client.SupportsReferences);

		// The scripted transport records writes asynchronously; wait for the settings push to reach the
		// recorder before pinning the sequence (same synchronization as the handshake test).
		await TestPolling.UntilAsync(
			() => attempt.Recording.GetWrittenText().Contains("workspace/didChangeConfiguration", StringComparison.Ordinal),
			TimeSpan.FromSeconds(5),
			"The handshake should push the settings notification after initialization completes.").ConfigureAwait(false);

		// The full handshake sequence must reach the server in this exact order.
		List<(string? Method, string BodyJson)> writtenMessages = JsonRpcFrameTestHelper.ExtractWrittenMessages(attempt.Recording.GetWrittenText());

		CollectionAssert.AreEqual(
			new[] { "initialize", "initialized", "workspace/didChangeConfiguration" },
			writtenMessages.Where(message => message.Method is not null).Select(message => message.Method!).ToArray());
	}

	[TestMethod]
	public void BuildInitializeParams_WhenCapabilitiesProviderReturnsNonObject_SendsEmptyCapabilities()
	{
		var options = LanguageServerClientOptions.Default with
		{
			ClientCapabilitiesProvider = static _ => new[] { 1 }
		};

		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", options);

		JsonElement initializeParams = JsonSerializer.SerializeToElement(client.BuildInitializeParams());
		JsonElement capabilities = initializeParams.GetProperty("capabilities");

		// A non-object payload cannot receive the enforced overrides and must not reach the server as a scalar
		// or array; the handshake stays protocol-valid with empty capabilities instead.
		Assert.AreEqual(JsonValueKind.Object, capabilities.ValueKind);
		Assert.AreEqual(0, capabilities.EnumerateObject().Count());
	}

	[TestMethod]
	public void BuildInitializeParams_WhenCapabilitiesProviderReturnsNull_SendsEmptyCapabilities()
	{
		var options = LanguageServerClientOptions.Default with
		{
			ClientCapabilitiesProvider = static _ => null
		};

		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", options);

		JsonElement initializeParams = JsonSerializer.SerializeToElement(client.BuildInitializeParams());
		JsonElement capabilities = initializeParams.GetProperty("capabilities");

		// A null factory result is treated as an empty capabilities object; the only member present is the
		// enforced UTF-16 position-encoding pin the client always adds.
		Assert.AreEqual(JsonValueKind.Object, capabilities.ValueKind);
		Assert.AreEqual(1, capabilities.EnumerateObject().Count());
		Assert.AreEqual("utf-16", capabilities.GetProperty("general").GetProperty("positionEncodings")[0].GetString());
	}

	[TestMethod]
	public async Task StartAsync_WhenTheSessionIsInvalidatedDuringHandshake_ReturnsFalseWithoutBecomingReady()
	{
		List<ScriptedStartupAttempt> attempts = [];
		bool invalidated = false;
		LanguageServerClient? client = null;

		try
		{
			client = CreateScriptedStartupClient(attempts, options: null,
				beforeInitializeRequest: _ =>
				{
					invalidated = true;
					Assert.IsTrue(client!.TryMarkTransportUnhealthy(client.TransportGeneration));
					return Task.CompletedTask;
				});

			Task<bool> startTask = client.StartAsync(CancellationToken.None);

			await TestPolling.UntilAsync(
				() => attempts.Count > 0,
				TestPolling.DefaultTimeout,
				"Timed out waiting for the scripted startup attempt to begin.").ConfigureAwait(false);

			ScriptedStartupAttempt attempt = attempts[0];
			int requestId = await WaitForRequestIdAsync(attempt.Recording).ConfigureAwait(false);

			attempt.Responses.SetPayload(JsonRpcFrameTestHelper.BuildResultResponse(requestId, ReadyCapabilitiesJson));

			Assert.IsFalse(await startTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
			Assert.IsTrue(invalidated);
			Assert.IsFalse(client.IsReady);
			Assert.AreEqual(0L, client.TransportGeneration);
		}
		finally
		{
			client?.Dispose();
		}
	}

	[TestMethod]
	public async Task StartAsync_AfterTransportInvalidation_RestartsWithNewSessionAndReopensReady()
	{
		List<ScriptedStartupAttempt> attempts = [];

		using var client = CreateScriptedStartupClient(attempts);

		Task<bool> firstStartTask = client.StartAsync(CancellationToken.None);

		await CompleteScriptedStartupAsync(firstStartTask, attempts, 0, ReadyCapabilitiesJson).ConfigureAwait(false);

		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(1L, client.TransportGeneration);

		Assert.IsTrue(client.TryMarkTransportUnhealthy(client.TransportGeneration));
		Assert.IsFalse(client.IsReady);

		Task<bool> restartTask = client.StartAsync(CancellationToken.None);

		await CompleteScriptedStartupAsync(restartTask, attempts, 1, ReadyCapabilitiesJson).ConfigureAwait(false);

		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(2L, client.TransportGeneration);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, client.TextDocumentSyncKind);

		// Both scripted sessions received an initialize request; only the new one may still accept requests.
		Assert.AreEqual(1, CountSentMethods(attempts[0].Recording.GetWrittenText(), "initialize"));
		Assert.AreEqual(1, CountSentMethods(attempts[1].Recording.GetWrittenText(), "initialize"));
	}

	[TestMethod]
	public async Task StartAsync_WhenSyncIsNotRequired_AcceptsServerWithoutTextSynchronization()
	{
		List<ScriptedStartupAttempt> attempts = [];
		var options = LanguageServerClientOptions.Default with
		{
			RequireTextDocumentSynchronization = false,
			ShutdownRequestTimeout = TimeSpan.FromMilliseconds(1500),
			DisposeWaitTimeout = TimeSpan.FromMilliseconds(2000)
		};

		using var client = CreateScriptedStartupClient(attempts, options);

		Task<bool> startTask = client.StartAsync(CancellationToken.None);

		await CompleteScriptedStartupAsync(startTask, attempts, 0, """{ "capabilities": { } }""").ConfigureAwait(false);

		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(TextDocumentSyncKind.None, client.TextDocumentSyncKind);
	}

	[TestMethod]
	public async Task StartAsync_WhenServerAdvertisesNoSyncAndSyncIsRequired_FailsStartup()
	{
		List<ScriptedStartupAttempt> attempts = [];

		using var client = CreateScriptedStartupClient(attempts);

		Task<bool> startTask = client.StartAsync(CancellationToken.None);

		await TestPolling.UntilAsync(
			() => attempts.Count > 0,
			TimeSpan.FromSeconds(5),
			"The scripted startup attempt should begin.").ConfigureAwait(false);

		ScriptedStartupAttempt attempt = attempts[0];
		int requestId = await WaitForRequestIdAsync(attempt.Recording).ConfigureAwait(false);

		attempt.Responses.SetPayload(JsonRpcFrameTestHelper.BuildResultResponse(requestId, """{ "capabilities": { "textDocumentSync": 0 } }"""));

		Assert.IsFalse(await startTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
		Assert.IsFalse(client.IsReady);
		Assert.IsNull(client.CapabilityStore.ActiveSession);
	}

	[TestMethod]
	public void CaptureServerCapabilitiesForGeneration_AfterTransportInvalidation_DoesNotRepublishCapabilities()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 12, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);

		InitializeResponse initializeResponse = DeserializeInitializeResponse(ReadyCapabilitiesJson);

		CaptureServerCapabilities(client, initializeResponse);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, client.TextDocumentSyncKind);
		Assert.IsTrue(client.SupportsReferences);

		Assert.IsTrue(client.TryMarkTransportUnhealthy(client.TransportGeneration));
		Assert.AreEqual(TextDocumentSyncKind.None, client.TextDocumentSyncKind);

		// A late initialize completion for the invalidated generation must not restore the capabilities.
		CaptureServerCapabilities(client, initializeResponse);
		Assert.AreEqual(TextDocumentSyncKind.None, client.TextDocumentSyncKind);
		Assert.IsFalse(client.SupportsReferences);
	}

	[TestMethod]
	public async Task PumpDiagnosticsAsync_DropsPayloadsQueuedBeforeTransportInvalidation()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 5, process: null, Stream.Null, Stream.Null);
		int publishedCount = 0;

		SetActiveSession(client, session);
		SetReadyState(client, true);

		// The callback pump must run so a payload that survives the invalidation gate would actually reach the
		// subscriber; without it this test could not fail even if the drop logic were removed.
		StartCallbackPump(client);

		client.DiagnosticsPublished += (_, _) => publishedCount++;

		client.DiagnosticsRouter.QueueDiagnosticsPublished(
			GetTransportGeneration(session),
			CreateDiagnosticsParameters("file:///C:/Workspace/queued.ext", "Queued warning."));

		Assert.IsTrue(client.TryMarkTransportUnhealthy(GetTransportGeneration(session)));

		Task diagnosticsPumpTask = client.DiagnosticsRouter.PumpDiagnosticsAsync();

		await client.DiagnosticsRouter.WaitForDrainAsync().WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		client.CancelLifetime();
		await diagnosticsPumpTask.ConfigureAwait(false);

		// A payload accepted before invalidation must not reach subscribers after the transport was marked unavailable.
		Assert.AreEqual(0, publishedCount);
	}

	[TestMethod]
	public void BuildInitializeParams_WithoutWorkspaceRoots_OmitsRootsAndPinsUtf16()
	{
		using var client = new LanguageServerClient([], "example-language-server.exe", s_defaultClientOptions);

		System.Text.Json.JsonElement payload = System.Text.Json.JsonSerializer.SerializeToElement(client.BuildInitializeParams());

		Assert.AreEqual(System.Text.Json.JsonValueKind.Null, payload.GetProperty("rootUri").ValueKind);
		Assert.AreEqual(System.Text.Json.JsonValueKind.Null, payload.GetProperty("workspaceFolders").ValueKind);
		Assert.AreEqual("utf-16",
			payload.GetProperty("capabilities").GetProperty("general").GetProperty("positionEncodings")[0].GetString());
	}

	[TestMethod]
	public void BuildInitializeParams_ConsumerAdvertisesUtf8_OverridesEncodingAndWorkDoneProgress()
	{
		var options = new LanguageServerClientOptions
		{
			ClientCapabilitiesProvider = _ => new
			{
				General = new { PositionEncodings = new[] { "utf-8" } },
				Window = new { WorkDoneProgress = true }
			}
		};

		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", options);

		System.Text.Json.JsonElement capabilities = System.Text.Json.JsonSerializer.SerializeToElement(client.BuildInitializeParams())
			.GetProperty("capabilities");

		Assert.AreEqual("utf-16", capabilities.GetProperty("general").GetProperty("positionEncodings")[0].GetString());
		Assert.IsFalse(capabilities.GetProperty("window").GetProperty("workDoneProgress").GetBoolean());
	}

	private static int CountSentMethods(string writtenText, string method)
	{
		int count = 0;

		foreach (var message in JsonRpcFrameTestHelper.ExtractWrittenMessages(writtenText))
		{
			if (string.Equals(message.Method, method, StringComparison.Ordinal))
				count++;
		}

		return count;
	}

	[TestMethod]
	public async Task StartAsync_WhenTheServerNeverAnswersInitialize_CompletesWithinTheConfiguredTimeout()
	{
		List<ScriptedStartupAttempt> attempts = [];

		await using var client = CreateScriptedStartupClient(attempts, LanguageServerClientOptions.Default with
		{
			InitializeTimeout = TimeSpan.FromMilliseconds(200)
		});

		Task<bool> startTask = client.StartAsync(CancellationToken.None);

		// The scripted server answers nothing, so the configured initialization timeout is the only bound on the
		// startup attempt; the bounded wait fails red with a timeout instead of hanging the suite when the
		// initialize request stops honoring it.
		Assert.IsFalse(await startTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
		Assert.IsFalse(client.IsReady);
		Assert.IsNotNull(client.LastStartupException);
	}

	[TestMethod]
	public async Task StartAsync_WithConcurrentCallers_StartsOneTransportAndReportsReadyForBoth()
	{
		List<ScriptedStartupAttempt> attempts = [];

		using var client = CreateScriptedStartupClient(attempts);

		Task<bool> firstStartTask = client.StartAsync(CancellationToken.None);

		// The first caller holds the startup gate by the time the scripted attempt exists, so the second caller is
		// deterministically queued behind it rather than racing the transport connect.
		await TestPolling.UntilAsync(() => attempts.Count == 1, TimeSpan.FromSeconds(5), "The first scripted attempt should begin.").ConfigureAwait(false);

		Task<bool> secondStartTask = client.StartAsync(CancellationToken.None);

		await CompleteScriptedStartupAsync(firstStartTask, attempts, 0, ReadyCapabilitiesJson).ConfigureAwait(false);

		// The queued caller observes the ready session the first caller published instead of opening a second
		// transport, so exactly one initialize request reaches the server.
		Assert.IsTrue(await secondStartTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
		Assert.AreEqual(1, attempts.Count);
		Assert.AreEqual(1L, client.TransportGeneration);
		Assert.AreEqual(1, CountSentMethods(attempts[0].Recording.GetWrittenText(), "initialize"));
	}

	[TestMethod]
	public async Task StartAsync_AfterAFailedAttempt_RetriesWithAFreshSessionAndBecomesReady()
	{
		List<ScriptedStartupAttempt> attempts = [];

		using var client = CreateScriptedStartupClient(attempts);

		await FailScriptedStartupAsync(client.StartAsync(CancellationToken.None), attempts, 0).ConfigureAwait(false);

		Assert.IsFalse(client.IsReady);

		// A failed attempt must not latch the client: the next call drives a fresh session over a new connection.
		await CompleteScriptedStartupAsync(client.StartAsync(CancellationToken.None), attempts, 1, ReadyCapabilitiesJson).ConfigureAwait(false);

		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, client.TextDocumentSyncKind);
		Assert.AreEqual(2, attempts.Count);
		Assert.AreEqual(1, CountSentMethods(attempts[0].Recording.GetWrittenText(), "initialize"));
		Assert.AreEqual(1, CountSentMethods(attempts[1].Recording.GetWrittenText(), "initialize"));
	}

	[TestMethod]
	public async Task StartAsync_AfterAFailedAttempt_ClearsTheLastStartupExceptionOnTheNextSuccessfulAttempt()
	{
		List<ScriptedStartupAttempt> attempts = [];

		using var client = CreateScriptedStartupClient(attempts);

		await FailScriptedStartupAsync(client.StartAsync(CancellationToken.None), attempts, 0).ConfigureAwait(false);

		Assert.IsNotNull(client.LastStartupException);

		await CompleteScriptedStartupAsync(client.StartAsync(CancellationToken.None), attempts, 1, ReadyCapabilitiesJson).ConfigureAwait(false);

		// The diagnostic surface reports only the most recent attempt, so a successful retry clears the failure.
		Assert.IsNull(client.LastStartupException);
	}

	[TestMethod]
	public async Task StartAsync_OnRestart_PushesTheConfigurationToTheNewSession()
	{
		List<ScriptedStartupAttempt> attempts = [];

		using var client = CreateScriptedStartupClient(attempts);

		await CompleteScriptedStartupAsync(client.StartAsync(CancellationToken.None), attempts, 0, ReadyCapabilitiesJson).ConfigureAwait(false);

		Assert.IsTrue(client.TryMarkTransportUnhealthy(client.TransportGeneration));

		await CompleteScriptedStartupAsync(client.StartAsync(CancellationToken.None), attempts, 1, ReadyCapabilitiesJson).ConfigureAwait(false);

		// Each session owns its own handshake, so the restarted session must receive the initialize pair and the
		// settings push again instead of reusing the first session's push.
		await TestPolling.UntilAsync(
			() => attempts[1].Recording.GetWrittenText().Contains("workspace/didChangeConfiguration", StringComparison.Ordinal),
			TimeSpan.FromSeconds(5),
			"The restarted session should receive the settings push.").ConfigureAwait(false);

		CollectionAssert.AreEqual(
			new[] { "initialize", "initialized", "workspace/didChangeConfiguration" },
			JsonRpcFrameTestHelper.ExtractWrittenMessages(attempts[1].Recording.GetWrittenText()).Where(message => message.Method is not null).Select(message => message.Method!).ToArray());
	}
}
