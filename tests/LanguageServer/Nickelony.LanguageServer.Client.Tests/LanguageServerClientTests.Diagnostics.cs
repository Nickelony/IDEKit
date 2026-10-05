using Microsoft.Extensions.Logging;
using Nickelony.Infrastructure;

namespace Nickelony.LanguageServer.Client.Tests;

public sealed partial class LanguageServerClientTests
{
	// These tests queue payloads directly instead of through an active session, so they replace the production
	// connection-generation gate with one that accepts the session-less generation.
	private static readonly Func<long, bool> s_unfencedServerMessageGate = static _ => true;

	[TestMethod]
	public async Task PublishDiagnostics_WhenConnectionIsAttachedButNotReady_DeliversDiagnostics()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 6,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		var publishedMessage = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

		SetActiveSession(client, session);
		client.Composition.Router.EnsureServerMessageAndDiagnosticsLoopsRunning();

		client.DiagnosticsPublished += (_, eventArgs) => publishedMessage.TrySetResult(eventArgs.Parameters.Diagnostics?[0].Message);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		rpcTarget.PublishDiagnostics(CreateDiagnosticsParameters(TestPaths.FileUri("test.ext"), "Initial warning."));

		Assert.AreEqual(
			"Initial warning.",
			await publishedMessage.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false)
		);
	}

	[TestMethod]
	public async Task PublishDiagnostics_IgnoresUnhealthyConnectionGeneration()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 6,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		var publishedMessage = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		// Both loops must run: the diagnostics coalescer applies the generation gate and the server-message dispatcher delivers
		// queued payloads, so only this combination can prove the payload is dropped rather than undelivered.
		client.Composition.Router.EnsureServerMessageAndDiagnosticsLoopsRunning();

		client.DiagnosticsPublished += (_, eventArgs) => publishedMessage.TrySetResult(eventArgs.Parameters.Diagnostics?[0].Message);

		client.TryMarkConnectionUnhealthy(client.ConnectionGeneration);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		rpcTarget.PublishDiagnostics(CreateDiagnosticsParameters(TestPaths.FileUri("test.ext"), "Ignored warning."));

		await TestPolling
			.UntilAsync(() => client.Composition.Router.IsDrained, TestPolling.DefaultTimeout, "The router should settle.")
			.ConfigureAwait(false);

		Assert.IsFalse(publishedMessage.Task.IsCompleted);
	}

	[TestMethod]
	public async Task PublishDiagnostics_WhenPayloadIsDegraded_DropsNotificationWithoutRaising()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 6,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		// Both loops run, so a payload that the target failed to drop would reach the subscriber.
		client.Composition.Router.EnsureServerMessageAndDiagnosticsLoopsRunning();

		int publishedCount = 0;
		client.DiagnosticsPublished += (_, _) => Interlocked.Increment(ref publishedCount);

		// A degraded payload is what the converter produces for a malformed notification; the RPC
		// target must drop it so it can never surface as "clear diagnostics for this document".
		var degradedPayload = new PublishDiagnosticsParams(TestPaths.FileUri("test.ext"), null, null) { IsDegraded = true };

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));
		rpcTarget.PublishDiagnostics(degradedPayload);

		// The drop is synchronous, so nothing was queued; a payload that was queued is delivered before the router drains.
		await TestPolling
			.UntilAsync(() => client.Composition.Router.IsDrained, TestPolling.DefaultTimeout, "The router should settle.")
			.ConfigureAwait(false);

		Assert.AreEqual(0, Volatile.Read(ref publishedCount));
	}

	[TestMethod]
	public async Task DiagnosticsCoalescer_CoalescesQueuedDiagnosticsByFile()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions,
			logger: null,
			serverMessageGate: s_unfencedServerMessageGate
		);

		StartServerMessageDispatcher(client);

		int publishedCount = 0;
		string? lastMessage = null;

		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			publishedCount++;
			lastMessage = eventArgs.Parameters.Diagnostics?[0].Message;
			client.Composition.Lifetime.Cancel();
		};

		client.Composition.Router.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters(TestPaths.FileUri("test.ext"), "Stale warning."));
		client.Composition.Router.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters(TestPaths.FileUri("test.ext"), "Current warning."));

		await client
			.Composition.Router.DiagnosticsCoalescer.RunAsync()
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(1, publishedCount);
		Assert.AreEqual("Current warning.", lastMessage);
	}

	[TestMethod]
	public async Task DiagnosticsCoalescer_CoalescesWindowsFileUrisThatDifferOnlyByCase()
	{
		if (!LocalPathRules.IgnoresCaseByDefault)
			Assert.Inconclusive("The test requires case-insensitive local-path identity on the current host.");

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions,
			logger: null,
			serverMessageGate: s_unfencedServerMessageGate
		);

		StartServerMessageDispatcher(client);

		int publishedCount = 0;
		string? lastMessage = null;

		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			publishedCount++;
			lastMessage = eventArgs.Parameters.Diagnostics?[0].Message;
			client.Composition.Lifetime.Cancel();
		};

		client.Composition.Router.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters(TestPaths.FileUri("Test.ext"), "First warning."));

		client.Composition.Router.QueueDiagnosticsPublished(
			0L,
			CreateDiagnosticsParameters(TestPaths.FileUri("test.ext").ToLowerInvariant(), "Latest warning.")
		);

		await client
			.Composition.Router.DiagnosticsCoalescer.RunAsync()
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(1, publishedCount);
		Assert.AreEqual("Latest warning.", lastMessage);
	}

	[TestMethod]
	public async Task DiagnosticsCoalescer_DropsQueuedDiagnosticsFromInactiveConnectionGeneration()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		StartServerMessageDispatcher(client);

		ConnectionSession oldSession = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		ConnectionSession newSession = CreateConnectionSession(
			client,
			generation: 2,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		int publishedCount = 0;
		string? lastMessage = null;

		SetActiveSession(client, newSession);

		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			publishedCount++;
			lastMessage = eventArgs.Parameters.Diagnostics?[0].Message;
			client.Composition.Lifetime.Cancel();
		};

		client.Composition.Router.QueueDiagnosticsPublished(
			GetConnectionGeneration(oldSession),
			CreateDiagnosticsParameters(TestPaths.FileUri("stale.ext"), "Stale warning.")
		);

		client.Composition.Router.QueueDiagnosticsPublished(
			GetConnectionGeneration(newSession),
			CreateDiagnosticsParameters(TestPaths.FileUri("current.ext"), "Current warning.")
		);

		await client
			.Composition.Router.DiagnosticsCoalescer.RunAsync()
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(1, publishedCount);
		Assert.AreEqual("Current warning.", lastMessage);
	}

	[TestMethod]
	public async Task DiagnosticsCoalescer_SameUriStaleGenerationDoesNotOverwriteCurrentGenerationPayload()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		StartServerMessageDispatcher(client);

		ConnectionSession oldSession = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		ConnectionSession newSession = CreateConnectionSession(
			client,
			generation: 2,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		int publishedCount = 0;
		string? lastMessage = null;

		SetActiveSession(client, newSession);

		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			publishedCount++;
			lastMessage = eventArgs.Parameters.Diagnostics?[0].Message;
			client.Composition.Lifetime.Cancel();
		};

		client.Composition.Router.QueueDiagnosticsPublished(
			GetConnectionGeneration(newSession),
			CreateDiagnosticsParameters(TestPaths.FileUri("test.ext"), "Current warning.")
		);

		client.Composition.Router.QueueDiagnosticsPublished(
			GetConnectionGeneration(oldSession),
			CreateDiagnosticsParameters(TestPaths.FileUri("test.ext"), "Stale warning.")
		);

		await client
			.Composition.Router.DiagnosticsCoalescer.RunAsync()
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(1, publishedCount);
		Assert.AreEqual("Current warning.", lastMessage);
	}

	[TestMethod]
	public async Task DiagnosticsCoalescer_WhenSubscriberThrows_LogsWarningAndContinuesProcessing()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>(),
			serverMessageGate: s_unfencedServerMessageGate
		);

		StartServerMessageDispatcher(client);
		int publishedCount = 0;
		var publishedMessages = new List<string>();

		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			publishedCount++;

			if (eventArgs.Parameters.Diagnostics?[0].Message is { } message)
				publishedMessages.Add(message);

			if (publishedCount == 1)
				throw new InvalidOperationException("Simulated diagnostics subscriber failure.");

			client.Composition.Lifetime.Cancel();
		};

		client.Composition.Router.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters(TestPaths.FileUri("first.ext"), "First warning."));
		client.Composition.Router.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters(TestPaths.FileUri("second.ext"), "Second warning."));

		await client
			.Composition.Router.DiagnosticsCoalescer.RunAsync()
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(2, publishedCount);

		Assert.AreSequenceEqual(
			["First warning.", "Second warning."],
			publishedMessages,
			Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder
		);

		Assert.Contains(
			log =>
				log.Contains("Diagnostics handler threw", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("Simulated diagnostics subscriber failure.", StringComparison.Ordinal),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task DiagnosticsCoalescer_SlowSubscriberDoesNotBlockLaterSubscriber()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions,
			logger: null,
			serverMessageGate: s_unfencedServerMessageGate
		);

		StartServerMessageDispatcher(client);

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
			client.Composition.Lifetime.Cancel();
		};

		Task diagnosticsCoalescerTask = client.Composition.Router.DiagnosticsCoalescer.RunAsync();

		client.Composition.Router.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters(TestPaths.FileUri("test.ext"), "Current warning."));

		await Task.WhenAll(
				firstSubscriberEntered.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken),
				secondSubscriberObserved.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken)
			)
			.ConfigureAwait(false);

		Assert.AreEqual("Current warning.", await secondSubscriberObserved.Task.ConfigureAwait(false));

		allowFirstSubscriberToFinish.TrySetResult(true);

		await firstSubscriberCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false);
		await diagnosticsCoalescerTask.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task DiagnosticsCoalescer_SubscribersReceiveIndependentDiagnosticsSnapshots()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions,
			logger: null,
			serverMessageGate: s_unfencedServerMessageGate
		);

		StartServerMessageDispatcher(client);

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
			client.Composition.Lifetime.Cancel();
		};

		Task diagnosticsCoalescerTask = client.Composition.Router.DiagnosticsCoalescer.RunAsync();

		client.Composition.Router.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters(TestPaths.FileUri("test.ext"), "Original warning."));

		Assert.AreEqual(
			"Original warning.",
			await secondSubscriberObserved.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false)
		);

		// Each subscriber receives its own detached diagnostics sequence instead of sharing one instance.
		Assert.IsNotNull(firstSubscriberDiagnostics);
		Assert.IsNotNull(secondSubscriberDiagnostics);
		Assert.IsFalse(ReferenceEquals(firstSubscriberDiagnostics, secondSubscriberDiagnostics));

		await diagnosticsCoalescerTask.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task InvokeDiagnosticsPublished_WhenSubscriberIsBusy_CoalescesPendingPayloadsPerDocument()
	{
		string uri = TestPaths.FileUri("test.ext");

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

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

		client.Composition.Router.InvokeDiagnosticsPublished(documentKey, CreateDiagnosticsParameters(uri, "First warning."));
		await firstInvocationEntered.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false);

		client.Composition.Router.InvokeDiagnosticsPublished(documentKey, CreateDiagnosticsParameters(uri, "Second warning."));
		client.Composition.Router.InvokeDiagnosticsPublished(documentKey, CreateDiagnosticsParameters(uri, "Third warning."));
		client.Composition.Router.InvokeDiagnosticsPublished(documentKey, CreateDiagnosticsParameters(uri, "Latest warning."));

		allowFirstInvocationToFinish.TrySetResult(true);

		Assert.AreEqual(
			"Latest warning.",
			await secondInvocationMessage.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false)
		);

		await TestPolling
			.UntilAsync(() => client.Composition.Router.IsDrained, TestPolling.DefaultTimeout, "The router should deliver every queued payload.")
			.ConfigureAwait(false);

		Assert.IsFalse(unexpectedThirdInvocation.Task.IsCompleted);
		Assert.AreEqual(2, Volatile.Read(ref invocationCount));
	}

	[TestMethod]
	public async Task DiagnosticsCoalescer_CoalescesRepeatedUpdatesWhileSubscriberIsBusy()
	{
		string uri = TestPaths.FileUri("test.ext");

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions,
			logger: null,
			serverMessageGate: s_unfencedServerMessageGate
		);

		StartServerMessageDispatcher(client);

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
				client.Composition.Lifetime.Cancel();
				return;
			}

			unexpectedThirdHandler.TrySetResult(true);
		};

		// Waits for the newest update to be delivered before releasing the busy handler, so the assertion is independent of thread scheduling.
		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			if (string.Equals(eventArgs.Parameters.Diagnostics?[0].Message, "Latest warning.", StringComparison.Ordinal))
				latestUpdateDelivered.TrySetResult(true);
		};

		Task diagnosticsCoalescerTask = client.Composition.Router.DiagnosticsCoalescer.RunAsync();

		client.Composition.Router.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters(uri, "First warning."));
		await firstHandlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false);

		client.Composition.Router.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters(uri, "Second warning."));
		client.Composition.Router.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters(uri, "Third warning."));
		client.Composition.Router.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters(uri, "Latest warning."));

		await latestUpdateDelivered.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false);

		allowFirstHandlerToFinish.TrySetResult(true);

		Assert.AreEqual(
			"Latest warning.",
			await secondHandlerMessage.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false)
		);

		await diagnosticsCoalescerTask.ConfigureAwait(false);

		await TestPolling
			.UntilAsync(() => client.Composition.Router.IsDrained, TestPolling.DefaultTimeout, "The router should deliver every queued payload.")
			.ConfigureAwait(false);

		Assert.IsFalse(unexpectedThirdHandler.Task.IsCompleted);
		Assert.AreEqual(2, Volatile.Read(ref publishedCount));
	}

	[TestMethod]
	public async Task InvokeDiagnosticsPublished_AfterUnsubscribe_DoesNotDeliverQueuedCallbacks()
	{
		string uri = TestPaths.FileUri("test.ext");

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

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

		client.Composition.Router.InvokeDiagnosticsPublished(documentKey, CreateDiagnosticsParameters(uri, "First warning."));
		await firstInvocationEntered.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false);

		client.DiagnosticsPublished -= handler;

		for (int i = 0; i < 5; i++)
			client.Composition.Router.InvokeDiagnosticsPublished(documentKey, CreateDiagnosticsParameters(uri, "Later warning."));

		allowFirstInvocationToFinish.TrySetResult(true);

		// The removed subscription has left the router, so its handler is observed through an absence window.
		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsFalse(unexpectedSecondInvocation.Task.IsCompleted);
		Assert.AreEqual(1, Volatile.Read(ref invocationCount));
	}

	[TestMethod]
	public async Task DiagnosticsCoalescer_DoesNotCoalesceNonFileUrisThatDifferOnlyByCase()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions,
			logger: null,
			serverMessageGate: s_unfencedServerMessageGate
		);

		StartServerMessageDispatcher(client);

		var publishedMessages = new List<string>();
		var bothDelivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		client.DiagnosticsPublished += (_, eventArgs) =>
		{
			publishedMessages.Add(eventArgs.Parameters.Diagnostics?[0].Message ?? string.Empty);

			if (publishedMessages.Count == 2)
			{
				bothDelivered.TrySetResult(true);
				client.Composition.Lifetime.Cancel();
			}
		};

		// The manually driven diagnostics coalescer only completes once the lifetime is canceled.
		Task diagnosticsCoalescerTask = client.Composition.Router.DiagnosticsCoalescer.RunAsync();

		// A non-file URI names an opaque document, so a case-only difference is significant even on a host whose
		// file system is case-insensitive; only a file URI coalesces with the local-path identity.
		client.Composition.Router.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters("untitled:Doc", "Upper warning."));
		client.Composition.Router.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters("untitled:doc", "Lower warning."));

		await bothDelivered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);
		await diagnosticsCoalescerTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreSequenceEqual(
			["Upper warning.", "Lower warning."],
			publishedMessages,
			Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder
		);
	}

	[TestMethod]
	public async Task Dispose_DropsPayloadQueuedForSubscriberBeforeDisposal()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

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
		// pending (the busy subscription can only pick it up after the first handler returns) before disposal.
		var diagnosticsKey = new DiagnosticsDocumentKey(false, documentKey);

		client.Composition.Router.InvokeDiagnosticsPublished(diagnosticsKey, CreateDiagnosticsParameters(documentKey, "First warning."));
		await handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		client.Composition.Router.InvokeDiagnosticsPublished(diagnosticsKey, CreateDiagnosticsParameters(documentKey, "Queued warning."));

		await client.DisposeAsync().ConfigureAwait(false);
		allowHandlerToFinish.TrySetResult(true);

		// Disposal completed the subscriber sets, so the router stops reporting the busy handler; the queued payload would
		// be delivered by that handler's drain within the window.
		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsFalse(unexpectedSecondDelivery.Task.IsCompleted);
		Assert.AreEqual(1, Volatile.Read(ref deliveryCount));
	}

	[TestMethod]
	public void QueueDiagnosticsPublished_AfterTeardown_IsDroppedAndTheRouterStaysDrained()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		client.Composition.Router.CompleteSignalChannels();
		client.Composition.Router.QueueDiagnosticsPublished(0L, CreateDiagnosticsParameters(TestPaths.FileUri("test.ext"), "Late warning."));

		// A payload queued after the signal channels completed has no loop left to publish it; it must be dropped
		// instead of staying pending - which would keep the router out of its drained state - for the client's
		// lifetime.
		Assert.IsTrue(client.Composition.Router.IsDrained);
	}

	[TestMethod]
	public void QueueDiagnosticRefreshRequested_AfterTeardown_IsDroppedAndTheRouterStaysDrained()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		client.Composition.Router.CompleteSignalChannels();
		client.Composition.Router.QueueDiagnosticRefreshRequested();

		// The diagnostic-refresh flag queued after the signal channels completed must be cleared for the same reason
		// as the diagnostics payload above.
		Assert.IsTrue(client.Composition.Router.IsDrained);
	}

	// Starts the background server-message dispatcher explicitly: the client constructor does not start background loops,
	// so tests that expect asynchronous subscriber dispatch (diagnostics or semantic-token refresh) must run the
	// server-message dispatcher themselves (the diagnostics-coalescer tests drive the diagnostics coalescer separately).
	private static void StartServerMessageDispatcher(LanguageServerClient client) => client.Composition.Router.EnsureServerMessageDispatcherRunning();
}
