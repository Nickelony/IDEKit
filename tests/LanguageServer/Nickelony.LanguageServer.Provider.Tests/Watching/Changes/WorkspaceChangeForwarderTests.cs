using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class WorkspaceChangeForwarderTests
{
	private static ProviderLifetime CreateDisposedDisposal()
	{
		var disposal = new ProviderLifetime();
		disposal.Signal();

		return disposal;
	}

	[TestMethod]
	public async Task DispatchAsync_Success_ForwardsImmediatelyWithoutBuffering()
	{
		bool ensureStartedCalled = false;
		IReadOnlyList<FileChange>? forwardedChanges = null;

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),

			ensureConnectionStartedAsync: _ =>
			{
				ensureStartedCalled = true;
				return Task.FromResult(true);
			}
		);

		FileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await forwarder
			.DispatchAsync(
				changes,

				(items, _) =>
				{
					forwardedChanges = [.. items];
					return Task.CompletedTask;
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		var replayRecorder = new CallbackRecorder();

		await forwarder.ReplayBufferedAsync(replayRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, replayRecorder.InvocationCount, "No buffered changes should remain.");
		Assert.IsTrue(ensureStartedCalled);
		Assert.IsNotNull(forwardedChanges);
		Assert.HasCount(1, forwardedChanges);
	}

	[TestMethod]
	public async Task DispatchAsync_WhenForwardingNotCurrentlyAllowed_DropsChanges()
	{
		bool ensureStartedCalled = false;
		bool forwardCalled = false;

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => false,
			disposal: new ProviderLifetime(),

			ensureConnectionStartedAsync: _ =>
			{
				ensureStartedCalled = true;
				return Task.FromResult(true);
			}
		);

		FileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await forwarder
			.DispatchAsync(
				changes,

				(_, _) =>
				{
					forwardCalled = true;
					return Task.CompletedTask;
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		var replayRecorder = new CallbackRecorder();

		await forwarder.ReplayBufferedAsync(replayRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, replayRecorder.InvocationCount, "Ignored changes should not be retained for replay.");
		Assert.IsFalse(ensureStartedCalled);
		Assert.IsFalse(forwardCalled);
	}

	[TestMethod]
	public async Task DispatchAsync_WhenStartupFails_BuffersAndReplayDispatchesChanges()
	{
		bool startResult = false;
		IReadOnlyList<FileChange>? replayedChanges = null;

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),
			ensureConnectionStartedAsync: _ => Task.FromResult(startResult)
		);

		FileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		var dispatchRecorder = new CallbackRecorder();

		await forwarder.DispatchAsync(changes, dispatchRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, dispatchRecorder.InvocationCount, "Dispatch should be buffered while startup fails.");

		startResult = true;

		await forwarder
			.ReplayBufferedAsync(
				(items, _) =>
				{
					replayedChanges = [.. items];
					return Task.CompletedTask;
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.IsNotNull(replayedChanges);
		Assert.HasCount(1, replayedChanges);
		Assert.AreEqual(changes[0].Path, replayedChanges[0].Path);
		Assert.AreEqual(changes[0].Kind, replayedChanges[0].Kind);
	}

	[TestMethod]
	public async Task DispatchAsync_DuringAProlongedOutage_KeepsTheBufferWithinItsLimitAndHandsTheRestToTheOverflowHook()
	{
		const int limit = 10;

		bool startResult = false;

		var collapsed = new List<FileChange>();
		var forwarded = new List<FileChange>();

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),
			ensureConnectionStartedAsync: _ => Task.FromResult(startResult),
			reportOverflow: collapsed.AddRange,
			bufferedPathLimit: limit
		);

		var dispatchRecorder = new CallbackRecorder();

		for (int i = 0; i < 25; i++)
		{
			FileChange[] change = [new($@"C:\Workspace\Scripts\file{i}.ext", FileChangeKind.Changed)];
			await forwarder.DispatchAsync(change, dispatchRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);
		}

		startResult = true;

		IReadOnlyList<FileChange> replayed = await forwarder
			.ReplayBufferedAsync(
				(items, _) =>
				{
					forwarded.AddRange(items);
					return Task.CompletedTask;
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.AreEqual(0, dispatchRecorder.InvocationCount, "Nothing reaches the server during the outage.");
		Assert.IsLessThanOrEqualTo(limit, replayed.Count);
		Assert.HasCount(22, collapsed, "The buffer collapses each time it holds eleven paths, so twice.");

		Assert.AreSequenceEqual(
			expected: [.. Enumerable.Range(0, 25).Select(i => $@"C:\Workspace\Scripts\file{i}.ext")],
			actual: [.. collapsed.Concat(forwarded).Select(change => change.Path)],
			order: Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder,
			message: "Every changed path reaches either the overflow hook or the replay."
		);
	}

	[TestMethod]
	public async Task DispatchAsync_WithoutAnOverflowHook_BuffersPastTheLimit()
	{
		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),
			ensureConnectionStartedAsync: _ => Task.FromResult(false),
			bufferedPathLimit: 2
		);

		var dispatchRecorder = new CallbackRecorder();

		for (int i = 0; i < 5; i++)
		{
			FileChange[] change = [new($@"C:\Workspace\Scripts\file{i}.ext", FileChangeKind.Changed)];
			await forwarder.DispatchAsync(change, dispatchRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);
		}

		IReadOnlyList<FileChange> replayed = await forwarder
			.ReplayBufferedAsync((_, _) => Task.CompletedTask, CancellationToken.None)
			.ConfigureAwait(false);

		Assert.HasCount(5, replayed);
	}

	[TestMethod]
	public async Task DispatchAsync_WhenEnsureStartedThrows_BuffersChangesAndDoesNotPropagate()
	{
		IReadOnlyList<FileChange>? replayedChanges = null;
		WorkspaceChangeForwardingFailure? reportedFailure = null;

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),
			ensureConnectionStartedAsync: _ => throw new InvalidOperationException("The connection could not be started."),
			logForwardingFailure: failure => reportedFailure = failure
		);

		FileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];
		var dispatchRecorder = new CallbackRecorder();

		// A throwing ensure-start must not escape into the watcher that dispatched the batch, and the batch must be
		// buffered for a later replay instead of being dropped.
		bool forwarded = await forwarder.DispatchAsync(changes, dispatchRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.IsFalse(forwarded);
		Assert.AreEqual(0, dispatchRecorder.InvocationCount);
		Assert.IsNotNull(reportedFailure);
		Assert.AreEqual(WorkspaceChangeForwardingFailureCodes.EnsureStartFailed, reportedFailure.Value.Code);
		Assert.IsFalse(reportedFailure.Value.WasDropped);
		Assert.AreEqual(changes[0].Path, reportedFailure.Value.FirstPath);

		await forwarder
			.ReplayBufferedAsync(
				(items, _) =>
				{
					replayedChanges = [.. items];
					return Task.CompletedTask;
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.IsNotNull(replayedChanges);
		Assert.HasCount(1, replayedChanges);
		Assert.AreEqual(changes[0].Path, replayedChanges[0].Path);
	}

	[TestMethod]
	public async Task DispatchAsync_WhenEnsureStartedThrowsObjectDisposed_BuffersChangesWithoutReportingFailure()
	{
		IReadOnlyList<FileChange>? replayedChanges = null;
		int logForwardingFailureCallCount = 0;

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),
			ensureConnectionStartedAsync: _ => throw new ObjectDisposedException("connection"),
			logForwardingFailure: _ => logForwardingFailureCallCount++
		);

		FileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];
		var dispatchRecorder = new CallbackRecorder();

		// A connection that is already disposed cannot start; the batch is buffered for a later replay, and the
		// disposed-connection case is expected rather than a forwarding failure, so nothing is reported.
		bool forwarded = await forwarder.DispatchAsync(changes, dispatchRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.IsFalse(forwarded);
		Assert.AreEqual(0, dispatchRecorder.InvocationCount);
		Assert.AreEqual(0, logForwardingFailureCallCount, "A disposed connection is not reported as a forwarding failure.");

		await forwarder
			.ReplayBufferedAsync(
				(items, _) =>
				{
					replayedChanges = [.. items];
					return Task.CompletedTask;
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.IsNotNull(replayedChanges);
		Assert.HasCount(1, replayedChanges);
		Assert.AreEqual(changes[0].Path, replayedChanges[0].Path);
	}

	[TestMethod]
	public async Task DispatchAsync_WhenEnsureStartedThrowsObjectDisposedWhileOwnerAlreadyDisposed_DoesNotBuffer()
	{
		int logForwardingFailureCallCount = 0;

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: CreateDisposedDisposal(),
			ensureConnectionStartedAsync: _ => throw new ObjectDisposedException("connection"),
			logForwardingFailure: _ => logForwardingFailureCallCount++
		);

		FileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];
		var dispatchRecorder = new CallbackRecorder();

		bool forwarded = await forwarder.DispatchAsync(changes, dispatchRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		var replayRecorder = new CallbackRecorder();

		await forwarder.ReplayBufferedAsync(replayRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.IsFalse(forwarded);
		Assert.AreEqual(0, dispatchRecorder.InvocationCount);
		Assert.AreEqual(0, logForwardingFailureCallCount);
		Assert.AreEqual(0, replayRecorder.InvocationCount, "A disposed owner does not retain buffered changes.");
	}

	[TestMethod]
	public async Task DispatchAsync_WhenIOExceptionDuringForwarding_BuffersChangesAndLogsTheFailure()
	{
		int logForwardingFailureCallCount = 0;

		WorkspaceChangeForwardingFailure? loggedFailure = null;
		IReadOnlyList<FileChange>? replayedChanges = null;

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),
			ensureConnectionStartedAsync: _ => Task.FromResult(true),

			logForwardingFailure: failure =>
			{
				logForwardingFailureCallCount++;
				loggedFailure = failure;
			}
		);

		FileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await forwarder
			.DispatchAsync(changes, (_, _) => throw new IOException("Simulated forwarding failure."), CancellationToken.None)
			.ConfigureAwait(false);

		await forwarder
			.ReplayBufferedAsync(
				(items, _) =>
				{
					replayedChanges = [.. items];
					return Task.CompletedTask;
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.AreEqual(1, logForwardingFailureCallCount);
		Assert.IsNotNull(loggedFailure);
		Assert.IsInstanceOfType(loggedFailure.Value.Exception, typeof(IOException));
		Assert.AreEqual(1, loggedFailure.Value.BatchCount);
		Assert.AreEqual(changes[0].Path, loggedFailure.Value.FirstPath);
		Assert.AreEqual(WorkspaceChangeForwardingFailureCodes.ConnectionFailed, loggedFailure.Value.Code);
		Assert.IsFalse(loggedFailure.Value.WasDropped);
		Assert.IsNotNull(replayedChanges);
		Assert.HasCount(1, replayedChanges);
	}

	[TestMethod]
	public async Task DispatchAsync_WhenEnsureStartedReplaysBufferedChanges_CompletesCurrentDispatchWithoutDeadlock()
	{
		bool startupSucceeds = false;
		var forwardedBatches = new List<IReadOnlyList<FileChange>>();
		WorkspaceChangeForwarder? forwarder = null;

		forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),

			ensureConnectionStartedAsync: async cancellationToken =>
			{
				if (!startupSucceeds)
					return false;

				WorkspaceChangeForwarder currentForwarder =
					forwarder ?? throw new InvalidOperationException("The forwarder was not initialized before startup.");

				await currentForwarder
					.ReplayBufferedAsync(
						(items, _) =>
						{
							forwardedBatches.Add([.. items]);
							return Task.CompletedTask;
						},

						cancellationToken
					)
					.ConfigureAwait(false);

				return true;
			}
		);

		FileChange[] bufferedChanges = [new(@"C:\Workspace\Scripts\buffered.ext", FileChangeKind.Changed)];
		FileChange[] currentChanges = [new(@"C:\Workspace\Scripts\current.ext", FileChangeKind.Changed)];

		var dispatchRecorder = new CallbackRecorder();

		await forwarder.DispatchAsync(bufferedChanges, dispatchRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, dispatchRecorder.InvocationCount, "Changes should be buffered while startup fails.");

		startupSucceeds = true;

		await forwarder
			.DispatchAsync(
				currentChanges,

				(items, _) =>
				{
					forwardedBatches.Add([.. items]);
					return Task.CompletedTask;
				},

				CancellationToken.None
			)
			.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.HasCount(2, forwardedBatches);
		Assert.AreEqual(bufferedChanges[0].Path, forwardedBatches[0][0].Path);
		Assert.AreEqual(bufferedChanges[0].Kind, forwardedBatches[0][0].Kind);
		Assert.AreEqual(currentChanges[0].Path, forwardedBatches[1][0].Path);
		Assert.AreEqual(currentChanges[0].Kind, forwardedBatches[1][0].Kind);
	}

	[TestMethod]
	public async Task DispatchAsync_WhenConnectionClosesWhileOwnerIsAlive_BuffersChanges()
	{
		IReadOnlyList<FileChange>? replayedChanges = null;

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),
			ensureConnectionStartedAsync: _ => Task.FromResult(true)
		);

		FileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await forwarder
			.DispatchAsync(changes, (_, _) => throw new ObjectDisposedException("connection"), CancellationToken.None)
			.ConfigureAwait(false);

		await forwarder
			.ReplayBufferedAsync(
				(items, _) =>
				{
					replayedChanges = [.. items];
					return Task.CompletedTask;
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.IsNotNull(replayedChanges);
		Assert.HasCount(1, replayedChanges);
	}

	[TestMethod]
	public async Task DispatchAsync_WhenForwardingThrowsUnexpectedException_LogsAndDropsChangesWithoutReplay()
	{
		int logForwardingFailureCallCount = 0;

		WorkspaceChangeForwardingFailure? loggedFailure = null;

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),
			ensureConnectionStartedAsync: _ => Task.FromResult(true),

			logForwardingFailure: failure =>
			{
				logForwardingFailureCallCount++;
				loggedFailure = failure;
			}
		);

		FileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await forwarder
			.DispatchAsync(changes, (_, _) => throw new InvalidOperationException("Simulated unexpected forwarding failure."), CancellationToken.None)
			.ConfigureAwait(false);

		var replayRecorder = new CallbackRecorder();

		await forwarder.ReplayBufferedAsync(replayRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, replayRecorder.InvocationCount, "Unexpected forwarding failures should not be replayed.");
		Assert.AreEqual(1, logForwardingFailureCallCount);
		Assert.IsNotNull(loggedFailure);
		Assert.IsInstanceOfType(loggedFailure.Value.Exception, typeof(InvalidOperationException));
		Assert.AreEqual(1, loggedFailure.Value.BatchCount);
		Assert.AreEqual(changes[0].Path, loggedFailure.Value.FirstPath);
		Assert.AreEqual(WorkspaceChangeForwardingFailureCodes.Unexpected, loggedFailure.Value.Code);
		Assert.IsTrue(loggedFailure.Value.WasDropped);
	}

	[TestMethod]
	public async Task DispatchAsync_WhenOwnerAlreadyDisposed_DoesNotBufferObjectDisposedFailure()
	{
		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: CreateDisposedDisposal(),
			ensureConnectionStartedAsync: _ => Task.FromResult(true)
		);

		FileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await forwarder
			.DispatchAsync(changes, (_, _) => throw new ObjectDisposedException("connection"), CancellationToken.None)
			.ConfigureAwait(false);

		var replayRecorder = new CallbackRecorder();

		await forwarder.ReplayBufferedAsync(replayRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, replayRecorder.InvocationCount, "Disposed owners should not retain buffered changes.");
	}

	[TestMethod]
	public async Task DispatchAsync_WhenTheCallerCancelsDuringTheConnectionStart_BuffersTheBatchAndRethrows()
	{
		using var callerSource = new CancellationTokenSource();

		WorkspaceChangeForwardingFailure? reportedFailure = null;

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),

			ensureConnectionStartedAsync: token =>
			{
				callerSource.Cancel();
				token.ThrowIfCancellationRequested();

				return Task.FromResult(true);
			},

			logForwardingFailure: failure => reportedFailure = failure
		);

		FileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];
		var dispatchRecorder = new CallbackRecorder();

		await Assert
			.ThrowsAsync<OperationCanceledException>(() => forwarder.DispatchAsync(changes, dispatchRecorder.Invoke, callerSource.Token))
			.ConfigureAwait(false);

		Assert.AreEqual(0, dispatchRecorder.InvocationCount);
		Assert.IsNull(reportedFailure, "A cancellation by the caller is no failure to start the connection.");

		IReadOnlyList<FileChange> replayedChanges = await forwarder
			.ReplayBufferedAsync((_, _) => Task.CompletedTask, CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreSequenceEqual([changes[0].Path], replayedChanges.Select(static change => change.Path));
	}

	[TestMethod]
	public async Task DispatchAsync_WhenTheCallerCancelsWhileWaitingForTheForwardingGate_BuffersTheBatchAndRethrows()
	{
		bool connectionStarted = false;

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),
			ensureConnectionStartedAsync: _ => Task.FromResult(connectionStarted)
		);

		// A change buffered while the connection is down lets a replay hold the forwarding gate.
		await forwarder
			.DispatchAsync(
				[new FileChange(@"C:\Workspace\Scripts\old.ext", FileChangeKind.Created)],
				(_, _) => Task.CompletedTask,
				CancellationToken.None
			)
			.ConfigureAwait(false);

		connectionStarted = true;

		var replayEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var replayGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Task replay = forwarder.ReplayBufferedAsync(
			async (_, _) =>
			{
				replayEntered.TrySetResult();
				await replayGate.Task.ConfigureAwait(false);
			},

			CancellationToken.None
		);

		try
		{
			await replayEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

			using var callerSource = new CancellationTokenSource();

			FileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];
			var dispatchRecorder = new CallbackRecorder();

			// The dispatch queues on the forwarding gate that the replay holds.
			Task<bool> dispatch = forwarder.DispatchAsync(changes, dispatchRecorder.Invoke, callerSource.Token);

			callerSource.Cancel();

			await Assert.ThrowsAsync<OperationCanceledException>(() => dispatch).ConfigureAwait(false);

			Assert.AreEqual(0, dispatchRecorder.InvocationCount);

			replayGate.SetResult();

			await replay.ConfigureAwait(false);

			IReadOnlyList<FileChange> replayedChanges = await forwarder
				.ReplayBufferedAsync((_, _) => Task.CompletedTask, CancellationToken.None)
				.ConfigureAwait(false);

			Assert.AreSequenceEqual([changes[0].Path], replayedChanges.Select(static change => change.Path));
		}
		finally
		{
			replayGate.TrySetResult();
		}
	}

	[TestMethod]
	public async Task DispatchAsync_WhenTheCallerCancelsWhileForwarding_BuffersTheBatchAndRethrows()
	{
		using var callerSource = new CancellationTokenSource();

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),
			ensureConnectionStartedAsync: _ => Task.FromResult(true)
		);

		FileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await Assert
			.ThrowsAsync<OperationCanceledException>(() =>
				forwarder.DispatchAsync(
					changes,

					(_, token) =>
					{
						callerSource.Cancel();
						token.ThrowIfCancellationRequested();

						return Task.CompletedTask;
					},

					callerSource.Token
				)
			)
			.ConfigureAwait(false);

		IReadOnlyList<FileChange> replayedChanges = await forwarder
			.ReplayBufferedAsync((_, _) => Task.CompletedTask, CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreSequenceEqual([changes[0].Path], replayedChanges.Select(static change => change.Path));
	}

	[TestMethod]
	public async Task ReplayBufferedAsync_WhenCallerCancels_RetainsBufferedChangesForLaterReplay()
	{
		bool startResult = false;
		IReadOnlyList<FileChange>? replayedChanges = null;

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),
			ensureConnectionStartedAsync: _ => Task.FromResult(startResult)
		);

		FileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await forwarder.DispatchAsync(changes, (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(false);

		startResult = true;

		using (var cancellationTokenSource = new CancellationTokenSource())
		{
			cancellationTokenSource.Cancel();
			await forwarder.ReplayBufferedAsync((_, token) => Task.FromCanceled(token), cancellationTokenSource.Token).ConfigureAwait(false);
		}

		await forwarder
			.ReplayBufferedAsync(
				(items, _) =>
				{
					replayedChanges = [.. items];
					return Task.CompletedTask;
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.IsNotNull(replayedChanges);
		Assert.HasCount(1, replayedChanges);
	}

	[TestMethod]
	public async Task ReplayBufferedAsync_WhenForwardingNotCurrentlyAllowed_RetainsBufferedChangesForLaterReplay()
	{
		bool canForward = true;
		bool startResult = false;

		IReadOnlyList<FileChange>? replayedChanges = null;

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => canForward,
			disposal: new ProviderLifetime(),
			ensureConnectionStartedAsync: _ => Task.FromResult(startResult)
		);

		FileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await forwarder.DispatchAsync(changes, (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(false);

		canForward = false;
		startResult = true;

		var replayRecorder = new CallbackRecorder();

		await forwarder.ReplayBufferedAsync(replayRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, replayRecorder.InvocationCount, "Changes should remain buffered while forwarding is not allowed.");

		canForward = true;

		await forwarder
			.ReplayBufferedAsync(
				(items, _) =>
				{
					replayedChanges = [.. items];
					return Task.CompletedTask;
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.IsNotNull(replayedChanges);
		Assert.HasCount(1, replayedChanges);
		Assert.AreEqual(changes[0].Path, replayedChanges[0].Path);
		Assert.AreEqual(changes[0].Kind, replayedChanges[0].Kind);
	}

	[TestMethod]
	public async Task ReplayBufferedAsync_WhenReplayIsInFlight_WaitsBeforeForwardingNewDispatch()
	{
		bool startResult = false;

		var forwardedBatches = new List<IReadOnlyList<FileChange>>();
		var replayEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowReplayToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var dispatchObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),
			ensureConnectionStartedAsync: _ => Task.FromResult(startResult)
		);

		FileChange[] bufferedChanges = [new(@"C:\Workspace\Scripts\buffered.ext", FileChangeKind.Changed)];
		FileChange[] liveChanges = [new(@"C:\Workspace\Scripts\live.ext", FileChangeKind.Created)];

		var dispatchRecorder = new CallbackRecorder();

		await forwarder.DispatchAsync(bufferedChanges, dispatchRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, dispatchRecorder.InvocationCount, "Changes should be buffered while startup is unavailable.");

		startResult = true;

		Task replayTask = forwarder.ReplayBufferedAsync(
			async (changes, _) =>
			{
				forwardedBatches.Add([.. changes]);
				replayEntered.TrySetResult(true);
				await allowReplayToFinish.Task.ConfigureAwait(false);
			},

			CancellationToken.None
		);

		await replayEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		Task dispatchTask = forwarder.DispatchAsync(
			liveChanges,

			(items, _) =>
			{
				forwardedBatches.Add([.. items]);
				dispatchObserved.TrySetResult(true);
				return Task.CompletedTask;
			},

			CancellationToken.None
		);

		Task completedTask = await Task.WhenAny(dispatchObserved.Task, Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreNotSame(
			dispatchObserved.Task,
			completedTask,
			"A live dispatch should not overtake an older buffered replay while the replay is still in flight."
		);

		allowReplayToFinish.TrySetResult(true);

		await replayTask.ConfigureAwait(false);
		await dispatchTask.ConfigureAwait(false);

		Assert.HasCount(2, forwardedBatches);
		Assert.AreEqual(bufferedChanges[0].Path, forwardedBatches[0][0].Path);
		Assert.AreEqual(liveChanges[0].Path, forwardedBatches[1][0].Path);
	}

	[TestMethod]
	public async Task ReplayBufferedAsync_ReplaysBufferedPathsInFirstPendingOccurrenceOrder()
	{
		bool startResult = false;
		IReadOnlyList<FileChange>? replayedChanges = null;

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),
			ensureConnectionStartedAsync: _ => Task.FromResult(startResult)
		);

		var dispatchRecorder = new CallbackRecorder();

		await forwarder
			.DispatchAsync(
				[
					new FileChange(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Created),
					new FileChange(@"C:\Workspace\Scripts\second.ext", FileChangeKind.Changed),
				],

				dispatchRecorder.Invoke,
				CancellationToken.None
			)
			.ConfigureAwait(false);

		await forwarder
			.DispatchAsync(
				[new FileChange(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Changed)],
				dispatchRecorder.Invoke,
				CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.AreEqual(0, dispatchRecorder.InvocationCount, "Changes should be buffered while startup is unavailable.");

		startResult = true;

		await forwarder
			.ReplayBufferedAsync(
				(items, _) =>
				{
					replayedChanges = [.. items];
					return Task.CompletedTask;
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.IsNotNull(replayedChanges);

		Assert.AreSequenceEqual(
			[@"C:\Workspace\Scripts\first.ext", @"C:\Workspace\Scripts\second.ext"],
			[.. replayedChanges.Select(change => change.Path)]
		);

		Assert.AreSequenceEqual([FileChangeKind.Created, FileChangeKind.Changed], [.. replayedChanges.Select(change => change.Kind)]);
	}

	[TestMethod]
	public async Task ReplayBufferedAsync_WhenTheRetriedBatchFails_PreservesDeleteCreateOrderAcrossBothBuffers()
	{
		string path = @"C:\Workspace\Scripts\test.ext";
		bool startResult = true;

		var forwardedBatches = new List<IReadOnlyList<FileChange>>();

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),
			ensureConnectionStartedAsync: _ => Task.FromResult(startResult)
		);

		// Buffer a delete through a failed dispatch; it lands in the failed-replay buffer.
		await forwarder
			.DispatchAsync(
				[new(path, FileChangeKind.Deleted)],
				(_, _) => throw new IOException("Simulated first dispatch failure."),
				CancellationToken.None
			)
			.ConfigureAwait(false);

		// The replay fails after a concurrent dispatch buffered a newer create for the same path: the delete must
		// stay ahead of the create instead of canceling against it.
		await forwarder
			.ReplayBufferedAsync(
				async (_, _) =>
				{
					startResult = false;

					await forwarder
						.DispatchAsync([new(path, FileChangeKind.Created)], (_, _) => Task.CompletedTask, CancellationToken.None)
						.ConfigureAwait(false);

					startResult = true;
					throw new IOException("Simulated replay failure.");
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		await forwarder
			.ReplayBufferedAsync(
				(items, _) =>
				{
					forwardedBatches.Add([.. items]);
					return Task.CompletedTask;
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.HasCount(1, forwardedBatches);
		Assert.HasCount(2, forwardedBatches[0]);
		Assert.AreEqual(FileChangeKind.Deleted, forwardedBatches[0][0].Kind);
		Assert.AreEqual(FileChangeKind.Created, forwardedBatches[0][1].Kind);
	}

	[TestMethod]
	public async Task Dispose_AfterDisposal_IgnoresDispatchAndReplay()
	{
		bool ensureStartedCalled = false;
		bool forwardCalled = false;

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),

			ensureConnectionStartedAsync: _ =>
			{
				ensureStartedCalled = true;
				return Task.FromResult(true);
			}
		);

		forwarder.Dispose();

		FileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await forwarder
			.DispatchAsync(
				changes,

				(_, _) =>
				{
					forwardCalled = true;
					return Task.CompletedTask;
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		await forwarder
			.ReplayBufferedAsync(
				(_, _) =>
				{
					forwardCalled = true;
					return Task.CompletedTask;
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.IsFalse(ensureStartedCalled);
		Assert.IsFalse(forwardCalled);
	}

	[TestMethod]
	public async Task Dispose_WhileReplayIsInFlight_AllowsReplayToFinishAndBlocksNewDispatch()
	{
		bool startResult = false;

		var forwardedBatches = new List<IReadOnlyList<FileChange>>();
		var replayEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowReplayToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),
			ensureConnectionStartedAsync: _ => Task.FromResult(startResult)
		);

		FileChange[] bufferedChanges = [new(@"C:\Workspace\Scripts\buffered.ext", FileChangeKind.Changed)];
		FileChange[] liveChanges = [new(@"C:\Workspace\Scripts\live.ext", FileChangeKind.Created)];

		var dispatchRecorder = new CallbackRecorder();

		await forwarder.DispatchAsync(bufferedChanges, dispatchRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, dispatchRecorder.InvocationCount, "Changes should be buffered while startup is unavailable.");

		startResult = true;

		Task replayTask = forwarder.ReplayBufferedAsync(
			async (changes, _) =>
			{
				forwardedBatches.Add([.. changes]);
				replayEntered.TrySetResult(true);
				await allowReplayToFinish.Task.ConfigureAwait(false);
			},

			CancellationToken.None
		);

		await replayEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);
		forwarder.Dispose();

		await forwarder
			.DispatchAsync(
				liveChanges,

				(items, _) =>
				{
					forwardedBatches.Add([.. items]);
					return Task.CompletedTask;
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		allowReplayToFinish.TrySetResult(true);

		await replayTask.ConfigureAwait(false);

		Assert.HasCount(1, forwardedBatches);
		Assert.AreEqual(bufferedChanges[0].Path, forwardedBatches[0][0].Path);
	}

	[TestMethod]
	public async Task Dispose_WhileDispatchWaitsForGate_DoesNotStartBlockedForwarding()
	{
		bool ensureStartedCalled = false;
		bool forwardCalled = false;

		var firstForwardEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstForwardToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),

			ensureConnectionStartedAsync: _ =>
			{
				ensureStartedCalled = true;
				return Task.FromResult(true);
			}
		);

		Task firstDispatchTask = forwarder.DispatchAsync(
			[new FileChange(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Changed)],

			async (_, _) =>
			{
				firstForwardEntered.TrySetResult(true);
				await allowFirstForwardToFinish.Task.ConfigureAwait(false);
			},

			CancellationToken.None
		);

		await firstForwardEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		Task blockedDispatchTask = forwarder.DispatchAsync(
			[new FileChange(@"C:\Workspace\Scripts\blocked.ext", FileChangeKind.Created)],

			(_, _) =>
			{
				forwardCalled = true;
				return Task.CompletedTask;
			},

			CancellationToken.None
		);

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);
		forwarder.Dispose();
		allowFirstForwardToFinish.TrySetResult(true);

		await firstDispatchTask.ConfigureAwait(false);
		await blockedDispatchTask.ConfigureAwait(false);

		Assert.IsTrue(ensureStartedCalled);
		Assert.IsFalse(forwardCalled);
	}

	[TestMethod]
	public void Dispose_WhenNoOperationIsInFlight_ReleasesTheForwardingGateNowAndOnlyOnce()
	{
		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),
			ensureConnectionStartedAsync: _ => Task.FromResult(true)
		);

		forwarder.Dispose();
		forwarder.Dispose();

		Assert.AreEqual(1, forwarder.ForwardingGateDisposals);
	}

	[TestMethod]
	public async Task Dispose_WhileAnOperationIsInFlight_ReleasesTheForwardingGateOnlyAfterTheLastOperationLeaves()
	{
		var forwardEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowForwardToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		var forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),
			ensureConnectionStartedAsync: _ => Task.FromResult(true)
		);

		Task dispatch = forwarder.DispatchAsync(
			[new FileChange(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Changed)],

			async (_, _) =>
			{
				forwardEntered.TrySetResult(true);
				await allowForwardToFinish.Task.ConfigureAwait(false);
			},

			CancellationToken.None
		);

		await forwardEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		forwarder.Dispose();
		forwarder.Dispose();

		// The disposal returned without waiting, and the operation in flight still owns the gate.
		Assert.AreEqual(0, forwarder.ForwardingGateDisposals);

		allowForwardToFinish.TrySetResult(true);
		await dispatch.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsTrue(
			await TestPolling.ForConditionAsync(() => forwarder.ForwardingGateDisposals == 1, TestPolling.DefaultTimeout).ConfigureAwait(false),
			"The last operation to leave must release the forwarding gate."
		);

		forwarder.Dispose();

		Assert.AreEqual(1, forwarder.ForwardingGateDisposals);
	}

	[TestMethod]
	public async Task Dispose_FromInsideAForwardingCallback_ReturnsAdmitsNothingMoreAndReleasesTheForwardingGateOnceAfterTheOperationLeaves()
	{
		WorkspaceChangeForwarder? forwarder = null;
		Exception? disposeFailure = null;
		int disposalsInsideTheCallback = -1;

		forwarder = new WorkspaceChangeForwarder(
			canForwardAccessor: () => true,
			disposal: new ProviderLifetime(),
			ensureConnectionStartedAsync: _ => Task.FromResult(true)
		);

		FileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		bool forwarded = await forwarder
			.DispatchAsync(
				changes,

				(_, _) =>
				{
					try
					{
						forwarder.Dispose();
					}
					catch (Exception exception)
					{
						disposeFailure = exception;
					}

					disposalsInsideTheCallback = forwarder.ForwardingGateDisposals;
					return Task.CompletedTask;
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		// The disposal does not wait for the operation that requests it, and the operation still owns the forwarding gate.
		Assert.IsTrue(forwarded);
		Assert.IsNull(disposeFailure);
		Assert.AreEqual(0, disposalsInsideTheCallback);

		bool lateForwardCalled = false;

		bool lateForwarded = await forwarder
			.DispatchAsync(
				changes,

				(_, _) =>
				{
					lateForwardCalled = true;
					return Task.CompletedTask;
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.IsFalse(lateForwarded);
		Assert.IsFalse(lateForwardCalled);

		Assert.IsTrue(
			await TestPolling.ForConditionAsync(() => forwarder.ForwardingGateDisposals == 1, TestPolling.DefaultTimeout).ConfigureAwait(false),
			"The operation that requested the disposal must release the forwarding gate when it leaves."
		);

		forwarder.Dispose();

		Assert.AreEqual(1, forwarder.ForwardingGateDisposals);
	}

	[TestMethod]
	public async Task ConcurrentDispatchesAndDisposal_ReleaseTheForwardingGateExactlyOnceWithoutFaultingAnOperation()
	{
		const int rounds = 100;
		const int workers = 8;

		for (int round = 0; round < rounds; round++)
		{
			var forwarder = new WorkspaceChangeForwarder(
				canForwardAccessor: () => true,
				disposal: new ProviderLifetime(),
				ensureConnectionStartedAsync: _ => Task.FromResult(true)
			);

			var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			Task[] dispatches =
			[
				.. Enumerable
					.Range(0, workers)
					.Select(worker =>
						Task.Run(
							async () =>
							{
								await start.Task.ConfigureAwait(false);

								await forwarder
									.DispatchAsync(
										[new FileChange($@"C:\Workspace\Scripts\{worker}.ext", FileChangeKind.Changed)],
										async (_, _) => await Task.Yield(),
										CancellationToken.None
									)
									.ConfigureAwait(false);
							},

							TestContext.CancellationToken
						)
					),
			];

			var disposal = Task.Run(
				async () =>
				{
					await start.Task.ConfigureAwait(false);
					forwarder.Dispose();
				},

				TestContext.CancellationToken
			);

			start.TrySetResult(true);

			// An operation that observed a disposed gate would fault here.
			await Task.WhenAll([.. dispatches, disposal]).WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);

			Assert.IsTrue(
				await TestPolling.ForConditionAsync(() => forwarder.ForwardingGateDisposals == 1, TestPolling.DefaultTimeout).ConfigureAwait(false),
				$"Round {round} did not release the forwarding gate."
			);

			Assert.AreEqual(1, forwarder.ForwardingGateDisposals, $"Round {round} released the forwarding gate more than once.");
		}
	}

	private sealed class CallbackRecorder
	{
		private int _invocationCount;

		public int InvocationCount => Volatile.Read(ref _invocationCount);

		public Task Invoke(IReadOnlyList<FileChange> changes, CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _invocationCount);
			return Task.CompletedTask;
		}
	}

	public TestContext? TestContext { get; set; }
}
