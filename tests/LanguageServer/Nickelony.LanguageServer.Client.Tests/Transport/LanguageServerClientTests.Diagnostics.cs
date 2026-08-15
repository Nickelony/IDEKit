using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

public partial class LanguageServerClientTests
{
	// These tests queue payloads directly instead of through an active session, so they replace the production
	// transport-generation gate with one that accepts the session-less generation.
	private static readonly ClientTestHooks s_unfencedDiagnosticsHooks = new()
	{
		CanAcceptServerCallbacksForGeneration = static _ => true
	};

	[TestMethod]
	public async Task PublishDiagnostics_WhenTransportIsAttachedButNotReady_DeliversDiagnostics()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 6, process: null, Stream.Null, Stream.Null);
		var publishedMessage = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

		SetActiveSession(client, session);
		client.DiagnosticsRouter.EnsurePumpsRunning(includeDiagnosticsPump: true);

		client.DiagnosticsPublished += (_, eventArgs) => publishedMessage.TrySetResult(eventArgs.Parameters.Diagnostics?[0].Message);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(session));

		rpcTarget.PublishDiagnostics(
			CreateDiagnosticsParameters("file:///C:/Workspace/test.ext", "Initial warning."));

		Assert.AreEqual("Initial warning.", await publishedMessage.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task PublishDiagnostics_IgnoresUnhealthyTransportGeneration()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 6, process: null, Stream.Null, Stream.Null);
		var publishedMessage = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		// Both pumps must run: the diagnostics pump applies the generation gate and the callback pump delivers
		// queued payloads, so only this combination can prove the payload is dropped rather than undelivered.
		client.DiagnosticsRouter.EnsurePumpsRunning(includeDiagnosticsPump: true);

		client.DiagnosticsPublished += (_, eventArgs) => publishedMessage.TrySetResult(eventArgs.Parameters.Diagnostics?[0].Message);

		client.TryMarkTransportUnhealthy(client.TransportGeneration);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(session));

		rpcTarget.PublishDiagnostics(
			CreateDiagnosticsParameters("file:///C:/Workspace/test.ext", "Ignored warning."));

		await client.DiagnosticsRouter.WaitForDrainAsync().WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsFalse(publishedMessage.Task.IsCompleted);
	}

	[TestMethod]
	public async Task PublishDiagnostics_WhenPayloadIsDegraded_DropsNotificationWithoutRaising()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 6, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);
		StartCallbackPump(client);

		int publishedCount = 0;
		client.DiagnosticsPublished += (_, _) => Interlocked.Increment(ref publishedCount);

		// A degraded payload is what the converter produces for a malformed notification; the server-callback
		// target must drop it so it can never surface as "clear diagnostics for this document".
		var degradedPayload = new PublishDiagnosticsParams("file:///C:/Workspace/test.ext", null, null) { IsDegraded = true };

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(session));
		rpcTarget.PublishDiagnostics(degradedPayload);

		// Cancel the callback pump instead of waiting out a fixed window: the drop is synchronous, so nothing was
		// ever queued for the pump to deliver.
		client.CancelLifetime();
		await client.DiagnosticsRouter.PumpCallbacksAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.AreEqual(0, publishedCount);
	}

	[TestMethod]
	public async Task PumpDiagnosticsAsync_CoalescesQueuedDiagnosticsByFile()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logger: null, testHooks: s_unfencedDiagnosticsHooks);
		StartCallbackPump(client);
		int publishedCount = 0;
		string? lastMessage = null;

		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			publishedCount++;
			lastMessage = eventArgs.Parameters.Diagnostics?[0].Message;
			client.CancelLifetime();
		};

		client.DiagnosticsRouter.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters("file:///C:/Workspace/test.ext", "Stale warning."));
		client.DiagnosticsRouter.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters("file:///C:/Workspace/test.ext", "Current warning."));

		await client.DiagnosticsRouter.PumpDiagnosticsAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.AreEqual(1, publishedCount);
		Assert.AreEqual("Current warning.", lastMessage);
	}

	[TestMethod]
	public async Task PumpDiagnosticsAsync_CoalescesWindowsFileUrisThatDifferOnlyByCase()
	{
		if (LanguageServerPaths.UsesCaseSensitiveLocalPaths)
			Assert.Inconclusive("The test requires case-insensitive local-path identity on the current host.");

		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logger: null, testHooks: s_unfencedDiagnosticsHooks);
		StartCallbackPump(client);
		int publishedCount = 0;
		string? lastMessage = null;

		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			publishedCount++;
			lastMessage = eventArgs.Parameters.Diagnostics?[0].Message;
			client.CancelLifetime();
		};

		client.DiagnosticsRouter.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters("file:///C:/Workspace/Test.ext", "First warning."));
		client.DiagnosticsRouter.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters("file:///c:/workspace/test.ext", "Latest warning."));

		await client.DiagnosticsRouter.PumpDiagnosticsAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.AreEqual(1, publishedCount);
		Assert.AreEqual("Latest warning.", lastMessage);
	}

	[TestMethod]
	public async Task PumpDiagnosticsAsync_DropsQueuedDiagnosticsFromInactiveTransportGeneration()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		StartCallbackPump(client);
		TransportSession oldSession = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);
		TransportSession newSession = CreateTransportSession(client, 2, process: null, Stream.Null, Stream.Null);
		int publishedCount = 0;
		string? lastMessage = null;

		SetActiveSession(client, newSession);

		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			publishedCount++;
			lastMessage = eventArgs.Parameters.Diagnostics?[0].Message;
			client.CancelLifetime();
		};

		client.DiagnosticsRouter.QueueDiagnosticsPublished(GetTransportGeneration(oldSession),
			CreateDiagnosticsParameters("file:///C:/Workspace/stale.ext", "Stale warning."));

		client.DiagnosticsRouter.QueueDiagnosticsPublished(GetTransportGeneration(newSession),
			CreateDiagnosticsParameters("file:///C:/Workspace/current.ext", "Current warning."));

		await client.DiagnosticsRouter.PumpDiagnosticsAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.AreEqual(1, publishedCount);
		Assert.AreEqual("Current warning.", lastMessage);
	}

	[TestMethod]
	public async Task PumpDiagnosticsAsync_SameUriStaleGenerationDoesNotOverwriteCurrentGenerationPayload()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		StartCallbackPump(client);
		TransportSession oldSession = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);
		TransportSession newSession = CreateTransportSession(client, 2, process: null, Stream.Null, Stream.Null);
		int publishedCount = 0;
		string? lastMessage = null;

		SetActiveSession(client, newSession);

		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			publishedCount++;
			lastMessage = eventArgs.Parameters.Diagnostics?[0].Message;
			client.CancelLifetime();
		};

		client.DiagnosticsRouter.QueueDiagnosticsPublished(GetTransportGeneration(newSession),
			CreateDiagnosticsParameters("file:///C:/Workspace/test.ext", "Current warning."));

		client.DiagnosticsRouter.QueueDiagnosticsPublished(GetTransportGeneration(oldSession),
			CreateDiagnosticsParameters("file:///C:/Workspace/test.ext", "Stale warning."));

		await client.DiagnosticsRouter.PumpDiagnosticsAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.AreEqual(1, publishedCount);
		Assert.AreEqual("Current warning.", lastMessage);
	}

	[TestMethod]
	public async Task PumpDiagnosticsAsync_WhenSubscriberThrows_LogsWarningAndContinuesProcessing()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>(), s_unfencedDiagnosticsHooks);
		StartCallbackPump(client);
		int publishedCount = 0;
		var publishedMessages = new List<string>();

		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			publishedCount++;

			if (eventArgs.Parameters.Diagnostics?[0].Message is { } message)
				publishedMessages.Add(message);

			if (publishedCount == 1)
				throw new InvalidOperationException("Simulated diagnostics subscriber failure.");

			client.CancelLifetime();
		};

		client.DiagnosticsRouter.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters("file:///C:/Workspace/first.ext", "First warning."));
		client.DiagnosticsRouter.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters("file:///C:/Workspace/second.ext", "Second warning."));

		await client.DiagnosticsRouter.PumpDiagnosticsAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.AreEqual(2, publishedCount);
		CollectionAssert.AreEquivalent(new[] { "First warning.", "Second warning." }, publishedMessages);

		Assert.IsTrue(logScope.Logs.Any(log => log.Contains("Diagnostics handler threw", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("Simulated diagnostics subscriber failure.", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public async Task PumpDiagnosticsAsync_SlowSubscriberDoesNotBlockLaterSubscriber()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logger: null, testHooks: s_unfencedDiagnosticsHooks);
		StartCallbackPump(client);
		var firstSubscriberEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var firstSubscriberCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondSubscriberObserved = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstSubscriberToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			firstSubscriberEntered.TrySetResult(true);
			allowFirstSubscriberToFinish.Task.GetAwaiter().GetResult();
			firstSubscriberCompleted.TrySetResult(true);
		};

		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			secondSubscriberObserved.TrySetResult(eventArgs.Parameters.Diagnostics?[0].Message);
			client.CancelLifetime();
		};

		Task diagnosticsPumpTask = client.DiagnosticsRouter.PumpDiagnosticsAsync();

		client.DiagnosticsRouter.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters("file:///C:/Workspace/test.ext", "Current warning."));

		await Task.WhenAll(
			firstSubscriberEntered.Task.WaitAsync(TimeSpan.FromSeconds(1)),
			secondSubscriberObserved.Task.WaitAsync(TimeSpan.FromSeconds(1))).ConfigureAwait(false);

		Assert.AreEqual("Current warning.", await secondSubscriberObserved.Task.ConfigureAwait(false));

		allowFirstSubscriberToFinish.TrySetResult(true);

		await firstSubscriberCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
		await diagnosticsPumpTask.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task PumpDiagnosticsAsync_SubscribersReceiveIndependentDiagnosticsSnapshots()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logger: null, testHooks: s_unfencedDiagnosticsHooks);
		StartCallbackPump(client);
		var firstSubscriberEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondSubscriberObserved = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		IReadOnlyList<DiagnosticPayload>? firstSubscriberDiagnostics = null;
		IReadOnlyList<DiagnosticPayload>? secondSubscriberDiagnostics = null;

		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			firstSubscriberDiagnostics = eventArgs.Parameters.Diagnostics;
			firstSubscriberEntered.TrySetResult(true);
		};

		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			firstSubscriberEntered.Task.GetAwaiter().GetResult();
			secondSubscriberDiagnostics = eventArgs.Parameters.Diagnostics;
			secondSubscriberObserved.TrySetResult(eventArgs.Parameters.Diagnostics?[0].Message);
			client.CancelLifetime();
		};

		Task diagnosticsPumpTask = client.DiagnosticsRouter.PumpDiagnosticsAsync();

		client.DiagnosticsRouter.QueueDiagnosticsPublished(0L,
			CreateDiagnosticsParameters("file:///C:/Workspace/test.ext", "Original warning."));

		Assert.AreEqual("Original warning.",
			await secondSubscriberObserved.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false));

		// Each subscriber receives its own detached diagnostics sequence instead of sharing one instance.
		Assert.IsNotNull(firstSubscriberDiagnostics);
		Assert.IsNotNull(secondSubscriberDiagnostics);
		Assert.IsFalse(ReferenceEquals(firstSubscriberDiagnostics, secondSubscriberDiagnostics));

		await diagnosticsPumpTask.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task InvokeDiagnosticsPublished_WhenSubscriberIsBusy_CoalescesPendingPayloadsPerDocument()
	{
		const string uri = "file:///C:/Workspace/test.ext";

		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		var firstInvocationEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstInvocationToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondInvocationMessage = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var unexpectedThirdInvocation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int invocationCount = 0;

		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			int currentInvocation = Interlocked.Increment(ref invocationCount);
			string? message = eventArgs.Parameters.Diagnostics?[0].Message;

			if (currentInvocation == 1)
			{
				firstInvocationEntered.TrySetResult(true);
				allowFirstInvocationToFinish.Task.GetAwaiter().GetResult();
				return;
			}

			if (currentInvocation == 2)
			{
				secondInvocationMessage.TrySetResult(message);
				return;
			}

			unexpectedThirdInvocation.TrySetResult(true);
		};

		var documentKey = DiagnosticsDocumentKey.FromUri(uri);

		client.DiagnosticsRouter.InvokeDiagnosticsPublished(documentKey, CreateDiagnosticsParameters(uri, "First warning."));
		await firstInvocationEntered.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

		client.DiagnosticsRouter.InvokeDiagnosticsPublished(documentKey, CreateDiagnosticsParameters(uri, "Second warning."));
		client.DiagnosticsRouter.InvokeDiagnosticsPublished(documentKey, CreateDiagnosticsParameters(uri, "Third warning."));
		client.DiagnosticsRouter.InvokeDiagnosticsPublished(documentKey, CreateDiagnosticsParameters(uri, "Latest warning."));

		allowFirstInvocationToFinish.TrySetResult(true);
		Assert.AreEqual("Latest warning.", await secondInvocationMessage.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false));

		await client.DiagnosticsRouter.WaitForDrainAsync().WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsFalse(unexpectedThirdInvocation.Task.IsCompleted);
		Assert.AreEqual(2, Volatile.Read(ref invocationCount));
	}

	[TestMethod]
	public async Task PumpDiagnosticsAsync_CoalescesRepeatedUpdatesWhileSubscriberIsBusy()
	{
		const string uri = "file:///C:/Workspace/test.ext";

		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logger: null, testHooks: s_unfencedDiagnosticsHooks);
		StartCallbackPump(client);
		var firstHandlerEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstHandlerToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondHandlerMessage = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var unexpectedThirdHandler = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var latestUpdateDelivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int publishedCount = 0;

		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			int invocationCount = Interlocked.Increment(ref publishedCount);
			string? message = eventArgs.Parameters.Diagnostics?[0].Message;

			if (invocationCount == 1)
			{
				firstHandlerEntered.TrySetResult(true);
				allowFirstHandlerToFinish.Task.GetAwaiter().GetResult();
				return;
			}

			if (invocationCount == 2)
			{
				secondHandlerMessage.TrySetResult(message);
				client.CancelLifetime();
				return;
			}

			unexpectedThirdHandler.TrySetResult(true);
		};

		// The observer subscriber signals when the pipeline delivered the newest update. Subscribers are dispatched in
		// subscription order and per-key delivery is monotonic, so the busy subscriber's pending slot already holds that
		// payload at that point. Waiting for the pipeline to catch up before releasing the busy handler keeps the
		// assertion independent of thread scheduling: intermediate updates may be collapsed by any pipeline stage, and
		// the coalescing guarantee under test is that the busy subscriber resumes with the newest delivered payload.
		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			if (string.Equals(eventArgs.Parameters.Diagnostics?[0].Message, "Latest warning.", StringComparison.Ordinal))
				latestUpdateDelivered.TrySetResult(true);
		};

		Task diagnosticsPumpTask = client.DiagnosticsRouter.PumpDiagnosticsAsync();

		client.DiagnosticsRouter.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters(uri, "First warning."));
		await firstHandlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

		client.DiagnosticsRouter.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters(uri, "Second warning."));
		client.DiagnosticsRouter.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters(uri, "Third warning."));
		client.DiagnosticsRouter.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters(uri, "Latest warning."));

		await latestUpdateDelivered.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

		allowFirstHandlerToFinish.TrySetResult(true);

		Assert.AreEqual("Latest warning.", await secondHandlerMessage.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false));
		await diagnosticsPumpTask.ConfigureAwait(false);
		await client.DiagnosticsRouter.WaitForDrainAsync().WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsFalse(unexpectedThirdHandler.Task.IsCompleted);
		Assert.AreEqual(2, Volatile.Read(ref publishedCount));
	}

	[TestMethod]
	public async Task InvokeDiagnosticsPublished_AfterUnsubscribe_DoesNotDeliverQueuedCallbacks()
	{
		const string uri = "file:///C:/Workspace/test.ext";

		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		var firstInvocationEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstInvocationToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var unexpectedSecondInvocation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int invocationCount = 0;

		void handler(object? _, DiagnosticsPublishedEventArgs eventArgs)
		{
			int currentInvocation = Interlocked.Increment(ref invocationCount);

			if (currentInvocation == 1)
			{
				firstInvocationEntered.TrySetResult(true);
				allowFirstInvocationToFinish.Task.GetAwaiter().GetResult();
				return;
			}

			unexpectedSecondInvocation.TrySetResult(true);
		}

		client.DiagnosticsPublished += handler;

		var documentKey = DiagnosticsDocumentKey.FromUri(uri);

		client.DiagnosticsRouter.InvokeDiagnosticsPublished(documentKey, CreateDiagnosticsParameters(uri, "First warning."));
		await firstInvocationEntered.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

		client.DiagnosticsPublished -= handler;

		for (int i = 0; i < 5; i++)
			client.DiagnosticsRouter.InvokeDiagnosticsPublished(documentKey, CreateDiagnosticsParameters(uri, "Later warning."));

		allowFirstInvocationToFinish.TrySetResult(true);

		await client.DiagnosticsRouter.WaitForDrainAsync().WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsFalse(unexpectedSecondInvocation.Task.IsCompleted);
		Assert.AreEqual(1, Volatile.Read(ref invocationCount));
	}

	[TestMethod]
	public async Task PumpDiagnosticsAsync_DoesNotCoalesceNonFileUrisThatDifferOnlyByCase()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logger: null, testHooks: s_unfencedDiagnosticsHooks);
		StartCallbackPump(client);
		var publishedMessages = new List<string>();
		var bothDelivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			publishedMessages.Add(eventArgs.Parameters.Diagnostics?[0].Message ?? string.Empty);

			if (publishedMessages.Count == 2)
			{
				bothDelivered.TrySetResult(true);
				client.CancelLifetime();
			}
		};

		// The manually driven diagnostics pump only completes once the lifetime is canceled.
		Task diagnosticsPumpTask = client.DiagnosticsRouter.PumpDiagnosticsAsync();

		// A non-file URI names an opaque document, so a case-only difference is significant even on a host whose
		// file system is case-insensitive; only a file URI coalesces with the local-path identity.
		client.DiagnosticsRouter.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters("untitled:Doc", "Upper warning."));
		client.DiagnosticsRouter.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters("untitled:doc", "Lower warning."));

		await bothDelivered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
		await diagnosticsPumpTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		CollectionAssert.AreEquivalent(new[] { "Upper warning.", "Lower warning." }, publishedMessages);
	}

	[TestMethod]
	public async Task Dispose_DropsPayloadQueuedForSubscriberBeforeDisposal()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		const string documentKey = "untitled:Doc";
		var handlerEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowHandlerToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var unexpectedSecondDelivery = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int deliveryCount = 0;

		client.DiagnosticsPublished += (_, _) =>
		{
			if (Interlocked.Increment(ref deliveryCount) > 1)
			{
				unexpectedSecondDelivery.TrySetResult(true);
				return;
			}

			handlerEntered.TrySetResult(true);
			allowHandlerToFinish.Task.GetAwaiter().GetResult();
		};

		// Dispatching directly reaches the subscriber synchronously, so the second payload is deterministically
		// pending (the busy subscriber drain can only pick it up after the first handler returns) before disposal.
		var diagnosticsKey = new DiagnosticsDocumentKey(false, documentKey);

		client.DiagnosticsRouter.InvokeDiagnosticsPublished(diagnosticsKey, CreateDiagnosticsParameters(documentKey, "First warning."));
		await handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		client.DiagnosticsRouter.InvokeDiagnosticsPublished(diagnosticsKey, CreateDiagnosticsParameters(documentKey, "Queued warning."));

		await client.DisposeAsync().ConfigureAwait(false);
		allowHandlerToFinish.TrySetResult(true);

		await client.DiagnosticsRouter.WaitForDrainAsync().WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsFalse(unexpectedSecondDelivery.Task.IsCompleted);
		Assert.AreEqual(1, Volatile.Read(ref deliveryCount));
	}

	// Starts the background callback pump explicitly: the client constructor no longer starts background loops,
	// so tests that expect asynchronous subscriber dispatch (diagnostics or semantic-token refresh) must run the
	// callback pump themselves (the diagnostics-pump tests drive the diagnostics pump separately).
	private static void StartCallbackPump(LanguageServerClient client)
		=> client.DiagnosticsRouter.EnsurePumpsRunning(includeDiagnosticsPump: false);
}
