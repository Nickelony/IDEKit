namespace Nickelony.LanguageServer.Provider.Tests;

// Timing note: the short "must not have happened yet" windows in this suite (a live dispatch versus an in-flight
// deferred replay, and a gate-blocked dispatch across disposal) are negative probes: they cannot flake red, but
// they can stop detecting a slow regression.
[TestClass]
public sealed class WorkspaceFileChangeForwarderTests
{
	[TestMethod]
	public async Task DispatchAsync_Success_ForwardsImmediatelyWithoutBuffering()
	{
		bool ensureStartedCalled = false;
		int tryMarkTransportUnhealthyCallCount = 0;
		IReadOnlyList<WorkspaceFileChange>? forwardedChanges = null;

		var forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => true,
			isDisposedAccessor: () => false,
			ensureTransportStartedAsync: _ =>
			{
				ensureStartedCalled = true;
				return Task.FromResult(true);
			},
			tryMarkTransportUnhealthy: () => tryMarkTransportUnhealthyCallCount++);

		WorkspaceFileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await forwarder.DispatchAsync(changes,
			(items, _) =>
			{
				forwardedChanges = [.. items];
				return Task.CompletedTask;
			},
			CancellationToken.None).ConfigureAwait(false);

		var replayRecorder = new CallbackRecorder();

		await forwarder.ReplayDeferredAsync(replayRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, replayRecorder.InvocationCount, "No deferred changes should remain.");
		Assert.IsTrue(ensureStartedCalled);
		Assert.IsNotNull(forwardedChanges);
		Assert.AreEqual(1, forwardedChanges.Count);
		Assert.AreEqual(0, tryMarkTransportUnhealthyCallCount);
	}

	[TestMethod]
	public async Task DispatchAsync_WhenForwardingNotCurrentlyAllowed_DropsChangesWhenBufferingIsDisabled()
	{
		bool ensureStartedCalled = false;
		bool forwardCalled = false;

		var forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => false,
			isDisposedAccessor: () => false,
			ensureTransportStartedAsync: _ =>
			{
				ensureStartedCalled = true;
				return Task.FromResult(true);
			},
			tryMarkTransportUnhealthy: static () => { },
			bufferChangesWhileForwardingDisabled: false);

		WorkspaceFileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await forwarder.DispatchAsync(changes,
			(_, _) =>
			{
				forwardCalled = true;
				return Task.CompletedTask;
			},
			CancellationToken.None).ConfigureAwait(false);

		var replayRecorder = new CallbackRecorder();

		await forwarder.ReplayDeferredAsync(replayRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, replayRecorder.InvocationCount, "Ignored changes should not be retained for replay.");
		Assert.IsFalse(ensureStartedCalled);
		Assert.IsFalse(forwardCalled);
	}

	[TestMethod]
	public async Task DispatchAsync_WhenForwardingNotCurrentlyAllowed_BuffersChangesByDefault()
	{
		bool canForward = false;
		IReadOnlyList<WorkspaceFileChange>? replayedChanges = null;

		var forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => canForward,
			isDisposedAccessor: () => false,
			ensureTransportStartedAsync: _ => Task.FromResult(true),
			tryMarkTransportUnhealthy: static () => { });

		WorkspaceFileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await forwarder.DispatchAsync(changes, (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(false);
		canForward = true;

		await forwarder.ReplayDeferredAsync(
			(items, _) =>
			{
				replayedChanges = [.. items];
				return Task.CompletedTask;
			},
			CancellationToken.None).ConfigureAwait(false);

		Assert.IsNotNull(replayedChanges);
		Assert.AreEqual(1, replayedChanges.Count);
		Assert.AreEqual(changes[0].Path, replayedChanges[0].Path);
		Assert.AreEqual(changes[0].Kind, replayedChanges[0].Kind);
	}

	[TestMethod]
	public async Task DispatchAsync_WhenStartupFails_BuffersAndReplayDispatchesChanges()
	{
		bool startResult = false;
		IReadOnlyList<WorkspaceFileChange>? replayedChanges = null;

		var forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => true,
			isDisposedAccessor: () => false,
			ensureTransportStartedAsync: _ => Task.FromResult(startResult),
			tryMarkTransportUnhealthy: static () => { });

		WorkspaceFileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		var dispatchRecorder = new CallbackRecorder();

		await forwarder.DispatchAsync(changes, dispatchRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, dispatchRecorder.InvocationCount, "Dispatch should be buffered while startup fails.");

		startResult = true;

		await forwarder.ReplayDeferredAsync(
			(items, _) =>
			{
				replayedChanges = [.. items];
				return Task.CompletedTask;
			},
			CancellationToken.None).ConfigureAwait(false);

		Assert.IsNotNull(replayedChanges);
		Assert.AreEqual(1, replayedChanges.Count);
		Assert.AreEqual(changes[0].Path, replayedChanges[0].Path);
		Assert.AreEqual(changes[0].Kind, replayedChanges[0].Kind);
	}

	[TestMethod]
	public async Task DispatchAsync_WhenEnsureStartedThrows_BuffersChangesAndDoesNotPropagate()
	{
		IReadOnlyList<WorkspaceFileChange>? replayedChanges = null;
		WorkspaceFileForwardingFailure? reportedFailure = null;

		var forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => true,
			isDisposedAccessor: () => false,
			ensureTransportStartedAsync: _ => throw new InvalidOperationException("The transport could not be started."),
			tryMarkTransportUnhealthy: static () => { },
			logForwardingFailure: failure => reportedFailure = failure);

		WorkspaceFileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];
		var dispatchRecorder = new CallbackRecorder();

		// A throwing ensure-start must not escape into the watcher that dispatched the batch, and the batch must be
		// buffered for a later replay instead of being dropped.
		bool forwarded = await forwarder.DispatchAsync(changes, dispatchRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.IsFalse(forwarded);
		Assert.AreEqual(0, dispatchRecorder.InvocationCount);
		Assert.IsNotNull(reportedFailure);
		Assert.IsFalse(reportedFailure.Value.WasDropped);
		Assert.AreEqual(changes[0].Path, reportedFailure.Value.FirstPath);

		await forwarder.ReplayDeferredAsync(
			(items, _) =>
			{
				replayedChanges = [.. items];
				return Task.CompletedTask;
			},
			CancellationToken.None).ConfigureAwait(false);

		Assert.IsNotNull(replayedChanges);
		Assert.AreEqual(1, replayedChanges.Count);
		Assert.AreEqual(changes[0].Path, replayedChanges[0].Path);
	}

	[TestMethod]
	public async Task DispatchAsync_WhenEnsureStartedThrowsObjectDisposed_BuffersChangesWithoutReportingFailure()
	{
		IReadOnlyList<WorkspaceFileChange>? replayedChanges = null;
		int logForwardingFailureCallCount = 0;

		var forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => true,
			isDisposedAccessor: () => false,
			ensureTransportStartedAsync: _ => throw new ObjectDisposedException("transport"),
			tryMarkTransportUnhealthy: static () => { },
			logForwardingFailure: _ => logForwardingFailureCallCount++);

		WorkspaceFileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];
		var dispatchRecorder = new CallbackRecorder();

		// A transport that is already disposed cannot start; the batch is buffered for a later replay, and the
		// disposed-transport case is expected rather than a forwarding failure, so nothing is reported.
		bool forwarded = await forwarder.DispatchAsync(changes, dispatchRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.IsFalse(forwarded);
		Assert.AreEqual(0, dispatchRecorder.InvocationCount);
		Assert.AreEqual(0, logForwardingFailureCallCount, "A disposed transport is not reported as a forwarding failure.");

		await forwarder.ReplayDeferredAsync(
			(items, _) =>
			{
				replayedChanges = [.. items];
				return Task.CompletedTask;
			},
			CancellationToken.None).ConfigureAwait(false);

		Assert.IsNotNull(replayedChanges);
		Assert.AreEqual(1, replayedChanges.Count);
		Assert.AreEqual(changes[0].Path, replayedChanges[0].Path);
	}

	[TestMethod]
	public async Task DispatchAsync_WhenEnsureStartedThrowsObjectDisposedWhileOwnerAlreadyDisposed_DoesNotBuffer()
	{
		int logForwardingFailureCallCount = 0;

		var forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => true,
			isDisposedAccessor: () => true,
			ensureTransportStartedAsync: _ => throw new ObjectDisposedException("transport"),
			tryMarkTransportUnhealthy: static () => { },
			logForwardingFailure: _ => logForwardingFailureCallCount++);

		WorkspaceFileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];
		var dispatchRecorder = new CallbackRecorder();

		bool forwarded = await forwarder.DispatchAsync(changes, dispatchRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		var replayRecorder = new CallbackRecorder();

		await forwarder.ReplayDeferredAsync(replayRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.IsFalse(forwarded);
		Assert.AreEqual(0, dispatchRecorder.InvocationCount);
		Assert.AreEqual(0, logForwardingFailureCallCount);
		Assert.AreEqual(0, replayRecorder.InvocationCount, "A disposed owner does not retain deferred changes.");
	}

	[TestMethod]
	public async Task DispatchAsync_WhenIOExceptionDuringForwarding_BuffersChangesAndMarksTransportUnavailable()
	{
		int tryMarkTransportUnhealthyCallCount = 0;
		int logForwardingFailureCallCount = 0;
		WorkspaceFileForwardingFailure? loggedFailure = null;
		IReadOnlyList<WorkspaceFileChange>? replayedChanges = null;

		var forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => true,
			isDisposedAccessor: () => false,
			ensureTransportStartedAsync: _ => Task.FromResult(true),
			tryMarkTransportUnhealthy: () => tryMarkTransportUnhealthyCallCount++,
			logForwardingFailure: failure =>
			{
				logForwardingFailureCallCount++;
				loggedFailure = failure;
			});

		WorkspaceFileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await forwarder.DispatchAsync(changes, (_, _) => throw new IOException("Simulated forwarding failure."), CancellationToken.None)
			.ConfigureAwait(false);

		await forwarder.ReplayDeferredAsync(
			(items, _) =>
			{
				replayedChanges = [.. items];
				return Task.CompletedTask;
			},
			CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(1, tryMarkTransportUnhealthyCallCount);
		Assert.AreEqual(1, logForwardingFailureCallCount);
		Assert.IsNotNull(loggedFailure);
		Assert.IsInstanceOfType(loggedFailure.Value.Exception, typeof(IOException));
		Assert.AreEqual(1, loggedFailure.Value.BatchCount);
		Assert.AreEqual(changes[0].Path, loggedFailure.Value.FirstPath);
		Assert.IsFalse(loggedFailure.Value.WasDropped);
		Assert.IsNotNull(replayedChanges);
		Assert.AreEqual(1, replayedChanges.Count);
	}

	[TestMethod]
	public async Task DispatchAsync_WhenEnsureStartedReplaysDeferredChanges_CompletesCurrentDispatchWithoutDeadlock()
	{
		bool startupSucceeds = false;
		var forwardedBatches = new List<IReadOnlyList<WorkspaceFileChange>>();
		WorkspaceFileChangeForwarder? forwarder = null;

		forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => true,
			isDisposedAccessor: () => false,
			ensureTransportStartedAsync: async cancellationToken =>
			{
				if (!startupSucceeds)
					return false;

				WorkspaceFileChangeForwarder currentForwarder = forwarder
					?? throw new InvalidOperationException("The forwarder was not initialized before startup.");

				await currentForwarder.ReplayDeferredAsync(
					(items, _) =>
					{
						forwardedBatches.Add([.. items]);
						return Task.CompletedTask;
					},
					cancellationToken).ConfigureAwait(false);

				return true;
			},
			tryMarkTransportUnhealthy: static () => { });

		WorkspaceFileChange[] deferredChanges = [new(@"C:\Workspace\Scripts\deferred.ext", FileChangeKind.Changed)];
		WorkspaceFileChange[] currentChanges = [new(@"C:\Workspace\Scripts\current.ext", FileChangeKind.Changed)];

		var dispatchRecorder = new CallbackRecorder();

		await forwarder.DispatchAsync(
			deferredChanges,
			dispatchRecorder.Invoke,
			CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, dispatchRecorder.InvocationCount, "Deferred changes should be buffered while startup fails.");

		startupSucceeds = true;

		await forwarder.DispatchAsync(
			currentChanges,
			(items, _) =>
			{
				forwardedBatches.Add([.. items]);
				return Task.CompletedTask;
			},
			CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

		Assert.AreEqual(2, forwardedBatches.Count);
		Assert.AreEqual(deferredChanges[0].Path, forwardedBatches[0][0].Path);
		Assert.AreEqual(deferredChanges[0].Kind, forwardedBatches[0][0].Kind);
		Assert.AreEqual(currentChanges[0].Path, forwardedBatches[1][0].Path);
		Assert.AreEqual(currentChanges[0].Kind, forwardedBatches[1][0].Kind);
	}

	[TestMethod]
	public async Task DispatchAsync_WhenTransportClosesWhileOwnerIsAlive_BuffersChangesAndMarksTransportUnavailable()
	{
		int tryMarkTransportUnhealthyCallCount = 0;
		IReadOnlyList<WorkspaceFileChange>? replayedChanges = null;

		var forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => true,
			isDisposedAccessor: () => false,
			ensureTransportStartedAsync: _ => Task.FromResult(true),
			tryMarkTransportUnhealthy: () => tryMarkTransportUnhealthyCallCount++);

		WorkspaceFileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await forwarder.DispatchAsync(changes,
			(_, _) => throw new ObjectDisposedException("transport"),
			CancellationToken.None).ConfigureAwait(false);

		await forwarder.ReplayDeferredAsync(
			(items, _) =>
			{
				replayedChanges = [.. items];
				return Task.CompletedTask;
			},
			CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(1, tryMarkTransportUnhealthyCallCount);
		Assert.IsNotNull(replayedChanges);
		Assert.AreEqual(1, replayedChanges.Count);
	}

	[TestMethod]
	public async Task DispatchAsync_WhenForwardingThrowsUnexpectedException_LogsAndDropsChangesWithoutReplay()
	{
		int tryMarkTransportUnhealthyCallCount = 0;
		int logForwardingFailureCallCount = 0;
		WorkspaceFileForwardingFailure? loggedFailure = null;

		var forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => true,
			isDisposedAccessor: () => false,
			ensureTransportStartedAsync: _ => Task.FromResult(true),
			tryMarkTransportUnhealthy: () => tryMarkTransportUnhealthyCallCount++,
			logForwardingFailure: failure =>
			{
				logForwardingFailureCallCount++;
				loggedFailure = failure;
			});

		WorkspaceFileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await forwarder.DispatchAsync(changes,
			(_, _) => throw new InvalidOperationException("Simulated unexpected forwarding failure."),
			CancellationToken.None).ConfigureAwait(false);

		var replayRecorder = new CallbackRecorder();

		await forwarder.ReplayDeferredAsync(replayRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, replayRecorder.InvocationCount, "Unexpected forwarding failures should not be replayed.");
		Assert.AreEqual(0, tryMarkTransportUnhealthyCallCount);
		Assert.AreEqual(1, logForwardingFailureCallCount);
		Assert.IsNotNull(loggedFailure);
		Assert.IsInstanceOfType(loggedFailure.Value.Exception, typeof(InvalidOperationException));
		Assert.AreEqual(1, loggedFailure.Value.BatchCount);
		Assert.AreEqual(changes[0].Path, loggedFailure.Value.FirstPath);
		Assert.IsTrue(loggedFailure.Value.WasDropped);
	}

	[TestMethod]
	public async Task DispatchAsync_WhenOwnerAlreadyDisposed_DoesNotBufferObjectDisposedFailure()
	{
		int tryMarkTransportUnhealthyCallCount = 0;

		var forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => true,
			isDisposedAccessor: () => true,
			ensureTransportStartedAsync: _ => Task.FromResult(true),
			tryMarkTransportUnhealthy: () => tryMarkTransportUnhealthyCallCount++);

		WorkspaceFileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await forwarder.DispatchAsync(changes,
			(_, _) => throw new ObjectDisposedException("transport"),
			CancellationToken.None).ConfigureAwait(false);

		var replayRecorder = new CallbackRecorder();

		await forwarder.ReplayDeferredAsync(replayRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, replayRecorder.InvocationCount, "Disposed owners should not retain deferred changes.");
		Assert.AreEqual(0, tryMarkTransportUnhealthyCallCount);
	}

	[TestMethod]
	public async Task ReplayDeferredAsync_WhenCallerCancels_RetainsDeferredChangesForLaterReplay()
	{
		bool startResult = false;
		int tryMarkTransportUnhealthyCallCount = 0;
		IReadOnlyList<WorkspaceFileChange>? replayedChanges = null;

		var forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => true,
			isDisposedAccessor: () => false,
			ensureTransportStartedAsync: _ => Task.FromResult(startResult),
			tryMarkTransportUnhealthy: () => tryMarkTransportUnhealthyCallCount++);

		WorkspaceFileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await forwarder.DispatchAsync(changes, (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(false);

		startResult = true;

		using (var cancellationTokenSource = new CancellationTokenSource())
		{
			cancellationTokenSource.Cancel();

			await forwarder.ReplayDeferredAsync(
				(_, token) => Task.FromCanceled(token),
				cancellationTokenSource.Token).ConfigureAwait(false);
		}

		await forwarder.ReplayDeferredAsync(
			(items, _) =>
			{
				replayedChanges = [.. items];
				return Task.CompletedTask;
			},
			CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, tryMarkTransportUnhealthyCallCount);
		Assert.IsNotNull(replayedChanges);
		Assert.AreEqual(1, replayedChanges.Count);
	}

	[TestMethod]
	public async Task ReplayDeferredAsync_WhenForwardingNotCurrentlyAllowed_RetainsDeferredChangesForLaterReplay()
	{
		bool canForward = true;
		bool startResult = false;
		IReadOnlyList<WorkspaceFileChange>? replayedChanges = null;

		var forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => canForward,
			isDisposedAccessor: () => false,
			ensureTransportStartedAsync: _ => Task.FromResult(startResult),
			tryMarkTransportUnhealthy: static () => { });

		WorkspaceFileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await forwarder.DispatchAsync(changes, (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(false);

		canForward = false;
		startResult = true;

		var replayRecorder = new CallbackRecorder();

		await forwarder.ReplayDeferredAsync(replayRecorder.Invoke, CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, replayRecorder.InvocationCount, "Deferred changes should remain buffered while forwarding is not allowed.");

		canForward = true;

		await forwarder.ReplayDeferredAsync(
			(items, _) =>
			{
				replayedChanges = [.. items];
				return Task.CompletedTask;
			},
			CancellationToken.None).ConfigureAwait(false);

		Assert.IsNotNull(replayedChanges);
		Assert.AreEqual(1, replayedChanges.Count);
		Assert.AreEqual(changes[0].Path, replayedChanges[0].Path);
		Assert.AreEqual(changes[0].Kind, replayedChanges[0].Kind);
	}

	[TestMethod]
	public async Task ReplayDeferredAsync_WhenReplayIsInFlight_WaitsBeforeForwardingNewDispatch()
	{
		bool startResult = false;

		var forwardedBatches = new List<IReadOnlyList<WorkspaceFileChange>>();
		var replayEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowReplayToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var dispatchObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		var forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => true,
			isDisposedAccessor: () => false,
			ensureTransportStartedAsync: _ => Task.FromResult(startResult),
			tryMarkTransportUnhealthy: static () => { });

		WorkspaceFileChange[] deferredChanges = [new(@"C:\Workspace\Scripts\deferred.ext", FileChangeKind.Changed)];
		WorkspaceFileChange[] liveChanges = [new(@"C:\Workspace\Scripts\live.ext", FileChangeKind.Created)];

		var dispatchRecorder = new CallbackRecorder();

		await forwarder.DispatchAsync(
			deferredChanges,
			dispatchRecorder.Invoke,
			CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, dispatchRecorder.InvocationCount, "Deferred changes should be buffered while startup is unavailable.");

		startResult = true;

		Task replayTask = forwarder.ReplayDeferredAsync(
			async (changes, _) =>
			{
				forwardedBatches.Add([.. changes]);
				replayEntered.TrySetResult(true);
				await allowReplayToFinish.Task.ConfigureAwait(false);
			},
			CancellationToken.None);

		await replayEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Task dispatchTask = forwarder.DispatchAsync(
			liveChanges,
			(items, _) =>
			{
				forwardedBatches.Add([.. items]);
				dispatchObserved.TrySetResult(true);
				return Task.CompletedTask;
			},
			CancellationToken.None);

		Task completedTask = await Task.WhenAny(dispatchObserved.Task, Task.Delay(TestPolling.AbsenceWindow)).ConfigureAwait(false);

		Assert.AreNotSame(dispatchObserved.Task, completedTask,
			"A live dispatch should not overtake an older deferred replay while the replay is still in flight.");

		allowReplayToFinish.TrySetResult(true);

		await replayTask.ConfigureAwait(false);
		await dispatchTask.ConfigureAwait(false);

		Assert.AreEqual(2, forwardedBatches.Count);
		Assert.AreEqual(deferredChanges[0].Path, forwardedBatches[0][0].Path);
		Assert.AreEqual(liveChanges[0].Path, forwardedBatches[1][0].Path);
	}

	[TestMethod]
	public async Task ReplayDeferredAsync_ReplaysBufferedPathsInFirstPendingOccurrenceOrder()
	{
		bool startResult = false;
		IReadOnlyList<WorkspaceFileChange>? replayedChanges = null;

		var forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => true,
			isDisposedAccessor: () => false,
			ensureTransportStartedAsync: _ => Task.FromResult(startResult),
			tryMarkTransportUnhealthy: static () => { });

		var dispatchRecorder = new CallbackRecorder();

		await forwarder.DispatchAsync(
			[
				new WorkspaceFileChange(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Created),
				new WorkspaceFileChange(@"C:\Workspace\Scripts\second.ext", FileChangeKind.Changed)
			],
			dispatchRecorder.Invoke,
			CancellationToken.None).ConfigureAwait(false);

		await forwarder.DispatchAsync(
			[new WorkspaceFileChange(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Changed)],
			dispatchRecorder.Invoke,
			CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, dispatchRecorder.InvocationCount, "Deferred changes should be buffered while startup is unavailable.");

		startResult = true;

		await forwarder.ReplayDeferredAsync(
			(items, _) =>
			{
				replayedChanges = [.. items];
				return Task.CompletedTask;
			},
			CancellationToken.None).ConfigureAwait(false);

		Assert.IsNotNull(replayedChanges);

		CollectionAssert.AreEqual(
			new[]
			{
				@"C:\Workspace\Scripts\first.ext",
				@"C:\Workspace\Scripts\second.ext"
			},
			replayedChanges.Select(change => change.Path).ToArray());

		CollectionAssert.AreEqual(
			new[]
			{
				FileChangeKind.Created,
				FileChangeKind.Changed
			},
			replayedChanges.Select(change => change.Kind).ToArray());
	}

	[TestMethod]
	public async Task ReplayDeferredAsync_WhenTheRetriedBatchFails_PreservesDeleteCreateOrderAcrossBothBuffers()
	{
		string path = @"C:\Workspace\Scripts\test.ext";
		bool startResult = true;
		var forwardedBatches = new List<IReadOnlyList<WorkspaceFileChange>>();

		var forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => true,
			isDisposedAccessor: () => false,
			ensureTransportStartedAsync: _ => Task.FromResult(startResult),
			tryMarkTransportUnhealthy: static () => { });

		// Buffer a delete through a failed dispatch; it lands in the failed-replay buffer.
		await forwarder.DispatchAsync(
			[new(path, FileChangeKind.Deleted)],
			(_, _) => throw new IOException("Simulated first dispatch failure."),
			CancellationToken.None).ConfigureAwait(false);

		// The replay fails after a concurrent dispatch buffered a newer create for the same path: the delete must
		// stay ahead of the create instead of canceling against it.
		await forwarder.ReplayDeferredAsync(
			async (_, _) =>
			{
				startResult = false;

				await forwarder.DispatchAsync(
					[new(path, FileChangeKind.Created)],
					(_, _) => Task.CompletedTask,
					CancellationToken.None).ConfigureAwait(false);

				startResult = true;
				throw new IOException("Simulated replay failure.");
			},
			CancellationToken.None).ConfigureAwait(false);

		await forwarder.ReplayDeferredAsync(
			(items, _) =>
			{
				forwardedBatches.Add([.. items]);
				return Task.CompletedTask;
			},
			CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(1, forwardedBatches.Count);
		Assert.AreEqual(2, forwardedBatches[0].Count);
		Assert.AreEqual(FileChangeKind.Deleted, forwardedBatches[0][0].Kind);
		Assert.AreEqual(FileChangeKind.Created, forwardedBatches[0][1].Kind);
	}

	[TestMethod]
	public async Task Dispose_AfterDisposal_IgnoresDispatchAndReplay()
	{
		bool ensureStartedCalled = false;
		bool forwardCalled = false;

		var forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => true,
			isDisposedAccessor: () => false,
			ensureTransportStartedAsync: _ =>
			{
				ensureStartedCalled = true;
				return Task.FromResult(true);
			},
			tryMarkTransportUnhealthy: static () => { });

		forwarder.Dispose();

		WorkspaceFileChange[] changes = [new(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed)];

		await forwarder.DispatchAsync(
			changes,
			(_, _) =>
			{
				forwardCalled = true;
				return Task.CompletedTask;
			},
			CancellationToken.None).ConfigureAwait(false);

		await forwarder.ReplayDeferredAsync(
			(_, _) =>
			{
				forwardCalled = true;
				return Task.CompletedTask;
			},
			CancellationToken.None).ConfigureAwait(false);

		Assert.IsFalse(ensureStartedCalled);
		Assert.IsFalse(forwardCalled);
	}

	[TestMethod]
	public async Task Dispose_WhileReplayIsInFlight_AllowsReplayToFinishAndBlocksNewDispatch()
	{
		bool startResult = false;

		var forwardedBatches = new List<IReadOnlyList<WorkspaceFileChange>>();
		var replayEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowReplayToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		var forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => true,
			isDisposedAccessor: () => false,
			ensureTransportStartedAsync: _ => Task.FromResult(startResult),
			tryMarkTransportUnhealthy: static () => { });

		WorkspaceFileChange[] deferredChanges = [new(@"C:\Workspace\Scripts\deferred.ext", FileChangeKind.Changed)];
		WorkspaceFileChange[] liveChanges = [new(@"C:\Workspace\Scripts\live.ext", FileChangeKind.Created)];

		var dispatchRecorder = new CallbackRecorder();

		await forwarder.DispatchAsync(
			deferredChanges,
			dispatchRecorder.Invoke,
			CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, dispatchRecorder.InvocationCount, "Deferred changes should be buffered while startup is unavailable.");

		startResult = true;

		Task replayTask = forwarder.ReplayDeferredAsync(
			async (changes, _) =>
			{
				forwardedBatches.Add([.. changes]);
				replayEntered.TrySetResult(true);
				await allowReplayToFinish.Task.ConfigureAwait(false);
			},
			CancellationToken.None);

		await replayEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
		forwarder.Dispose();

		await forwarder.DispatchAsync(
			liveChanges,
			(items, _) =>
			{
				forwardedBatches.Add([.. items]);
				return Task.CompletedTask;
			},
			CancellationToken.None).ConfigureAwait(false);

		allowReplayToFinish.TrySetResult(true);

		await replayTask.ConfigureAwait(false);

		Assert.AreEqual(1, forwardedBatches.Count);
		Assert.AreEqual(deferredChanges[0].Path, forwardedBatches[0][0].Path);
	}

	[TestMethod]
	public async Task Dispose_WhileDispatchWaitsForGate_DoesNotStartBlockedForwarding()
	{
		bool ensureStartedCalled = false;
		bool forwardCalled = false;

		var firstForwardEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstForwardToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		var forwarder = new WorkspaceFileChangeForwarder(
			canForwardAccessor: () => true,
			isDisposedAccessor: () => false,
			ensureTransportStartedAsync: _ =>
			{
				ensureStartedCalled = true;
				return Task.FromResult(true);
			},
			tryMarkTransportUnhealthy: static () => { });

		Task firstDispatchTask = forwarder.DispatchAsync(
			[new WorkspaceFileChange(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Changed)],
			async (_, _) =>
			{
				firstForwardEntered.TrySetResult(true);
				await allowFirstForwardToFinish.Task.ConfigureAwait(false);
			},
			CancellationToken.None);

		await firstForwardEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Task blockedDispatchTask = forwarder.DispatchAsync(
			[new WorkspaceFileChange(@"C:\Workspace\Scripts\blocked.ext", FileChangeKind.Created)],
			(_, _) =>
			{
				forwardCalled = true;
				return Task.CompletedTask;
			},
			CancellationToken.None);

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);
		forwarder.Dispose();
		allowFirstForwardToFinish.TrySetResult(true);

		await firstDispatchTask.ConfigureAwait(false);
		await blockedDispatchTask.ConfigureAwait(false);

		Assert.IsTrue(ensureStartedCalled);
		Assert.IsFalse(forwardCalled);
	}

	private sealed class CallbackRecorder
	{
		private int _invocationCount;

		public int InvocationCount => Volatile.Read(ref _invocationCount);

		public Task Invoke(IReadOnlyList<WorkspaceFileChange> changes, CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _invocationCount);

			return Task.CompletedTask;
		}
	}
}
