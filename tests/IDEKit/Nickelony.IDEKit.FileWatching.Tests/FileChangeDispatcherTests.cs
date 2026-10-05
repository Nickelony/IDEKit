namespace Nickelony.IDEKit.FileWatching.Tests;

/// <summary>Covers how drained workspace changes are dispatched, retried with backoff, and dropped after the last attempt.</summary>
[TestClass]
public sealed class FileChangeDispatcherTests
{
	private static readonly TimeSpan s_retryBase = TimeSpan.FromMilliseconds(250);

	private static readonly string s_path = Path.Combine(Path.GetTempPath(), "dispatcher-tests", "a.ext");
	private static readonly string s_otherPath = Path.Combine(Path.GetTempPath(), "dispatcher-tests", "b.ext");

	private sealed class TestHost : IDisposable
	{
		private readonly object _syncRoot = new();
		private readonly List<FileChangeBatch> _batches = [];
		private readonly List<(Exception Exception, int Failures, int Dropped)> _exhausted = [];
		private readonly CancellationTokenSource _lifetime = new();

		private int _dispatchCount;
		private volatile bool _isDisposed;

		internal TestHost()
		{
			Debouncer = new FileChangeDebouncer(
				debounceDelay: s_retryBase,
				maxDispatchDelay: TimeSpan.FromSeconds(30),
				dispatchPendingChanges: () => _ = Dispatcher.DispatchPendingAsync(),
				timeProvider: Time
			);

			Dispatcher = new FileChangeDispatcher(
				Debouncer,
				DispatchAsync,
				isDisposed: () => _isDisposed,
				retryBaseDelay: s_retryBase,
				OnRetriesExhausted,
				workspaceRootDirectoryPath: "workspace",
				Logger,
				lifetimeToken: _lifetime.Token
			);
		}

		internal FakeTimeProvider Time { get; } = new();

		internal RecordingLogger Logger { get; } = new();

		internal FileChangeDebouncer Debouncer { get; }

		internal FileChangeDispatcher Dispatcher { get; }

		/// <summary>What the next dispatch does, given the 1-based number of the call; the default succeeds.</summary>
		internal Func<int, CancellationToken, Task> Behavior { get; set; } = static (_, _) => Task.CompletedTask;

		internal int DispatchCount => Volatile.Read(ref _dispatchCount);

		internal CancellationToken LifetimeToken => _lifetime.Token;

		internal IReadOnlyList<FileChangeBatch> Batches
		{
			get
			{
				lock (_syncRoot)
					return [.. _batches];
			}
		}

		internal IReadOnlyList<(Exception Exception, int Failures, int Dropped)> Exhausted
		{
			get
			{
				lock (_syncRoot)
					return [.. _exhausted];
			}
		}

		internal void Queue(string path = "", FileChangeKind kind = FileChangeKind.Changed) =>
			Debouncer.Queue(string.IsNullOrEmpty(path) ? s_path : path, kind);

		internal void MarkDisposed() => _isDisposed = true;

		internal void CancelLifetime() => _lifetime.Cancel();

		public void Dispose()
		{
			Debouncer.Dispose();
			Logger.Dispose();
			_lifetime.Dispose();
		}

		private Task DispatchAsync(FileChangeBatch batch, CancellationToken cancellationToken)
		{
			int call = Interlocked.Increment(ref _dispatchCount);

			lock (_syncRoot)
				_batches.Add(batch);

			return Behavior(call, cancellationToken);
		}

		private void OnRetriesExhausted(Exception exception, int failures, int dropped)
		{
			lock (_syncRoot)
				_exhausted.Add((exception, failures, dropped));
		}
	}

	private static Func<int, CancellationToken, Task> FailFirst(int failures) =>
		(call, _) => call <= failures ? Task.FromException(new IOException($"failure {call}")) : Task.CompletedTask;

	[TestMethod]
	public async Task DispatchPendingAsync_DeliversTheDrainedBatchWithTheLifetimeToken()
	{
		using var fixture = new TestHost();

		CancellationToken received = default;

		fixture.Behavior = (_, token) =>
		{
			received = token;
			return Task.CompletedTask;
		};

		fixture.Queue(kind: FileChangeKind.Created);
		fixture.Queue(s_otherPath);

		await fixture.Dispatcher.DispatchPendingAsync().WaitAsync(TestPolling.DefaultTimeout);

		FileChangeBatch batch = Assert.ContainsSingle(fixture.Batches);

		Assert.AreEqual(2, batch.Count);
		Assert.AreEqual(fixture.LifetimeToken, received);
		Assert.IsTrue(fixture.Debouncer.IsEmpty);
	}

	[TestMethod]
	public async Task DispatchPendingAsync_WithNothingPending_DeliversNothing()
	{
		using var fixture = new TestHost();

		await fixture.Dispatcher.DispatchPendingAsync().WaitAsync(TestPolling.DefaultTimeout);

		Assert.AreEqual(0, fixture.DispatchCount);
	}

	[TestMethod]
	public async Task DispatchPendingAsync_AfterDisposalStarted_DeliversNothing()
	{
		using var fixture = new TestHost();

		fixture.Queue();
		fixture.MarkDisposed();

		await fixture.Dispatcher.DispatchPendingAsync().WaitAsync(TestPolling.DefaultTimeout);

		Assert.AreEqual(0, fixture.DispatchCount);
	}

	[TestMethod]
	public async Task DispatchPendingAsync_WhenTheDispatchFails_RequeuesTheBatchForARetryAfterTheBaseDelay()
	{
		using var fixture = new TestHost();

		fixture.Behavior = FailFirst(1);
		fixture.Queue();

		await fixture.Dispatcher.DispatchPendingAsync().WaitAsync(TestPolling.DefaultTimeout);

		Assert.AreEqual(1, fixture.DispatchCount);
		Assert.IsFalse(fixture.Debouncer.IsEmpty);

		fixture.Time.Advance(s_retryBase - TimeSpan.FromMilliseconds(1));

		Assert.AreEqual(1, fixture.DispatchCount);

		fixture.Time.Advance(TimeSpan.FromMilliseconds(2));

		// The retry delivers the same change that failed.
		Assert.AreEqual(2, fixture.DispatchCount);
		Assert.AreEqual(fixture.Batches[0].Entries[0], fixture.Batches[1].Entries[0]);
		Assert.IsTrue(fixture.Debouncer.IsEmpty);
		Assert.IsEmpty(fixture.Exhausted);
	}

	[TestMethod]
	public async Task DispatchPendingAsync_WhenTheRetryFailsToo_DoublesTheDelay()
	{
		using var fixture = new TestHost();

		fixture.Behavior = FailFirst(2);
		fixture.Queue();

		await fixture.Dispatcher.DispatchPendingAsync().WaitAsync(TestPolling.DefaultTimeout);

		fixture.Time.Advance(s_retryBase + TimeSpan.FromMilliseconds(1));

		Assert.AreEqual(2, fixture.DispatchCount);

		// The second failure waits two base delays, so one base delay later nothing has run yet.
		fixture.Time.Advance(s_retryBase);

		Assert.AreEqual(2, fixture.DispatchCount);

		fixture.Time.Advance(s_retryBase + TimeSpan.FromMilliseconds(1));

		Assert.AreEqual(3, fixture.DispatchCount);
		Assert.Contains(static log => log.Contains("retrying in 250 ms", StringComparison.Ordinal), fixture.Logger.Logs);
		Assert.Contains(static log => log.Contains("retrying in 500 ms", StringComparison.Ordinal), fixture.Logger.Logs);
	}

	[TestMethod]
	public async Task DispatchPendingAsync_AfterTheLastAttemptFails_DropsTheBatchAndTellsTheOwnerOnce()
	{
		using var fixture = new TestHost();

		fixture.Behavior = FailFirst(int.MaxValue);

		fixture.Queue();
		fixture.Queue(s_otherPath);

		await fixture.Dispatcher.DispatchPendingAsync().WaitAsync(TestPolling.DefaultTimeout);

		fixture.Time.Advance(s_retryBase + TimeSpan.FromMilliseconds(1));
		fixture.Time.Advance((s_retryBase * 2) + TimeSpan.FromMilliseconds(1));

		Assert.AreEqual(3, fixture.DispatchCount);

		(Exception exception, int failures, int dropped) = Assert.ContainsSingle(fixture.Exhausted);

		Assert.AreEqual("failure 3", exception.Message);
		Assert.AreEqual(3, failures);
		Assert.AreEqual(2, dropped);

		// Nothing is retried after the owner was told.
		fixture.Time.Advance(TimeSpan.FromMinutes(1));

		Assert.AreEqual(3, fixture.DispatchCount);
		Assert.IsTrue(fixture.Debouncer.IsEmpty);
	}

	[TestMethod]
	public async Task DispatchPendingAsync_AfterASuccessfulDispatch_StartsTheFailureCountAgain()
	{
		using var fixture = new TestHost();

		fixture.Behavior = static (call, _) => call is 1 or 3 or 4 ? Task.FromException(new IOException($"failure {call}")) : Task.CompletedTask;

		// Failure 1, then a success on the retry resets the count, so failures 3 and 4 are only the first two of a new run.
		fixture.Queue();

		await fixture.Dispatcher.DispatchPendingAsync().WaitAsync(TestPolling.DefaultTimeout);

		fixture.Time.Advance(s_retryBase + TimeSpan.FromMilliseconds(1));

		Assert.AreEqual(2, fixture.DispatchCount);

		fixture.Queue();

		await fixture.Dispatcher.DispatchPendingAsync().WaitAsync(TestPolling.DefaultTimeout);

		fixture.Time.Advance(s_retryBase + TimeSpan.FromMilliseconds(1));

		Assert.AreEqual(4, fixture.DispatchCount);
		Assert.IsEmpty(fixture.Exhausted);
	}

	[TestMethod]
	public async Task ResetFailureCount_GivesTheNextBatchAFullSetOfAttempts()
	{
		using var fixture = new TestHost();

		fixture.Behavior = FailFirst(int.MaxValue);
		fixture.Queue();

		await fixture.Dispatcher.DispatchPendingAsync().WaitAsync(TestPolling.DefaultTimeout);

		fixture.Time.Advance(s_retryBase + TimeSpan.FromMilliseconds(1));

		Assert.AreEqual(2, fixture.DispatchCount);

		fixture.Dispatcher.ResetFailureCount();

		fixture.Time.Advance((s_retryBase * 2) + TimeSpan.FromMilliseconds(1));

		// The count began again at the reset, so the third call is the first failure of a new run, not the exhausting one.
		Assert.AreEqual(3, fixture.DispatchCount);
		Assert.IsEmpty(fixture.Exhausted);
	}

	[TestMethod]
	public async Task DispatchPendingAsync_WhenDisposalStartsDuringAFailingDispatch_DropsTheBatchWithoutRetryOrNotice()
	{
		using var fixture = new TestHost();

		fixture.Behavior = (_, _) =>
		{
			fixture.MarkDisposed();
			return Task.FromException(new IOException("The dispatch failed."));
		};

		fixture.Queue();

		await fixture.Dispatcher.DispatchPendingAsync().WaitAsync(TestPolling.DefaultTimeout);

		fixture.Time.Advance(TimeSpan.FromMinutes(1));

		Assert.AreEqual(1, fixture.DispatchCount);
		Assert.IsEmpty(fixture.Exhausted);
		Assert.IsTrue(fixture.Debouncer.IsEmpty);
		Assert.Contains(static log => log.Contains("because the watcher was disposed", StringComparison.Ordinal), fixture.Logger.Logs);
	}

	[TestMethod]
	public async Task DispatchPendingAsync_AfterDisposalStarted_DoesNotWaitForARunningDispatch()
	{
		using var fixture = new TestHost();

		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		fixture.Behavior = async (call, _) =>
		{
			if (call == 1)
			{
				firstEntered.TrySetResult();
				await release.Task.ConfigureAwait(false);
			}
		};

		fixture.Queue();

		Task first = fixture.Dispatcher.DispatchPendingAsync();

		await firstEntered.Task.WaitAsync(TestPolling.DefaultTimeout);

		fixture.MarkDisposed();

		// A disposed owner needs no delivery, so the call returns at once instead of queueing behind the running dispatch.
		await fixture.Dispatcher.DispatchPendingAsync().WaitAsync(TestPolling.DefaultTimeout);

		release.SetResult();

		await first.WaitAsync(TestPolling.DefaultTimeout);

		Assert.AreEqual(1, fixture.DispatchCount);
	}

	[TestMethod]
	public async Task DispatchPendingAsync_WhenDisposalStartsWhileWaitingAtTheGate_DeliversNothing()
	{
		using var fixture = new TestHost();

		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		fixture.Behavior = async (call, _) =>
		{
			if (call == 1)
			{
				firstEntered.TrySetResult();
				await release.Task.ConfigureAwait(false);
			}
		};

		fixture.Queue();

		Task first = fixture.Dispatcher.DispatchPendingAsync();

		await firstEntered.Task.WaitAsync(TestPolling.DefaultTimeout);

		fixture.Queue(s_otherPath);

		Task second = fixture.Dispatcher.DispatchPendingAsync();

		fixture.MarkDisposed();
		release.SetResult();

		await Task.WhenAll(first, second).WaitAsync(TestPolling.DefaultTimeout);

		// The second dispatch passed the first check, then found the owner disposed once it held the gate.
		Assert.AreEqual(1, fixture.DispatchCount);
	}

	[TestMethod]
	public async Task DispatchPendingAsync_RunsOneDispatchAtATime()
	{
		using var fixture = new TestHost();

		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		int running = 0;
		int maxRunning = 0;

		fixture.Behavior = async (call, _) =>
		{
			int now = Interlocked.Increment(ref running);

			maxRunning = Math.Max(maxRunning, now);

			if (call == 1)
			{
				firstEntered.TrySetResult();
				await release.Task.ConfigureAwait(false);
			}

			Interlocked.Decrement(ref running);
		};

		fixture.Queue();

		Task first = fixture.Dispatcher.DispatchPendingAsync();

		await firstEntered.Task.WaitAsync(TestPolling.DefaultTimeout);

		fixture.Queue(s_otherPath);

		Task second = fixture.Dispatcher.DispatchPendingAsync();

		await Task.Delay(TestPolling.AbsenceWindow);

		// The second dispatch waits at the gate, so it delivered nothing while the first one runs.
		Assert.AreEqual(1, fixture.DispatchCount);

		release.SetResult();

		await Task.WhenAll(first, second).WaitAsync(TestPolling.DefaultTimeout);

		Assert.AreEqual(2, fixture.DispatchCount);
		Assert.AreEqual(1, maxRunning);
	}

	[TestMethod]
	public async Task DispatchPendingAsync_WhenTheLifetimeEndsWhileWaitingAtTheGate_GivesUpWithoutDispatching()
	{
		using var fixture = new TestHost();

		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		fixture.Behavior = async (call, _) =>
		{
			if (call == 1)
			{
				firstEntered.TrySetResult();
				await release.Task.ConfigureAwait(false);
			}
		};

		fixture.Queue();

		Task first = fixture.Dispatcher.DispatchPendingAsync();

		await firstEntered.Task.WaitAsync(TestPolling.DefaultTimeout);

		fixture.Queue(s_otherPath);

		Task second = fixture.Dispatcher.DispatchPendingAsync();

		fixture.CancelLifetime();

		await second.WaitAsync(TestPolling.DefaultTimeout);

		release.SetResult();

		await first.WaitAsync(TestPolling.DefaultTimeout);

		Assert.AreEqual(1, fixture.DispatchCount);

		Assert.Contains(
			static log => log.Contains("was interrupted before any pending change was drained", StringComparison.Ordinal),
			fixture.Logger.Logs
		);
	}
}
