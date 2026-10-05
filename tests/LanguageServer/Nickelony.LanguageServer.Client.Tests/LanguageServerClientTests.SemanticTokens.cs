namespace Nickelony.LanguageServer.Client.Tests;

public sealed partial class LanguageServerClientTests
{
	[TestMethod]
	public async Task RefreshSemanticTokensAsync_RaisesEventAndReturnsNull()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		StartServerMessageDispatcher(client);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		var refreshRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		client.SemanticTokensRefreshRequested += (_, _) => refreshRequested.TrySetResult(true);

		object? result = await rpcTarget.RefreshSemanticTokensAsync().ConfigureAwait(false);
		Assert.IsNull(result);

		await refreshRequested.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task RefreshSemanticTokensAsync_ReturnsBeforeSlowSubscriberCompletes()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		StartServerMessageDispatcher(client);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		var handlerEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowHandlerToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		client.SemanticTokensRefreshRequested += (_, _) =>
		{
			handlerEntered.TrySetResult(true);
			allowHandlerToFinish.Task.GetAwaiter().GetResult();
		};

		Task<object?> refreshTask = rpcTarget.RefreshSemanticTokensAsync();

		// The refresh completion is the event under test; the bound only needs to accommodate a loaded machine.
		Task completedTask = await Task.WhenAny(refreshTask, Task.Delay(TestPolling.DefaultTimeout, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreSame(refreshTask, completedTask);
		Assert.IsNull(await refreshTask.ConfigureAwait(false));

		await handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false);
		allowHandlerToFinish.TrySetResult(true);
	}

	[TestMethod]
	public async Task RefreshSemanticTokensAsync_SlowSubscriberDoesNotBlockLaterSubscriber()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		StartServerMessageDispatcher(client);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		var firstSubscriberEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondSubscriberObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstSubscriberToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		client.SemanticTokensRefreshRequested += (_, _) =>
		{
			firstSubscriberEntered.TrySetResult(true);
			allowFirstSubscriberToFinish.Task.GetAwaiter().GetResult();
		};

		client.SemanticTokensRefreshRequested += (_, _) => secondSubscriberObserved.TrySetResult(true);

		object? result = await rpcTarget.RefreshSemanticTokensAsync().ConfigureAwait(false);

		Assert.IsNull(result);

		await Task.WhenAll(
				firstSubscriberEntered.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken),
				secondSubscriberObserved.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken)
			)
			.ConfigureAwait(false);

		allowFirstSubscriberToFinish.TrySetResult(true);
	}

	[TestMethod]
	public async Task InvokeSemanticTokensRefreshRequested_WhenSubscriberIsBusy_CoalescesPendingSignalsPerSubscriber()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		var firstInvocationEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstInvocationToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondInvocationEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var unexpectedThirdInvocation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int invocationCount = 0;

		client.SemanticTokensRefreshRequested += (_, _) =>
		{
			int currentInvocation = Interlocked.Increment(ref invocationCount);

			if (currentInvocation == 1)
			{
				firstInvocationEntered.TrySetResult(true);
				allowFirstInvocationToFinish.Task.GetAwaiter().GetResult();
				return;
			}

			if (currentInvocation == 2)
			{
				secondInvocationEntered.TrySetResult(true);
				return;
			}

			unexpectedThirdInvocation.TrySetResult(true);
		};

		client.Composition.Router.InvokeSemanticTokensRefreshRequested();
		await firstInvocationEntered.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false);

		for (int i = 0; i < 10; i++)
			client.Composition.Router.InvokeSemanticTokensRefreshRequested();

		allowFirstInvocationToFinish.TrySetResult(true);
		await secondInvocationEntered.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false);

		await TestPolling
			.UntilAsync(() => client.Composition.Router.IsDrained, TestPolling.DefaultTimeout, "The router should deliver every queued payload.")
			.ConfigureAwait(false);

		Assert.IsFalse(unexpectedThirdInvocation.Task.IsCompleted);
		Assert.AreEqual(2, Volatile.Read(ref invocationCount));
	}

	[TestMethod]
	public async Task RefreshSemanticTokensAsync_CoalescesRepeatedRequestsWhileSubscriberIsBusy()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		StartServerMessageDispatcher(client);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		var firstHandlerEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstHandlerToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondHandlerEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var unexpectedThirdHandler = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int refreshRequestedCount = 0;

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		client.SemanticTokensRefreshRequested += (_, _) =>
		{
			int invocationCount = Interlocked.Increment(ref refreshRequestedCount);

			if (invocationCount == 1)
			{
				firstHandlerEntered.TrySetResult(true);
				allowFirstHandlerToFinish.Task.GetAwaiter().GetResult();
				return;
			}

			if (invocationCount == 2)
			{
				secondHandlerEntered.TrySetResult(true);
				return;
			}

			unexpectedThirdHandler.TrySetResult(true);
		};

		Assert.IsNull(await rpcTarget.RefreshSemanticTokensAsync().ConfigureAwait(false));
		await firstHandlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false);

		Task<object?>[] repeatedRefreshRequests =
		[
			rpcTarget.RefreshSemanticTokensAsync(),
			rpcTarget.RefreshSemanticTokensAsync(),
			rpcTarget.RefreshSemanticTokensAsync(),
			rpcTarget.RefreshSemanticTokensAsync(),
			rpcTarget.RefreshSemanticTokensAsync(),
		];

		object?[] repeatedResults = await Task.WhenAll(repeatedRefreshRequests)
			.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.IsTrue(repeatedResults.All(result => result is null));

		allowFirstHandlerToFinish.TrySetResult(true);
		await secondHandlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false);

		await TestPolling
			.UntilAsync(() => client.Composition.Router.IsDrained, TestPolling.DefaultTimeout, "The router should deliver every queued payload.")
			.ConfigureAwait(false);

		Assert.IsFalse(unexpectedThirdHandler.Task.IsCompleted);
		Assert.AreEqual(2, Volatile.Read(ref refreshRequestedCount));
	}

	[TestMethod]
	public async Task InvokeSemanticTokensRefreshRequested_AfterUnsubscribe_DoesNotDeliverQueuedCallbacks()
	{
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

		void handler(object? sender, EventArgs e)
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

		client.SemanticTokensRefreshRequested += handler;

		client.Composition.Router.InvokeSemanticTokensRefreshRequested();
		await firstInvocationEntered.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false);

		client.SemanticTokensRefreshRequested -= handler;

		for (int i = 0; i < 5; i++)
			client.Composition.Router.InvokeSemanticTokensRefreshRequested();

		allowFirstInvocationToFinish.TrySetResult(true);

		await TestPolling
			.UntilAsync(() => client.Composition.Router.IsDrained, TestPolling.DefaultTimeout, "The router should deliver every queued payload.")
			.ConfigureAwait(false);

		Assert.IsFalse(unexpectedSecondInvocation.Task.IsCompleted);
		Assert.AreEqual(1, Volatile.Read(ref invocationCount));
	}

	[TestMethod]
	public async Task RefreshSemanticTokensAsync_IgnoresStaleConnectionGeneration()
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

		int refreshRequestedCount = 0;

		SetActiveSession(client, newSession);
		SetReadyState(client, true);

		client.SemanticTokensRefreshRequested += (_, _) => refreshRequestedCount++;

		ClientRpcTarget oldRpcTarget = CreateRpcTarget(client, GetConnectionGeneration(oldSession));
		object? result = await oldRpcTarget.RefreshSemanticTokensAsync().ConfigureAwait(false);

		// A broken gate would enqueue the refresh for asynchronous server-message dispatch; wait for the pipeline to
		// settle before asserting the drop.
		await TestPolling
			.UntilAsync(() => client.Composition.Router.IsDrained, TestPolling.DefaultTimeout, "The router should settle.")
			.ConfigureAwait(false);

		Assert.AreEqual(0, refreshRequestedCount);
		Assert.IsNull(result);
	}

	[TestMethod]
	public async Task RefreshSemanticTokensAsync_WhenConnectionIsAttachedButNotReady_DeliversRefreshRequest()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		StartServerMessageDispatcher(client);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		var refreshRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		SetActiveSession(client, session);

		client.SemanticTokensRefreshRequested += (_, _) => refreshRequested.TrySetResult(true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));
		object? result = await rpcTarget.RefreshSemanticTokensAsync().ConfigureAwait(false);

		Assert.IsNull(result);

		await refreshRequested.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task RefreshSemanticTokensAsync_IgnoresUnhealthyConnectionGeneration()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		StartServerMessageDispatcher(client);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		int refreshRequestedCount = 0;

		SetActiveSession(client, session);
		SetReadyState(client, true);

		client.SemanticTokensRefreshRequested += (_, _) => refreshRequestedCount++;

		client.TryMarkConnectionUnhealthy(client.ConnectionGeneration);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));
		object? result = await rpcTarget.RefreshSemanticTokensAsync().ConfigureAwait(false);

		// A broken gate would enqueue the refresh for asynchronous server-message dispatch; wait for the pipeline to
		// settle before asserting the drop.
		await TestPolling
			.UntilAsync(() => client.Composition.Router.IsDrained, TestPolling.DefaultTimeout, "The router should settle.")
			.ConfigureAwait(false);

		Assert.AreEqual(0, refreshRequestedCount);
		Assert.IsNull(result);
	}

	[TestMethod]
	public void QueueSemanticTokensRefreshRequested_AfterTeardown_IsDroppedAndTheRouterStaysDrained()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		client.Composition.Router.CompleteSignalChannels();
		client.Composition.Router.QueueSemanticTokensRefreshRequested();

		// A refresh queued after the signal channels completed has no loop left to deliver it; it must be dropped
		// instead of pinning the router out of its drained state for the remainder of the client's lifetime.
		Assert.IsTrue(client.Composition.Router.IsDrained);
	}
}
