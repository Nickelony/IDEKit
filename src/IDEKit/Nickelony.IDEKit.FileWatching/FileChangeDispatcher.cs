using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.FileWatching;

/// <summary>Drains the debounced file changes into batches and dispatches them, retrying a failed batch with bounded backoff.</summary>
/// <remarks>
/// <para>
/// One dispatch runs at a time. A failed batch is requeued at the front of the debouncer with an exponential delay
/// that starts at the retry base delay; when the attempt limit is reached the batch is dropped and the owner is told, so it can replace the watcher
/// that feeds this dispatcher. A dispatch that completes resets the failure count.
/// </para>
/// <para>
/// Once disposal began, nothing is delivered: a pending dispatch is skipped, and a drained batch whose dispatch
/// fails or observes cancellation is dropped. A dispatch callback that completes normally has already delivered its batch, even when disposal began
/// while it was in flight. The dispatch gate is never disposed, because a dispatch that is still waiting on it when disposal begins must not fault
/// with an <see cref="ObjectDisposedException"/>.
/// </para>
/// </remarks>
[SuppressMessage(
	"Design",
	"CA1001:Types that own disposable fields should be disposable",
	Justification = "The dispatch gate is never disposed on purpose; see the remarks."
)]
internal sealed class FileChangeDispatcher
{
	// Bounded dispatch retry policy: a failed dispatch is requeued with exponential backoff started at the retry base delay; when the
	// attempt limit is reached the owner is told once and the batch is dropped.
	private const int DispatchFailureMaxAttempts = 3;

	private readonly FileChangeDebouncer _debouncer;
	private readonly Func<FileChangeBatch, CancellationToken, Task> _dispatchAsync;

	private readonly Func<bool> _isDisposed;
	private readonly TimeSpan _retryBaseDelay;
	private readonly Action<Exception, int, int> _onRetriesExhausted;

	private readonly string _workspaceRootDirectoryPath;
	private readonly ILogger _logger;

	// The token of the owner's lifetime: disposal cancels it so a dispatch that waits for the gate or runs the callback can stop.
	private readonly CancellationToken _lifetimeToken;

	private readonly SemaphoreSlim _dispatchGate = new(1, 1);

	private int _consecutiveDispatchFailures;

	/// <param name="debouncer">The debouncer whose pending changes are drained, and which schedules the retries.</param>
	/// <param name="dispatchAsync">The callback that delivers one drained batch to the owner.</param>
	/// <param name="isDisposed">Reports whether the owner is disposed.</param>
	/// <param name="retryBaseDelay">The delay before the first retry; each further retry doubles it.</param>
	/// <param name="onRetriesExhausted">
	/// Invoked with the last failure, the number of consecutive failures and the number of dropped changes when a batch used up its attempts.
	/// </param>
	/// <param name="workspaceRootDirectoryPath">The workspace root that the log text names.</param>
	/// <param name="logger">The logger instance.</param>
	/// <param name="lifetimeToken">The token that disposal of the owner cancels.</param>
	internal FileChangeDispatcher(
		FileChangeDebouncer debouncer,
		Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
		Func<bool> isDisposed,
		TimeSpan retryBaseDelay,
		Action<Exception, int, int> onRetriesExhausted,
		string workspaceRootDirectoryPath,
		ILogger logger,
		CancellationToken lifetimeToken
	)
	{
		_debouncer = debouncer;
		_dispatchAsync = dispatchAsync;
		_isDisposed = isDisposed;
		_retryBaseDelay = retryBaseDelay;
		_onRetriesExhausted = onRetriesExhausted;
		_workspaceRootDirectoryPath = workspaceRootDirectoryPath;
		_logger = logger;
		_lifetimeToken = lifetimeToken;
	}

	/// <summary>Dispatches any currently pending changes.</summary>
	/// <remarks>
	/// Changes are not delivered once disposal began: the dispatch is skipped when the owner is already disposed, and a drained batch whose dispatch
	/// fails or observes cancellation after disposal started is dropped. A dispatch callback that completes normally has already delivered its batch,
	/// even when disposal began while the dispatch was in flight.
	/// </remarks>
	internal async Task DispatchPendingAsync()
	{
		if (_isDisposed())
			return;

		bool dispatchGateHeld = false;
		FileChangeBatch? batch = null;

		try
		{
			await _dispatchGate.WaitAsync(_lifetimeToken).ConfigureAwait(false);
			dispatchGateHeld = true;

			// Disposal drops anything that is still buffered: the debounce buffer and scheduled retries do not
			// survive the watcher, and the owning supervisor reconciles missed changes through its tracked snapshot.
			if (_isDisposed() || _debouncer.IsEmpty)
				return;

			batch = _debouncer.DrainBatch();

			if (batch.Count == 0)
				return;

			await _dispatchAsync(batch, _lifetimeToken).ConfigureAwait(false);
			Interlocked.Exchange(ref _consecutiveDispatchFailures, 0);
		}
		catch (Exception exception)
		{
			if (batch is null)
			{
				_logger.LogDebug(
					exception,
					"Workspace file watcher dispatch for '{Workspace}' was interrupted before any pending change was drained.",
					_workspaceRootDirectoryPath
				);

				return;
			}

			// Disposal cancels the lifetime token and drops a batch that was already drained; that is expected
			// teardown, not a dispatch failure.
			if (_isDisposed())
			{
				_logger.LogDebug(
					exception,
					"Workspace file watcher dropped {Count} queued change(s) for '{Workspace}' because the watcher was disposed.",
					batch.Count,
					_workspaceRootDirectoryPath
				);

				return;
			}

			int consecutiveDispatchFailures = Interlocked.Increment(ref _consecutiveDispatchFailures);

			if (consecutiveDispatchFailures < DispatchFailureMaxAttempts)
			{
				TimeSpan retryDelay = GetDispatchRetryDelay(consecutiveDispatchFailures);

				_logger.LogDebug(
					exception,
					"Workspace file watcher dispatch failed for '{Workspace}' with {Count} queued change(s) {FailureCount} times in a row; retrying in {RetryDelayMs} ms.",
					_workspaceRootDirectoryPath,
					batch.Count,
					consecutiveDispatchFailures,
					(int)retryDelay.TotalMilliseconds
				);

				_debouncer.Requeue(batch, retryDelay);
			}
			else
			{
				// Delivery does not wait on an owner reaction: the batch is dropped and the owner decides what to stop.
				_onRetriesExhausted(exception, consecutiveDispatchFailures, batch.Count);
			}
		}
		finally
		{
			if (dispatchGateHeld)
				_dispatchGate.Release();
		}
	}

	/// <summary>Forgets the failures of the previous run, so a restarted watcher begins with a full set of attempts.</summary>
	internal void ResetFailureCount() => Interlocked.Exchange(ref _consecutiveDispatchFailures, 0);

	/// <summary>Returns the bounded exponential retry delay for the supplied consecutive failure count.</summary>
	/// <param name="consecutiveDispatchFailures">The number of consecutive dispatch failures.</param>
	private TimeSpan GetDispatchRetryDelay(int consecutiveDispatchFailures)
	{
		// Bounded attempts keep the shift small: the first retry waits the base delay, the second waits two base delays.
		int exponentialShift = consecutiveDispatchFailures - 1;
		double retryDelayMilliseconds = _retryBaseDelay.TotalMilliseconds * (1 << exponentialShift);

		return TimeSpan.FromMilliseconds(retryDelayMilliseconds);
	}
}
