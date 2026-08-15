namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class DocumentOperationSchedulerTests
{
	[TestMethod]
	public async Task EnqueueGlobalAsync_CanceledWhileWaiting_DoesNotInvokeDelegate()
	{
		var scheduler = new DocumentOperationScheduler();
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cancellationTokenSource = new CancellationTokenSource();

		int canceledOperationCallCount = 0;

		Task<bool> firstTask = scheduler.EnqueueGlobalAsync(async _ =>
		{
			firstStarted.TrySetResult(true);
			await allowFirstToFinish.Task.ConfigureAwait(false);
			return true;
		}, CancellationToken.None);

		await firstStarted.Task.ConfigureAwait(false);

		Task<bool> canceledTask = scheduler.EnqueueGlobalAsync(_ =>
		{
			Interlocked.Increment(ref canceledOperationCallCount);
			return Task.FromResult(true);
		}, cancellationTokenSource.Token);

		cancellationTokenSource.Cancel();
		allowFirstToFinish.TrySetResult(true);

		await firstTask.ConfigureAwait(false);
		await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => canceledTask).ConfigureAwait(false);

		Assert.AreEqual(0, canceledOperationCallCount);
	}

	[TestMethod]
	public async Task EnqueuePerDocumentAsync_CanceledWhileWaiting_DoesNotInvokeDelegate()
	{
		var scheduler = new DocumentOperationScheduler();
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cancellationTokenSource = new CancellationTokenSource();

		int canceledOperationCallCount = 0;

		Task<bool> firstTask = scheduler.EnqueuePerDocumentAsync("test.ext", async _ =>
		{
			firstStarted.TrySetResult(true);
			await allowFirstToFinish.Task.ConfigureAwait(false);
			return true;
		}, CancellationToken.None);

		await firstStarted.Task.ConfigureAwait(false);

		Task<bool> canceledTask = scheduler.EnqueuePerDocumentAsync("test.ext", _ =>
		{
			Interlocked.Increment(ref canceledOperationCallCount);
			return Task.FromResult(true);
		}, cancellationTokenSource.Token);

		cancellationTokenSource.Cancel();
		allowFirstToFinish.TrySetResult(true);

		await firstTask.ConfigureAwait(false);
		await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => canceledTask).ConfigureAwait(false);

		Assert.AreEqual(0, canceledOperationCallCount);
	}

	[TestMethod]
	public async Task EnqueuePerDocumentAsync_LaterWorkStillRunsAfterCanceledOperation()
	{
		var scheduler = new DocumentOperationScheduler();
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cancellationTokenSource = new CancellationTokenSource();

		int canceledOperationCallCount = 0;
		int laterOperationCallCount = 0;

		Task<bool> firstTask = scheduler.EnqueuePerDocumentAsync("test.ext", async _ =>
		{
			firstStarted.TrySetResult(true);
			await allowFirstToFinish.Task.ConfigureAwait(false);
			return true;
		}, CancellationToken.None);

		await firstStarted.Task.ConfigureAwait(false);

		Task<bool> canceledTask = scheduler.EnqueuePerDocumentAsync("test.ext", _ =>
		{
			Interlocked.Increment(ref canceledOperationCallCount);
			return Task.FromResult(true);
		}, cancellationTokenSource.Token);

		Task<bool> laterTask = scheduler.EnqueuePerDocumentAsync("test.ext", _ =>
		{
			Interlocked.Increment(ref laterOperationCallCount);
			return Task.FromResult(true);
		}, CancellationToken.None);

		cancellationTokenSource.Cancel();
		allowFirstToFinish.TrySetResult(true);

		await firstTask.ConfigureAwait(false);
		await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => canceledTask).ConfigureAwait(false);
		Assert.IsTrue(await laterTask.ConfigureAwait(false));

		Assert.AreEqual(0, canceledOperationCallCount);
		Assert.AreEqual(1, laterOperationCallCount);
	}

	[TestMethod]
	public async Task EnqueuePerDocumentAsync_NormalizesEquivalentPathsIntoSameQueue()
	{
		var scheduler = new DocumentOperationScheduler();
		string canonicalFilePath = Path.Combine(Path.GetTempPath(), "DocumentOperationSchedulerTests", "test.ext");

		string directoryPath = Path.GetDirectoryName(canonicalFilePath)
			?? throw new InvalidOperationException("The canonical test file path did not have a directory.");

		string aliasedFilePath = Path.Combine(directoryPath, ".", Path.GetFileName(canonicalFilePath));
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int secondOperationCallCount = 0;

		Task<bool> firstTask = scheduler.EnqueuePerDocumentAsync(canonicalFilePath, async _ =>
		{
			firstStarted.TrySetResult(true);
			await allowFirstToFinish.Task.ConfigureAwait(false);
			return true;
		}, CancellationToken.None);

		await firstStarted.Task.ConfigureAwait(false);

		Task<bool> secondTask = scheduler.EnqueuePerDocumentAsync(aliasedFilePath, _ =>
		{
			// Equivalent paths share one queue, so the second operation cannot start before the first finished;
			// checking the release point inside the delegate keeps the ordering assertion free of timing windows.
			Assert.IsTrue(allowFirstToFinish.Task.IsCompleted,
				"An aliased path must serialize behind the already-running operation for the canonical path.");

			Interlocked.Increment(ref secondOperationCallCount);
			return Task.FromResult(true);
		}, CancellationToken.None);

		allowFirstToFinish.TrySetResult(true);

		Assert.IsTrue(await firstTask.ConfigureAwait(false));
		Assert.IsTrue(await secondTask.ConfigureAwait(false));
		Assert.AreEqual(1, secondOperationCallCount);
	}

	[TestMethod]
	public async Task EnqueueLatestUpdateAsync_SerializesRunningWorkAndSkipsSupersededPendingUpdates()
	{
		var scheduler = new DocumentOperationScheduler();
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int secondStarted = 0;
		int thirdStarted = 0;

		Task firstTask = scheduler.EnqueueLatestUpdateAsync("test.ext", async _ =>
		{
			firstStarted.TrySetResult(true);
			await allowFirstToFinish.Task.ConfigureAwait(false);
		});

		await firstStarted.Task.ConfigureAwait(false);

		Task secondTask = scheduler.EnqueueLatestUpdateAsync("test.ext", _ =>
		{
			Interlocked.Increment(ref secondStarted);
			return Task.CompletedTask;
		});

		Task thirdTask = scheduler.EnqueueLatestUpdateAsync("test.ext", _ =>
		{
			// The pending slot runs only after the running update finished; checking the release point inside the
			// delegate keeps the serialization assertion free of timing windows.
			Assert.IsTrue(allowFirstToFinish.Task.IsCompleted,
				"A pending latest-update slot must wait for the running update before it executes.");

			Interlocked.Increment(ref thirdStarted);
			return Task.CompletedTask;
		});

		allowFirstToFinish.TrySetResult(true);

		await Task.WhenAll(firstTask, secondTask, thirdTask).ConfigureAwait(false);

		Assert.AreEqual(0, secondStarted);
		Assert.AreEqual(1, thirdStarted);
	}

	[TestMethod]
	public async Task EnqueueLatestUpdateAsync_SupersedingPendingWork_DoesNotCancelRunningUpdate()
	{
		var scheduler = new DocumentOperationScheduler();
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondQueued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int thirdStarted = 0;

		Task firstTask = scheduler.EnqueueLatestUpdateAsync("test.ext", async token =>
		{
			firstStarted.TrySetResult(true);
			await secondQueued.Task.ConfigureAwait(false);

			Assert.IsFalse(token.IsCancellationRequested, "A newer queued update should not cancel the already-running update.");
			Assert.IsNotNull(token.WaitHandle, "The running update should keep owning its token source until it finishes.");

			await allowFirstToFinish.Task.ConfigureAwait(false);
		});

		await firstStarted.Task.ConfigureAwait(false);

		Task secondTask = scheduler.EnqueueLatestUpdateAsync("test.ext", _ => Task.CompletedTask);
		Task thirdTask = scheduler.EnqueueLatestUpdateAsync("test.ext", _ =>
		{
			// The superseding update runs only after the running update finished; checking the release point
			// inside the delegate keeps the serialization assertion free of timing windows.
			Assert.IsTrue(allowFirstToFinish.Task.IsCompleted,
				"A superseding queued update must not start before the running update finishes.");

			Interlocked.Increment(ref thirdStarted);
			return Task.CompletedTask;
		});

		secondQueued.TrySetResult(true);
		allowFirstToFinish.TrySetResult(true);

		await Task.WhenAll(firstTask, secondTask, thirdTask).ConfigureAwait(false);

		Assert.AreEqual(1, thirdStarted);
	}

	[TestMethod]
	public async Task WaitForPerDocumentOperationsAsync_WaitsForQueuedLatestUpdatesForSamePath()
	{
		var scheduler = new DocumentOperationScheduler();
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int secondStarted = 0;
		Task waitTask = null!;

		Task firstTask = scheduler.EnqueueLatestUpdateAsync("test.ext", async _ =>
		{
			firstStarted.TrySetResult(true);
			await allowFirstToFinish.Task.ConfigureAwait(false);
		});

		await firstStarted.Task.ConfigureAwait(false);

		Task secondTask = scheduler.EnqueueLatestUpdateAsync("test.ext", _ =>
		{
			// The queued update is running at this point, so the wait cannot have completed yet; checking it here
			// keeps the assertion free of timing windows.
			Assert.IsFalse(waitTask.IsCompleted,
				"Waiting for document operations should include queued latest-update work for the same path.");

			Interlocked.Increment(ref secondStarted);
			return Task.CompletedTask;
		});

		waitTask = scheduler.WaitForPerDocumentOperationsAsync("test.ext", "test.ext");

		allowFirstToFinish.TrySetResult(true);

		await Task.WhenAll(firstTask, secondTask, waitTask).ConfigureAwait(false);

		Assert.AreEqual(1, secondStarted);
	}

	[TestMethod]
	public async Task EnqueueExclusivePerDocumentAsync_BlocksLaterPerDocumentWorkOnAffectedPath()
	{
		var scheduler = new DocumentOperationScheduler();
		var exclusiveStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowExclusiveToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int laterOperationCallCount = 0;

		Task<bool> exclusiveTask = scheduler.EnqueueExclusivePerDocumentAsync(
			"old.ext",
			"new.ext",
			async _ =>
			{
				exclusiveStarted.TrySetResult(true);
				await allowExclusiveToFinish.Task.ConfigureAwait(false);
				return true;
			},
			CancellationToken.None);

		await exclusiveStarted.Task.ConfigureAwait(false);

		Task<bool> laterTask = scheduler.EnqueuePerDocumentAsync("new.ext", _ =>
		{
			// The exclusive operation still owns an affected path here; checking the release point inside the
			// delegate keeps the blocking assertion free of timing windows.
			Assert.IsTrue(allowExclusiveToFinish.Task.IsCompleted,
				"Per-document work on a path affected by an exclusive operation must wait for it to finish.");

			Interlocked.Increment(ref laterOperationCallCount);
			return Task.FromResult(true);
		}, CancellationToken.None);

		allowExclusiveToFinish.TrySetResult(true);

		Assert.IsTrue(await exclusiveTask.ConfigureAwait(false));
		Assert.IsTrue(await laterTask.ConfigureAwait(false));

		Assert.AreEqual(1, laterOperationCallCount);
	}

	[TestMethod]
	public async Task WaitForPerDocumentOperationsAsync_WaitsForActiveExclusiveOperation()
	{
		var scheduler = new DocumentOperationScheduler();
		var exclusiveStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowExclusiveToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		Task waitTask = null!;

		Task<bool> exclusiveTask = scheduler.EnqueueExclusivePerDocumentAsync(
			"old.ext",
			"new.ext",
			async _ =>
			{
				exclusiveStarted.TrySetResult(true);
				await allowExclusiveToFinish.Task.ConfigureAwait(false);

				// The exclusive operation is still running here, so the wait cannot have completed yet; checking it
				// here keeps the assertion free of timing windows.
				Assert.IsFalse(waitTask.IsCompleted,
					"Waiting for document operations should include an active exclusive operation for the affected paths.");

				return true;
			},
			CancellationToken.None);

		await exclusiveStarted.Task.ConfigureAwait(false);

		waitTask = scheduler.WaitForPerDocumentOperationsAsync("old.ext", "new.ext");

		allowExclusiveToFinish.TrySetResult(true);

		Assert.IsTrue(await exclusiveTask.ConfigureAwait(false));
		await waitTask.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task EnqueueExclusivePerDocumentAsync_WhileLatestUpdateIsRunning_CompletesWithoutDeadlock()
	{
		var scheduler = new DocumentOperationScheduler();
		var updateStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowUpdateToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int exclusiveStarted = 0;
		int laterWorkStarted = 0;

		Task updateTask = scheduler.EnqueueLatestUpdateAsync("test.ext", async _ =>
		{
			updateStarted.TrySetResult(true);
			await allowUpdateToFinish.Task.ConfigureAwait(false);
		});

		await updateStarted.Task.ConfigureAwait(false);

		// The exclusive operation arrives while the update is still running and must wait for it, while the work
		// queued afterwards must wait for the exclusive operation. None of the participants may wait for itself.
		Task<bool> exclusiveTask = scheduler.EnqueueExclusivePerDocumentAsync("test.ext", "renamed.ext", _ =>
		{
			Interlocked.Increment(ref exclusiveStarted);
			return Task.FromResult(true);
		}, CancellationToken.None);

		Task<bool> laterTask = scheduler.EnqueuePerDocumentAsync("test.ext", _ =>
		{
			Interlocked.Increment(ref laterWorkStarted);
			return Task.FromResult(true);
		}, CancellationToken.None);

		allowUpdateToFinish.TrySetResult(true);

		await Task.WhenAll(updateTask, exclusiveTask, laterTask).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.AreEqual(1, exclusiveStarted);
		Assert.AreEqual(1, laterWorkStarted);
	}

	[TestMethod]
	public async Task EnqueueExclusivePerDocumentAsync_WithEquivalentPaths_ClaimsOneChain()
	{
		var scheduler = new DocumentOperationScheduler();
		string canonicalFilePath = Path.Combine(Path.GetTempPath(), "DocumentOperationSchedulerTests", "test.ext");

		string directoryPath = Path.GetDirectoryName(canonicalFilePath)
			?? throw new InvalidOperationException("The canonical test file path did not have a directory.");

		string aliasedFilePath = Path.Combine(directoryPath, ".", Path.GetFileName(canonicalFilePath));
		var exclusiveStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowExclusiveToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		Task waitTask = null!;
		int laterOperationCallCount = 0;

		Task<bool> exclusiveTask = scheduler.EnqueueExclusivePerDocumentAsync(
			canonicalFilePath,
			aliasedFilePath,
			async _ =>
			{
				exclusiveStarted.TrySetResult(true);
				await allowExclusiveToFinish.Task.ConfigureAwait(false);

				// Equivalent paths collapse into one affected path, so a wait for either spelling must include the
				// exclusive operation; checking it here keeps the assertion free of timing windows.
				Assert.IsFalse(waitTask.IsCompleted,
					"An exclusive operation that names one document twice must still be awaited by that document's wait.");

				return true;
			},
			CancellationToken.None);

		await exclusiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		waitTask = scheduler.WaitForPerDocumentOperationsAsync(canonicalFilePath, aliasedFilePath);

		Task<bool> laterTask = scheduler.EnqueuePerDocumentAsync(aliasedFilePath, _ =>
		{
			// The aliased spelling belongs to the same single affected path, so work queued for it must wait for the
			// exclusive operation; checking the release point inside the delegate keeps the assertion timing-free.
			Assert.IsTrue(allowExclusiveToFinish.Task.IsCompleted,
				"Work on an aliased spelling of an affected path must wait for the exclusive operation.");

			Interlocked.Increment(ref laterOperationCallCount);
			return Task.FromResult(true);
		}, CancellationToken.None);

		allowExclusiveToFinish.TrySetResult(true);

		Assert.IsTrue(await exclusiveTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
		Assert.IsTrue(await laterTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
		await waitTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.AreEqual(1, laterOperationCallCount);
	}

	[TestMethod]
	public async Task EnqueueExclusivePerDocumentAsync_DoesNotGateUnaffectedPaths()
	{
		var scheduler = new DocumentOperationScheduler();
		var exclusiveStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowExclusiveToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<bool> exclusiveTask = scheduler.EnqueueExclusivePerDocumentAsync(
			"old.ext",
			"new.ext",
			async _ =>
			{
				exclusiveStarted.TrySetResult(true);
				await allowExclusiveToFinish.Task.ConfigureAwait(false);
				return true;
			},
			CancellationToken.None);

		await exclusiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		// The exclusive operation is still running, so the unaffected path can only complete if exclusive operations
		// gate nothing but their own affected paths.
		bool unaffectedRan = await scheduler.EnqueuePerDocumentAsync("unaffected.ext", _ => Task.FromResult(true), CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.IsTrue(unaffectedRan);

		allowExclusiveToFinish.TrySetResult(true);

		Assert.IsTrue(await exclusiveTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task EnqueueGlobalAsync_WaitsForRunningExclusiveOperation()
	{
		var scheduler = new DocumentOperationScheduler();
		var exclusiveStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowExclusiveToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int globalRunCount = 0;

		Task<bool> exclusiveTask = scheduler.EnqueueExclusivePerDocumentAsync(
			"old.ext",
			"new.ext",
			async _ =>
			{
				exclusiveStarted.TrySetResult(true);
				await allowExclusiveToFinish.Task.ConfigureAwait(false);
				return true;
			},
			CancellationToken.None);

		await exclusiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Task<bool> globalTask = scheduler.EnqueueGlobalAsync(_ =>
		{
			// A global operation shares the exclusive operation's global chain, so it cannot start before the
			// exclusive operation finished; the assertion runs after the release for that reason.
			Assert.IsTrue(allowExclusiveToFinish.Task.IsCompleted,
				"A global operation queued after an exclusive operation must not start while it still runs.");

			Interlocked.Increment(ref globalRunCount);
			return Task.FromResult(true);
		}, CancellationToken.None);

		allowExclusiveToFinish.TrySetResult(true);

		Assert.IsTrue(await exclusiveTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
		Assert.IsTrue(await globalTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));

		Assert.AreEqual(1, globalRunCount);
	}

	[TestMethod]
	public async Task EnqueuePerDocumentAsync_FromInsideAnOperationForTheSamePath_FailsFast()
	{
		var scheduler = new DocumentOperationScheduler();

		Task<bool> outerTask = scheduler.EnqueuePerDocumentAsync("test.ext", async _ =>
		{
			await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
				scheduler.EnqueuePerDocumentAsync("test.ext", _ => Task.FromResult(true), CancellationToken.None)).ConfigureAwait(false);

			return true;
		}, CancellationToken.None);

		Assert.IsTrue(await outerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task EnqueueLatestUpdateAsync_FromInsideAnUpdateForTheSamePath_FailsFast()
	{
		var scheduler = new DocumentOperationScheduler();

		Task updateTask = scheduler.EnqueueLatestUpdateAsync("test.ext", async _ =>
		{
			await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
				scheduler.EnqueueLatestUpdateAsync("test.ext", _ => Task.CompletedTask)).ConfigureAwait(false);
		});

		await updateTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task WaitForPerDocumentOperationsAsync_FromInsideAnOperationForTheSamePath_FailsFast()
	{
		var scheduler = new DocumentOperationScheduler();

		Task<bool> outerTask = scheduler.EnqueuePerDocumentAsync("test.ext", async _ =>
		{
			await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
				scheduler.WaitForPerDocumentOperationsAsync("test.ext", "test.ext")).ConfigureAwait(false);

			return true;
		}, CancellationToken.None);

		Assert.IsTrue(await outerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task EnqueueGlobalAsync_FromInsideAGlobalOperation_FailsFast()
	{
		var scheduler = new DocumentOperationScheduler();

		Task<bool> outerTask = scheduler.EnqueueGlobalAsync(async _ =>
		{
			await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
				scheduler.EnqueueGlobalAsync(_ => Task.FromResult(true), CancellationToken.None)).ConfigureAwait(false);

			return true;
		}, CancellationToken.None);

		Assert.IsTrue(await outerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task EnqueuePerDocumentAsync_FromInsideAnOperationForAnotherPath_FailsFast()
	{
		var scheduler = new DocumentOperationScheduler();

		Task<bool> outerTask = scheduler.EnqueuePerDocumentAsync("first.ext", async _ =>
		{
			await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
				scheduler.EnqueuePerDocumentAsync("second.ext", _ => Task.FromResult(true), CancellationToken.None)).ConfigureAwait(false);

			return true;
		}, CancellationToken.None);

		Assert.IsTrue(await outerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task EnqueueGlobalAsync_FromInsideAPerDocumentOperation_FailsFast()
	{
		var scheduler = new DocumentOperationScheduler();

		Task<bool> outerTask = scheduler.EnqueuePerDocumentAsync("first.ext", async _ =>
		{
			await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
				scheduler.EnqueueGlobalAsync(_ => Task.FromResult(true), CancellationToken.None)).ConfigureAwait(false);

			return true;
		}, CancellationToken.None);

		Assert.IsTrue(await outerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task EnqueueExclusivePerDocumentAsync_FromInsideAnotherOperation_FailsFast()
	{
		var scheduler = new DocumentOperationScheduler();

		Task<bool> outerTask = scheduler.EnqueuePerDocumentAsync("first.ext", async _ =>
		{
			await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
				scheduler.EnqueueExclusivePerDocumentAsync("first.ext", "second.ext", _ => Task.FromResult(true), CancellationToken.None)).ConfigureAwait(false);

			return true;
		}, CancellationToken.None);

		Assert.IsTrue(await outerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task CancelQueuedUpdate_SkipsPendingUpdateWithoutRunningItsDelegate()
	{
		var scheduler = new DocumentOperationScheduler();
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int pendingRan = 0;

		Task firstTask = scheduler.EnqueueLatestUpdateAsync("test.ext", async _ =>
		{
			firstStarted.TrySetResult(true);
			await allowFirstToFinish.Task.ConfigureAwait(false);
		});

		await firstStarted.Task.ConfigureAwait(false);

		Task pendingTask = scheduler.EnqueueLatestUpdateAsync("test.ext", _ =>
		{
			Interlocked.Increment(ref pendingRan);
			return Task.CompletedTask;
		});

		scheduler.CancelQueuedUpdate("test.ext");
		allowFirstToFinish.TrySetResult(true);

		await firstTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
		await pendingTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.AreEqual(0, pendingRan);
	}

	[TestMethod]
	public async Task CancelAllQueuedUpdates_SkipsEveryPendingUpdate()
	{
		var scheduler = new DocumentOperationScheduler();
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowSecondToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int pendingRan = 0;

		Task runningFirst = scheduler.EnqueueLatestUpdateAsync("first.ext", async _ =>
		{
			firstStarted.TrySetResult(true);
			await allowFirstToFinish.Task.ConfigureAwait(false);
		});

		Task runningSecond = scheduler.EnqueueLatestUpdateAsync("second.ext", async _ =>
		{
			secondStarted.TrySetResult(true);
			await allowSecondToFinish.Task.ConfigureAwait(false);
		});

		await Task.WhenAll(firstStarted.Task, secondStarted.Task).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Task pendingFirst = scheduler.EnqueueLatestUpdateAsync("first.ext", _ =>
		{
			Interlocked.Increment(ref pendingRan);
			return Task.CompletedTask;
		});

		Task pendingSecond = scheduler.EnqueueLatestUpdateAsync("second.ext", _ =>
		{
			Interlocked.Increment(ref pendingRan);
			return Task.CompletedTask;
		});

		scheduler.CancelAllQueuedUpdates();
		allowFirstToFinish.TrySetResult(true);
		allowSecondToFinish.TrySetResult(true);

		await Task.WhenAll(runningFirst, runningSecond).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
		await Task.WhenAll(pendingFirst, pendingSecond).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.AreEqual(0, pendingRan);
	}

	[TestMethod]
	public async Task CancelQueuedUpdate_WhenRunningUpdateWasSuperseded_StopsTheRunningDelegate()
	{
		var scheduler = new DocumentOperationScheduler();
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondQueued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int firstCancellationObserved = 0;

		Task firstTask = scheduler.EnqueueLatestUpdateAsync("test.ext", async token =>
		{
			firstStarted.TrySetResult(true);
			await secondQueued.Task.ConfigureAwait(false);

			try
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				Interlocked.Increment(ref firstCancellationObserved);
				throw;
			}
		});

		await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Task secondTask = scheduler.EnqueueLatestUpdateAsync("test.ext", _ => Task.CompletedTask);
		secondQueued.TrySetResult(true);

		// The enqueue superseded the running update in the slot; the cancel must still reach it instead of only
		// skipping the newer pending update.
		scheduler.CancelQueuedUpdate("test.ext");

		await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await firstTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)).ConfigureAwait(false);
		await secondTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.AreEqual(1, firstCancellationObserved);
	}

	[TestMethod]
	public async Task EnqueueGlobalAsync_SerializesBehindRunningGlobalOperationAndRuns()
	{
		var scheduler = new DocumentOperationScheduler();
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int secondRunCount = 0;

		Task<bool> firstTask = scheduler.EnqueueGlobalAsync(async _ =>
		{
			firstStarted.TrySetResult(true);
			await allowFirstToFinish.Task.ConfigureAwait(false);
			return true;
		}, CancellationToken.None);

		await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Task<bool> secondTask = scheduler.EnqueueGlobalAsync(_ =>
		{
			Assert.IsTrue(allowFirstToFinish.Task.IsCompleted,
				"A global operation must not start while another global operation is still running.");

			Interlocked.Increment(ref secondRunCount);
			return Task.FromResult(true);
		}, CancellationToken.None);

		// The later global operation stays queued until the running one releases: its delegate asserts that the
		// running operation already completed, so a start that overtook the release faults the delegate itself
		// instead of needing a bounded timing window to observe the absence.
		allowFirstToFinish.TrySetResult(true);

		Assert.IsTrue(await firstTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
		Assert.IsTrue(await secondTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
		Assert.AreEqual(1, secondRunCount);
	}

	[TestMethod]
	public async Task RunDetachedFromActiveContext_WorkStartedInsideAnOperation_DoesNotInheritTheReentrancyScope()
	{
		var scheduler = new DocumentOperationScheduler();
		var detachedEnqueueCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<bool> outerTask = scheduler.EnqueuePerDocumentAsync("first.ext", async _ =>
		{
			// Start detached work through the helper; it enqueues while this operation is still running. Without the
			// detach, the enqueue would inherit this slot's re-entrancy scope and throw instead of completing.
			scheduler.RunDetachedFromActiveContext(() => StartDetachedEnqueue(scheduler, detachedEnqueueCompleted));

			Assert.IsTrue(await detachedEnqueueCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));

			return true;
		}, CancellationToken.None);

		Assert.IsTrue(await outerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task ReentrancyGuard_WorkStartedFromInsideAnOperationWithoutDetaching_FailsFast()
	{
		var scheduler = new DocumentOperationScheduler();
		var detachedEnqueueOutcome = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<bool> outerTask = scheduler.EnqueuePerDocumentAsync("first.ext", async operationToken =>
		{
			// A queued task captures this operation's flow, so its enqueue must be rejected while the operation runs;
			// the operation stays alive until the detached task reports, so the rejection is deterministic.
			_ = Task.Run(async () =>
			{
				try
				{
					await scheduler.EnqueuePerDocumentAsync("second.ext", _ => Task.FromResult(true), CancellationToken.None).ConfigureAwait(false);
					detachedEnqueueOutcome.TrySetResult(null);
				}
				catch (Exception exception)
				{
					detachedEnqueueOutcome.TrySetResult(exception);
				}
			});

			Exception? failure = await detachedEnqueueOutcome.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
			Assert.IsInstanceOfType(failure, typeof(InvalidOperationException));

			return true;
		}, CancellationToken.None);

		Assert.IsTrue(await outerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
	}

	private static void StartDetachedEnqueue(DocumentOperationScheduler scheduler, TaskCompletionSource<bool> completed)
	{
		_ = Task.Run(async () =>
		{
			try
			{
				bool ran = await scheduler.EnqueuePerDocumentAsync("second.ext", _ => Task.FromResult(true), CancellationToken.None).ConfigureAwait(false);
				completed.TrySetResult(ran);
			}
			catch (Exception exception)
			{
				completed.TrySetException(exception);
			}
		});
	}
}
