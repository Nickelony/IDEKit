using System.Runtime.CompilerServices;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class DocumentOperationSchedulerTests
{
	[TestMethod]
	public async Task EnqueuePerDocumentAsync_CanceledWhileWaiting_DoesNotInvokeDelegate()
	{
		var scheduler = new DocumentOperationScheduler();
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cancellationTokenSource = new CancellationTokenSource();

		int canceledOperationCallCount = 0;

		Task<bool> firstTask = scheduler.EnqueuePerDocumentAsync(
			TestPaths.Script("test.ext"),

			async _ =>
			{
				firstStarted.TrySetResult(true);
				await allowFirstToFinish.Task.ConfigureAwait(false);
				return true;
			},

			CancellationToken.None
		);

		await firstStarted.Task.ConfigureAwait(false);

		Task<bool> canceledTask = scheduler.EnqueuePerDocumentAsync(
			TestPaths.Script("test.ext"),

			_ =>
			{
				Interlocked.Increment(ref canceledOperationCallCount);
				return Task.FromResult(true);
			},

			cancellationTokenSource.Token
		);

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

		Task<bool> firstTask = scheduler.EnqueuePerDocumentAsync(
			TestPaths.Script("test.ext"),

			async _ =>
			{
				firstStarted.TrySetResult(true);
				await allowFirstToFinish.Task.ConfigureAwait(false);
				return true;
			},

			CancellationToken.None
		);

		await firstStarted.Task.ConfigureAwait(false);

		Task<bool> canceledTask = scheduler.EnqueuePerDocumentAsync(
			TestPaths.Script("test.ext"),

			_ =>
			{
				Interlocked.Increment(ref canceledOperationCallCount);
				return Task.FromResult(true);
			},

			cancellationTokenSource.Token
		);

		Task<bool> laterTask = scheduler.EnqueuePerDocumentAsync(
			TestPaths.Script("test.ext"),

			_ =>
			{
				Interlocked.Increment(ref laterOperationCallCount);
				return Task.FromResult(true);
			},

			CancellationToken.None
		);

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

		string directoryPath =
			Path.GetDirectoryName(canonicalFilePath) ?? throw new InvalidOperationException("The canonical test file path did not have a directory.");

		string aliasedFilePath = Path.Combine(directoryPath, ".", Path.GetFileName(canonicalFilePath));

		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int secondOperationCallCount = 0;

		Task<bool> firstTask = scheduler.EnqueuePerDocumentAsync(
			canonicalFilePath,

			async _ =>
			{
				firstStarted.TrySetResult(true);
				await allowFirstToFinish.Task.ConfigureAwait(false);
				return true;
			},

			CancellationToken.None
		);

		await firstStarted.Task.ConfigureAwait(false);

		Task<bool> secondTask = scheduler.EnqueuePerDocumentAsync(
			aliasedFilePath,

			_ =>
			{
				// Equivalent paths share one queue, so the second operation cannot start before the first finished;
				// checking the release point inside the delegate keeps the ordering assertion free of timing windows.
				Assert.IsTrue(
					allowFirstToFinish.Task.IsCompleted,
					"An aliased path must serialize behind the already-running operation for the canonical path."
				);

				Interlocked.Increment(ref secondOperationCallCount);
				return Task.FromResult(true);
			},

			CancellationToken.None
		);

		allowFirstToFinish.TrySetResult(true);

		Assert.IsTrue(await firstTask.ConfigureAwait(false));
		Assert.IsTrue(await secondTask.ConfigureAwait(false));
		Assert.AreEqual(1, secondOperationCallCount);
	}

	[TestMethod]
	public async Task EnqueuePerDocumentAsync_UnderACaseInsensitiveComparison_QueuesACaseVariantBehindTheRunningOperation()
	{
		var scheduler = new DocumentOperationScheduler(TestOptions.CaseInsensitive);
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int secondOperationCallCount = 0;

		Task<bool> firstTask = scheduler.EnqueuePerDocumentAsync(
			TestPaths.Script("Case.lua"),

			async _ =>
			{
				firstStarted.TrySetResult(true);
				await allowFirstToFinish.Task.ConfigureAwait(false);
				return true;
			},

			CancellationToken.None
		);

		await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		Task<bool> secondTask = scheduler.EnqueuePerDocumentAsync(
			TestPaths.Script("CASE.lua"),

			_ =>
			{
				Interlocked.Increment(ref secondOperationCallCount);
				return Task.FromResult(true);
			},

			CancellationToken.None
		);

		// The first operation is still running, so the case variant can only start if the scheduler treats it as another document.
		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(0, Volatile.Read(ref secondOperationCallCount));

		allowFirstToFinish.TrySetResult(true);

		Assert.IsTrue(await firstTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
		Assert.IsTrue(await secondTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
		Assert.AreEqual(1, secondOperationCallCount);
	}

	[TestMethod]
	public async Task EnqueuePerDocumentAsync_UnderACaseSensitiveComparison_RunsACaseVariantWhileTheOtherOperationRuns()
	{
		var scheduler = new DocumentOperationScheduler(TestOptions.CaseSensitive);
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<bool> firstTask = scheduler.EnqueuePerDocumentAsync(
			TestPaths.Script("Case.lua"),

			async _ =>
			{
				firstStarted.TrySetResult(true);
				await allowFirstToFinish.Task.ConfigureAwait(false);
				return true;
			},

			CancellationToken.None
		);

		await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		// The first operation is still running, so the case variant can only complete if the scheduler keeps it apart.
		bool variantRan = await scheduler
			.EnqueuePerDocumentAsync(TestPaths.Script("CASE.lua"), _ => Task.FromResult(true), CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.IsTrue(variantRan);

		allowFirstToFinish.TrySetResult(true);

		Assert.IsTrue(await firstTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task EnqueueLatestUpdateAsync_SerializesRunningWorkAndSkipsSupersededPendingUpdates()
	{
		var scheduler = new DocumentOperationScheduler();
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int secondStarted = 0;
		int thirdStarted = 0;

		Task firstTask = scheduler.EnqueueLatestUpdateAsync(
			TestPaths.Script("test.ext"),

			async _ =>
			{
				firstStarted.TrySetResult(true);
				await allowFirstToFinish.Task.ConfigureAwait(false);
			}
		);

		await firstStarted.Task.ConfigureAwait(false);

		Task secondTask = scheduler.EnqueueLatestUpdateAsync(
			TestPaths.Script("test.ext"),

			_ =>
			{
				Interlocked.Increment(ref secondStarted);
				return Task.CompletedTask;
			}
		);

		Task thirdTask = scheduler.EnqueueLatestUpdateAsync(
			TestPaths.Script("test.ext"),

			_ =>
			{
				// The pending slot runs only after the running update finished; checking the release point inside the
				// delegate keeps the serialization assertion free of timing windows.
				Assert.IsTrue(
					allowFirstToFinish.Task.IsCompleted,
					"A pending latest-update slot must wait for the running update before it executes."
				);

				Interlocked.Increment(ref thirdStarted);
				return Task.CompletedTask;
			}
		);

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

		Task firstTask = scheduler.EnqueueLatestUpdateAsync(
			TestPaths.Script("test.ext"),

			async cancellationToken =>
			{
				firstStarted.TrySetResult(true);
				await secondQueued.Task.ConfigureAwait(false);

				Assert.IsFalse(cancellationToken.IsCancellationRequested, "Enqueuing a newer update should not cancel the already-running update.");
				Assert.IsNotNull(cancellationToken.WaitHandle, "The running update should keep owning its token source until it finishes.");

				await allowFirstToFinish.Task.ConfigureAwait(false);
			}
		);

		await firstStarted.Task.ConfigureAwait(false);

		Task secondTask = scheduler.EnqueueLatestUpdateAsync(TestPaths.Script("test.ext"), _ => Task.CompletedTask);

		Task thirdTask = scheduler.EnqueueLatestUpdateAsync(
			TestPaths.Script("test.ext"),

			_ =>
			{
				// The superseding update runs only after the running update finished; checking the release point
				// inside the delegate keeps the serialization assertion free of timing windows.
				Assert.IsTrue(allowFirstToFinish.Task.IsCompleted, "A superseding update must not start before the running update finishes.");

				Interlocked.Increment(ref thirdStarted);
				return Task.CompletedTask;
			}
		);

		secondQueued.TrySetResult(true);
		allowFirstToFinish.TrySetResult(true);

		await Task.WhenAll(firstTask, secondTask, thirdTask).ConfigureAwait(false);

		Assert.AreEqual(1, thirdStarted);
	}

	[TestMethod]
	public async Task EnqueueExclusivePerDocumentAsync_BlocksLaterPerDocumentWorkOnAffectedPath()
	{
		var scheduler = new DocumentOperationScheduler();
		var exclusiveStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowExclusiveToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int laterOperationCallCount = 0;

		Task<bool> exclusiveTask = scheduler.EnqueueExclusivePerDocumentAsync(
			firstFilePath: TestPaths.Script("old.ext"),
			secondFilePath: TestPaths.Script("new.ext"),

			operation: async _ =>
			{
				exclusiveStarted.TrySetResult(true);
				await allowExclusiveToFinish.Task.ConfigureAwait(false);
				return true;
			},

			cancellationToken: CancellationToken.None
		);

		await exclusiveStarted.Task.ConfigureAwait(false);

		Task<bool> laterTask = scheduler.EnqueuePerDocumentAsync(
			TestPaths.Script("new.ext"),

			_ =>
			{
				// The exclusive operation still owns an affected path here; checking the release point inside the
				// delegate keeps the blocking assertion free of timing windows.
				Assert.IsTrue(
					allowExclusiveToFinish.Task.IsCompleted,
					"Per-document work on a path affected by an exclusive operation must wait for it to finish."
				);

				Interlocked.Increment(ref laterOperationCallCount);
				return Task.FromResult(true);
			},

			CancellationToken.None
		);

		allowExclusiveToFinish.TrySetResult(true);

		Assert.IsTrue(await exclusiveTask.ConfigureAwait(false));
		Assert.IsTrue(await laterTask.ConfigureAwait(false));

		Assert.AreEqual(1, laterOperationCallCount);
	}

	[TestMethod]
	public async Task EnqueueExclusivePerDocumentAsync_WhileLatestUpdateIsRunning_CompletesWithoutDeadlock()
	{
		var scheduler = new DocumentOperationScheduler();
		var updateStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowUpdateToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int exclusiveStarted = 0;
		int laterWorkStarted = 0;

		Task updateTask = scheduler.EnqueueLatestUpdateAsync(
			TestPaths.Script("test.ext"),

			async _ =>
			{
				updateStarted.TrySetResult(true);
				await allowUpdateToFinish.Task.ConfigureAwait(false);
			}
		);

		await updateStarted.Task.ConfigureAwait(false);

		// The exclusive operation arrives while the update is still running and must wait for it, while the work
		// queued afterwards must wait for the exclusive operation. None of the participants may wait for itself.
		Task<bool> exclusiveTask = scheduler.EnqueueExclusivePerDocumentAsync(
			firstFilePath: TestPaths.Script("test.ext"),
			secondFilePath: TestPaths.Script("renamed.ext"),

			operation: _ =>
			{
				Interlocked.Increment(ref exclusiveStarted);
				return Task.FromResult(true);
			},

			cancellationToken: CancellationToken.None
		);

		Task<bool> laterTask = scheduler.EnqueuePerDocumentAsync(
			TestPaths.Script("test.ext"),

			_ =>
			{
				Interlocked.Increment(ref laterWorkStarted);
				return Task.FromResult(true);
			},

			CancellationToken.None
		);

		allowUpdateToFinish.TrySetResult(true);

		await Task.WhenAll(updateTask, exclusiveTask, laterTask)
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(1, exclusiveStarted);
		Assert.AreEqual(1, laterWorkStarted);
	}

	[TestMethod]
	public async Task EnqueueExclusivePerDocumentAsync_WithEquivalentPaths_ClaimsOneLane()
	{
		var scheduler = new DocumentOperationScheduler();
		string canonicalFilePath = Path.Combine(Path.GetTempPath(), "DocumentOperationSchedulerTests", "test.ext");

		string directoryPath =
			Path.GetDirectoryName(canonicalFilePath) ?? throw new InvalidOperationException("The canonical test file path did not have a directory.");

		string aliasedFilePath = Path.Combine(directoryPath, ".", Path.GetFileName(canonicalFilePath));

		var exclusiveStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowExclusiveToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int laterOperationCallCount = 0;

		Task<bool> exclusiveTask = scheduler.EnqueueExclusivePerDocumentAsync(
			firstFilePath: canonicalFilePath,
			secondFilePath: aliasedFilePath,

			operation: async _ =>
			{
				exclusiveStarted.TrySetResult(true);
				await allowExclusiveToFinish.Task.ConfigureAwait(false);
				return true;
			},

			cancellationToken: CancellationToken.None
		);

		await exclusiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		Task<bool> laterTask = scheduler.EnqueuePerDocumentAsync(
			aliasedFilePath,

			_ =>
			{
				// The aliased spelling belongs to the same single affected path, so work queued for it must wait for the
				// exclusive operation; checking the release point inside the delegate keeps the assertion timing-free.
				Assert.IsTrue(
					allowExclusiveToFinish.Task.IsCompleted,
					"Work on an aliased spelling of an affected path must wait for the exclusive operation."
				);

				Interlocked.Increment(ref laterOperationCallCount);
				return Task.FromResult(true);
			},

			CancellationToken.None
		);

		allowExclusiveToFinish.TrySetResult(true);

		Assert.IsTrue(await exclusiveTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
		Assert.IsTrue(await laterTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));

		Assert.AreEqual(1, laterOperationCallCount);
	}

	[TestMethod]
	public async Task EnqueueExclusivePerDocumentAsync_DoesNotGateUnaffectedPaths()
	{
		var scheduler = new DocumentOperationScheduler();
		var exclusiveStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowExclusiveToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<bool> exclusiveTask = scheduler.EnqueueExclusivePerDocumentAsync(
			firstFilePath: TestPaths.Script("old.ext"),
			secondFilePath: TestPaths.Script("new.ext"),

			operation: async _ =>
			{
				exclusiveStarted.TrySetResult(true);
				await allowExclusiveToFinish.Task.ConfigureAwait(false);
				return true;
			},

			cancellationToken: CancellationToken.None
		);

		await exclusiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		// The exclusive operation is still running, so the unaffected path can only complete if exclusive operations
		// gate nothing but their own affected paths.
		bool unaffectedRan = await scheduler
			.EnqueuePerDocumentAsync(TestPaths.Script("unaffected.ext"), _ => Task.FromResult(true), CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.IsTrue(unaffectedRan);

		allowExclusiveToFinish.TrySetResult(true);

		Assert.IsTrue(await exclusiveTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task EnqueueExclusivePerDocumentAsync_WhileAnotherExclusiveOperationRuns_WaitsEvenOnUnrelatedPaths()
	{
		var scheduler = new DocumentOperationScheduler();
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<bool> firstTask = scheduler.EnqueueExclusivePerDocumentAsync(
			firstFilePath: TestPaths.Script("old.ext"),
			secondFilePath: TestPaths.Script("new.ext"),

			operation: async _ =>
			{
				firstStarted.TrySetResult(true);
				await allowFirstToFinish.Task.ConfigureAwait(false);
				return true;
			},

			cancellationToken: CancellationToken.None
		);

		await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		Task<bool> secondTask = scheduler.EnqueueExclusivePerDocumentAsync(
			firstFilePath: TestPaths.Script("other-old.ext"),
			secondFilePath: TestPaths.Script("other-new.ext"),
			operation: _ => Task.FromResult(true),
			cancellationToken: CancellationToken.None
		);

		// No path overlaps, so only the lane every exclusive operation shares can hold the second operation back; an absence has
		// no completion signal, so the bounded window proves it.
		Task completedTask = await Task.WhenAny(secondTask, Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreNotSame(secondTask, completedTask, "An exclusive operation queued behind a running one must not start while it still runs.");

		allowFirstToFinish.TrySetResult(true);

		Assert.IsTrue(await firstTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
		Assert.IsTrue(await secondTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task EnqueuePerDocumentAsync_FromInsideAnOperationForTheSamePath_FailsFast()
	{
		var scheduler = new DocumentOperationScheduler();

		Task<bool> outerTask = scheduler.EnqueuePerDocumentAsync(
			TestPaths.Script("test.ext"),

			async _ =>
			{
				await Assert
					.ThrowsExactlyAsync<InvalidOperationException>(() =>
						scheduler.EnqueuePerDocumentAsync(TestPaths.Script("test.ext"), _ => Task.FromResult(true), CancellationToken.None)
					)
					.ConfigureAwait(false);

				return true;
			},

			CancellationToken.None
		);

		Assert.IsTrue(await outerTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task EnqueueLatestUpdateAsync_FromInsideAnUpdateForTheSamePath_FailsFast()
	{
		var scheduler = new DocumentOperationScheduler();

		Task updateTask = scheduler.EnqueueLatestUpdateAsync(
			TestPaths.Script("test.ext"),

			async _ =>
			{
				await Assert
					.ThrowsExactlyAsync<InvalidOperationException>(() =>
						scheduler.EnqueueLatestUpdateAsync(TestPaths.Script("test.ext"), _ => Task.CompletedTask)
					)
					.ConfigureAwait(false);
			}
		);

		await updateTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task EnqueuePerDocumentAsync_FromInsideAnOperationForAnotherPath_FailsFast()
	{
		var scheduler = new DocumentOperationScheduler();

		Task<bool> outerTask = scheduler.EnqueuePerDocumentAsync(
			TestPaths.Script("first.ext"),

			async _ =>
			{
				await Assert
					.ThrowsExactlyAsync<InvalidOperationException>(() =>
						scheduler.EnqueuePerDocumentAsync(TestPaths.Script("second.ext"), _ => Task.FromResult(true), CancellationToken.None)
					)
					.ConfigureAwait(false);

				return true;
			},

			CancellationToken.None
		);

		Assert.IsTrue(await outerTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task EnqueueExclusivePerDocumentAsync_FromInsideAnotherOperation_FailsFast()
	{
		var scheduler = new DocumentOperationScheduler();

		Task<bool> outerTask = scheduler.EnqueuePerDocumentAsync(
			TestPaths.Script("first.ext"),

			async _ =>
			{
				await Assert
					.ThrowsExactlyAsync<InvalidOperationException>(() =>
						scheduler.EnqueueExclusivePerDocumentAsync(
							firstFilePath: TestPaths.Script("first.ext"),
							secondFilePath: TestPaths.Script("second.ext"),
							operation: _ => Task.FromResult(true),
							cancellationToken: CancellationToken.None
						)
					)
					.ConfigureAwait(false);

				return true;
			},

			CancellationToken.None
		);

		Assert.IsTrue(await outerTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task EnqueueLatestUpdateAsync_AfterTheUpdateSettled_ReleasesItsCancellationSource()
	{
		var scheduler = new DocumentOperationScheduler();

		CancellationToken updateToken = default;

		await scheduler
			.EnqueueLatestUpdateAsync(
				TestPaths.Script("test.ext"),

				cancellationToken =>
				{
					updateToken = cancellationToken;
					return Task.CompletedTask;
				}
			)
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		// The caller's task settles before the node releases the source, so the release is awaited instead of assumed.
		await TestPolling
			.UntilAsync(
				() =>
				{
					try
					{
						_ = updateToken.WaitHandle;
						return false;
					}
					catch (ObjectDisposedException)
					{
						return true;
					}
				},

				TestPolling.DefaultTimeout,
				"The scheduler must release the cancellation source of a settled update."
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task EnqueueLatestUpdateAsync_AfterTheUpdateSettled_ReleasesThePath()
	{
		var scheduler = new DocumentOperationScheduler();

		(Task updateTask, WeakReference path) = EnqueueUpdateForAFreshPath(scheduler);

		await updateTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		// The caller's task settles before the node forgets the path, so the release is awaited instead of assumed.
		await TestPolling
			.UntilAsync(
				() =>
				{
					GC.Collect();
					GC.WaitForPendingFinalizers();

					return !path.IsAlive;
				},

				TestPolling.DefaultTimeout,
				"The scheduler must not retain the path of a settled update."
			)
			.ConfigureAwait(false);

		// The scheduler stays reachable during the collections, so the wait cannot succeed because the whole scheduler was collected.
		GC.KeepAlive(scheduler);
	}

	[TestMethod]
	public async Task CancelLatestUpdate_WithAnEquivalentPath_SkipsThePendingUpdate()
	{
		var scheduler = new DocumentOperationScheduler();
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		string canonicalFilePath = TestPaths.Script("test.ext");
		string aliasedFilePath = Path.Combine(TestPaths.Script("."), "test.ext");

		int pendingRan = 0;

		Task firstTask = scheduler.EnqueueLatestUpdateAsync(
			canonicalFilePath,

			async _ =>
			{
				firstStarted.TrySetResult(true);
				await allowFirstToFinish.Task.ConfigureAwait(false);
			}
		);

		await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		Task pendingTask = scheduler.EnqueueLatestUpdateAsync(
			canonicalFilePath,

			_ =>
			{
				Interlocked.Increment(ref pendingRan);
				return Task.CompletedTask;
			}
		);

		scheduler.CancelLatestUpdate(aliasedFilePath);
		allowFirstToFinish.TrySetResult(true);

		await firstTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);
		await pendingTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(0, pendingRan);
	}

	[TestMethod]
	public async Task CancelLatestUpdate_SkipsPendingUpdateWithoutRunningItsDelegate()
	{
		var scheduler = new DocumentOperationScheduler();
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int pendingRan = 0;

		Task firstTask = scheduler.EnqueueLatestUpdateAsync(
			TestPaths.Script("test.ext"),

			async _ =>
			{
				firstStarted.TrySetResult(true);
				await allowFirstToFinish.Task.ConfigureAwait(false);
			}
		);

		await firstStarted.Task.ConfigureAwait(false);

		Task pendingTask = scheduler.EnqueueLatestUpdateAsync(
			TestPaths.Script("test.ext"),

			_ =>
			{
				Interlocked.Increment(ref pendingRan);
				return Task.CompletedTask;
			}
		);

		scheduler.CancelLatestUpdate(TestPaths.Script("test.ext"));
		allowFirstToFinish.TrySetResult(true);

		await firstTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);
		await pendingTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(0, pendingRan);
	}

	[TestMethod]
	public async Task CancelAllLatestUpdates_SkipsEveryPendingUpdate()
	{
		var scheduler = new DocumentOperationScheduler();
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowSecondToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int pendingRan = 0;

		Task runningFirst = scheduler.EnqueueLatestUpdateAsync(
			TestPaths.Script("first.ext"),

			async _ =>
			{
				firstStarted.TrySetResult(true);
				await allowFirstToFinish.Task.ConfigureAwait(false);
			}
		);

		Task runningSecond = scheduler.EnqueueLatestUpdateAsync(
			TestPaths.Script("second.ext"),

			async _ =>
			{
				secondStarted.TrySetResult(true);
				await allowSecondToFinish.Task.ConfigureAwait(false);
			}
		);

		await Task.WhenAll(firstStarted.Task, secondStarted.Task)
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Task pendingFirst = scheduler.EnqueueLatestUpdateAsync(
			TestPaths.Script("first.ext"),

			_ =>
			{
				Interlocked.Increment(ref pendingRan);
				return Task.CompletedTask;
			}
		);

		Task pendingSecond = scheduler.EnqueueLatestUpdateAsync(
			TestPaths.Script("second.ext"),

			_ =>
			{
				Interlocked.Increment(ref pendingRan);
				return Task.CompletedTask;
			}
		);

		scheduler.CancelAllLatestUpdates();

		allowFirstToFinish.TrySetResult(true);
		allowSecondToFinish.TrySetResult(true);

		await Task.WhenAll(runningFirst, runningSecond).WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);
		await Task.WhenAll(pendingFirst, pendingSecond).WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(0, pendingRan);
	}

	[TestMethod]
	public async Task CancelLatestUpdate_WhenRunningUpdateWasSuperseded_StopsTheRunningDelegate()
	{
		var scheduler = new DocumentOperationScheduler();
		var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondQueued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int firstCancellationObserved = 0;

		Task firstTask = scheduler.EnqueueLatestUpdateAsync(
			TestPaths.Script("test.ext"),

			async cancellationToken =>
			{
				firstStarted.TrySetResult(true);
				await secondQueued.Task.ConfigureAwait(false);

				try
				{
					await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					Interlocked.Increment(ref firstCancellationObserved);
					throw;
				}
			}
		);

		await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		Task secondTask = scheduler.EnqueueLatestUpdateAsync(TestPaths.Script("test.ext"), _ => Task.CompletedTask);
		secondQueued.TrySetResult(true);

		// The enqueue superseded the running update in the slot; the cancel must still reach it instead of only
		// skipping the newer pending update.
		scheduler.CancelLatestUpdate(TestPaths.Script("test.ext"));

		await Assert
			.ThrowsExactlyAsync<TaskCanceledException>(async () =>
				await firstTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false)
			)
			.ConfigureAwait(false);

		await secondTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(1, firstCancellationObserved);
	}

	[TestMethod]
	public async Task RunDetachedFromActiveContext_WorkStartedInsideAnOperation_DoesNotInheritTheReentrancyScope()
	{
		var scheduler = new DocumentOperationScheduler();
		var detachedEnqueueCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<bool> outerTask = scheduler.EnqueuePerDocumentAsync(
			TestPaths.Script("first.ext"),

			async _ =>
			{
				// Start detached work through the helper; it enqueues while this operation is still running. Without the
				// detach, the enqueue would inherit this slot's re-entrancy scope and throw instead of completing.
				scheduler.RunDetachedFromActiveContext(() => StartDetachedEnqueue(scheduler, detachedEnqueueCompleted));

				Assert.IsTrue(
					await detachedEnqueueCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false)
				);

				return true;
			},

			CancellationToken.None
		);

		Assert.IsTrue(await outerTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task ReentrancyGuard_WorkStartedFromInsideAnOperationWithoutDetaching_FailsFast()
	{
		var scheduler = new DocumentOperationScheduler();
		var detachedEnqueueOutcome = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<bool> outerTask = scheduler.EnqueuePerDocumentAsync(
			TestPaths.Script("first.ext"),

			async operationToken =>
			{
				// A queued task captures this operation's flow, so its enqueue must be rejected while the operation runs;
				// the operation stays alive until the detached task reports, so the rejection is deterministic.
				_ = Task.Run(
					async () =>
					{
						try
						{
							await scheduler
								.EnqueuePerDocumentAsync(TestPaths.Script("second.ext"), _ => Task.FromResult(true), CancellationToken.None)
								.ConfigureAwait(false);

							detachedEnqueueOutcome.TrySetResult(null);
						}
						catch (Exception exception)
						{
							detachedEnqueueOutcome.TrySetResult(exception);
						}
					},

					TestContext.CancellationToken
				);

				Exception? failure = await detachedEnqueueOutcome
					.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
					.ConfigureAwait(false);

				Assert.IsInstanceOfType(failure, typeof(InvalidOperationException));

				return true;
			},

			CancellationToken.None
		);

		Assert.IsTrue(await outerTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
	}

	private static void StartDetachedEnqueue(DocumentOperationScheduler scheduler, TaskCompletionSource<bool> completed)
	{
		_ = Task.Run(async () =>
		{
			try
			{
				bool ran = await scheduler
					.EnqueuePerDocumentAsync(TestPaths.Script("second.ext"), _ => Task.FromResult(true), CancellationToken.None)
					.ConfigureAwait(false);

				completed.TrySetResult(ran);
			}
			catch (Exception exception)
			{
				completed.TrySetException(exception);
			}
		});
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static (Task Update, WeakReference Path) EnqueueUpdateForAFreshPath(DocumentOperationScheduler scheduler)
	{
		string path = TestPaths.Script("fresh-" + Guid.NewGuid().ToString("N") + ".ext");
		return (scheduler.EnqueueLatestUpdateAsync(path, _ => Task.CompletedTask), new WeakReference(path));
	}

	public TestContext? TestContext { get; set; }
}
