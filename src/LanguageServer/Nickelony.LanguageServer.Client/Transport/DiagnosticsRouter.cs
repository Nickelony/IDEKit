using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Routes diagnostics payloads and refresh requests (semantic tokens and pull diagnostics) from transport callbacks
/// to <see cref="LanguageServerClient"/> subscribers through serialized background pumps.
/// </summary>
/// <remarks>
/// Diagnostics are stored as the latest payload per document identity, refresh requests are coalesced, and both are
/// delivered by serialized observed background pumps whose unexpected termination marks the transport unhealthy
/// rather than recreating locally. The routing scale and the recovery model are documented in
/// <c>docs/LanguageServerInternals.md</c>.
/// </remarks>
internal sealed class DiagnosticsRouter
{
	/// <summary>
	/// Stores one queued diagnostics payload together with the transport generation that produced it.
	/// </summary>
	/// <param name="TransportGeneration">The transport generation that published the diagnostics.</param>
	/// <param name="Parameters">The diagnostics payload.</param>
	private readonly record struct QueuedDiagnostics(long TransportGeneration, PublishDiagnosticsParams Parameters);

	/// <summary>
	/// Identifies one coalesced diagnostics queue slot.
	/// </summary>
	/// <param name="TransportGeneration">The transport generation that produced the diagnostics.</param>
	/// <param name="DocumentKey">The document identity used to coalesce the payload.</param>
	private readonly record struct DiagnosticsQueueKey(long TransportGeneration, DiagnosticsDocumentKey DocumentKey);

	/// <summary>
	/// Compares diagnostics queue keys by transport generation and document identity.
	/// </summary>
	private sealed class DiagnosticsQueueKeyComparer : IEqualityComparer<DiagnosticsQueueKey>
	{
		/// <summary>
		/// The comparer instance shared by the pending-diagnostics dictionary.
		/// </summary>
		public static readonly DiagnosticsQueueKeyComparer Instance = new();

		/// <inheritdoc/>
		public bool Equals(DiagnosticsQueueKey left, DiagnosticsQueueKey right) =>
			left.TransportGeneration == right.TransportGeneration
			&& DiagnosticsDocumentKey.Comparer.Equals(left.DocumentKey, right.DocumentKey);

		/// <inheritdoc/>
		public int GetHashCode(DiagnosticsQueueKey key) =>
			HashCode.Combine(key.TransportGeneration, DiagnosticsDocumentKey.Comparer.GetHashCode(key.DocumentKey));
	}

	private readonly ILogger _logger;
	private readonly object _eventSender;

	private readonly Func<long, bool> _canAcceptServerCallbacksForGeneration;
	private readonly Func<bool> _isDisposed;
	private readonly CancellationToken _lifetimeToken;
	private readonly Action _markTransportUnhealthy;

	// Queued diagnostics state. Both dictionaries share the document-key comparer: a file URI coalesces on its
	// normalized local path with the platform's local-path identity - so a case-only path difference cannot produce
	// duplicate coalescing slots on a case-insensitive host - while any other URI compares ordinally.
	private readonly ConcurrentDictionary<DiagnosticsQueueKey, QueuedDiagnostics> _pendingDiagnostics = new(DiagnosticsQueueKeyComparer.Instance);
	private readonly ConcurrentDictionary<DiagnosticsDocumentKey, QueuedDiagnostics> _pendingCallbackDiagnostics = new(DiagnosticsDocumentKey.Comparer);

	private readonly Channel<bool> _diagnosticsSignal = Channel.CreateBounded<bool>(
		new BoundedChannelOptions(1)
		{
			SingleReader = true,
			SingleWriter = false,
			AllowSynchronousContinuations = false,
			FullMode = BoundedChannelFullMode.DropWrite
		});

	// Background callback pump state.
	private Task _diagnosticsPumpTask = Task.CompletedTask;
	private int _pendingSemanticTokensRefresh;
	private int _pendingDiagnosticRefresh;

	// Set once CompleteSignalChannels runs, so a raise that arrives after teardown started is dropped instead of
	// being queued into a pipeline whose pumps are completing and will never deliver it.
	private int _signalsCompleted;

	private readonly Channel<bool> _callbackSignal = Channel.CreateBounded<bool>(
		new BoundedChannelOptions(1)
		{
			SingleReader = true,
			SingleWriter = false,
			AllowSynchronousContinuations = false,
			FullMode = BoundedChannelFullMode.DropWrite
		});

	private Task _callbackPumpTask = Task.CompletedTask;
	private long _diagnosticsFallbackSequence;

	private readonly SerializedDiagnosticsSubscriberSet<EventHandler<DiagnosticsPublishedEventArgs>> _diagnosticsPublishedSubscribers;
	private readonly SerializedSignalSubscriberSet<EventHandler> _semanticTokensRefreshSubscribers;
	private readonly SerializedSignalSubscriberSet<EventHandler> _diagnosticRefreshSubscribers;

	// Observed background loop state.
	private readonly object _observedBackgroundLoopSyncRoot = new();
	private readonly object _backgroundLoopSyncRoot = new();
	private readonly HashSet<Task> _observedBackgroundLoopTerminations = [];

	// Quiescence signal for WaitForDrainAsync: a waiter parks on the current task source and every step that
	// advances the pipeline toward (or past) the drained state completes it, so the wait is event-driven instead
	// of a timed poll. The field is null while no waiter is parked, so the hot delivery paths only pay a null check.
	private readonly object _drainSignalSyncRoot = new();
	private TaskCompletionSource? _drainSignal;

	// Quiescence observation: the number of pump iterations currently in flight. A pump iteration removes a payload
	// from its source queue before enqueueing it into the next stage, so an observer that required only empty queues
	// could mistake that gap for a drained pipeline; counting the in-flight iterations closes it.
	private int _activePumpIterations;

	/// <summary>
	/// Initializes a new instance of the <see cref="DiagnosticsRouter"/> class.
	/// </summary>
	/// <param name="logger">The logger used for subscriber and background-loop diagnostics.</param>
	/// <param name="eventSender">The sender reported to diagnostics and semantic-token refresh subscribers.</param>
	/// <param name="canAcceptServerCallbacksForGeneration">
	/// Reports whether one transport generation may currently publish server callbacks; queued payloads that no
	/// longer qualify are dropped instead of delivered.
	/// </param>
	/// <param name="isDisposed">Reports whether the owning client started disposal.</param>
	/// <param name="markTransportUnhealthy">Marks the owning client transport unhealthy when a tracked pump terminates unexpectedly.</param>
	/// <param name="testHooks">The test-only hooks threaded to the diagnostics pump, or <see cref="ClientTestHooks.None"/> for production.</param>
	/// <param name="lifetimeToken">Signals client disposal to the background pumps.</param>
	internal DiagnosticsRouter(
		ILogger logger,
		object eventSender,
		Func<long, bool> canAcceptServerCallbacksForGeneration,
		Func<bool> isDisposed,
		Action markTransportUnhealthy,
		ClientTestHooks testHooks,
		CancellationToken lifetimeToken)
	{
		_logger = logger;
		_eventSender = eventSender;
		_canAcceptServerCallbacksForGeneration = canAcceptServerCallbacksForGeneration;
		_isDisposed = isDisposed;
		_lifetimeToken = lifetimeToken;
		_markTransportUnhealthy = markTransportUnhealthy;

		_diagnosticsPublishedSubscribers = new(
			(handler, parameters) => handler(eventSender, new DiagnosticsPublishedEventArgs(parameters)),
			exception => _logger.LogWarning(exception, "Diagnostics handler threw; later subscribers will still be notified."),
			SignalDrainProgress,
			testHooks);

		_semanticTokensRefreshSubscribers = new(
			handler => handler(_eventSender, EventArgs.Empty),
			exception => _logger.LogWarning(exception, "Semantic tokens refresh request handler threw; later subscribers will still be notified."),
			SignalDrainProgress);

		_diagnosticRefreshSubscribers = new(
			handler => handler(_eventSender, EventArgs.Empty),
			exception => _logger.LogWarning(exception, "Diagnostic refresh request handler threw; later subscribers will still be notified."),
			SignalDrainProgress);
	}

	/// <summary>
	/// Gets or replaces the tracked background callback pump task.
	/// </summary>
	internal Task CallbackPumpTask
	{
		get => Volatile.Read(ref _callbackPumpTask);
		set => Volatile.Write(ref _callbackPumpTask, value);
	}

	/// <summary>
	/// Gets or replaces the tracked background diagnostics pump task.
	/// </summary>
	internal Task DiagnosticsPumpTask
	{
		get => Volatile.Read(ref _diagnosticsPumpTask);
		set => Volatile.Write(ref _diagnosticsPumpTask, value);
	}

	/// <summary>
	/// Adds one subscriber to the serialized diagnostics-dispatch set.
	/// </summary>
	/// <param name="handler">The subscriber to add.</param>
	internal void AddDiagnosticsSubscriber(EventHandler<DiagnosticsPublishedEventArgs>? handler)
		=> _diagnosticsPublishedSubscribers.Add(handler);

	/// <summary>
	/// Removes one subscriber from the serialized diagnostics-dispatch set.
	/// </summary>
	/// <param name="handler">The subscriber to remove.</param>
	internal void RemoveDiagnosticsSubscriber(EventHandler<DiagnosticsPublishedEventArgs>? handler)
		=> _diagnosticsPublishedSubscribers.Remove(handler);

	/// <summary>
	/// Adds one subscriber to the serialized semantic-token refresh signal set.
	/// </summary>
	/// <param name="handler">The subscriber to add.</param>
	internal void AddSemanticTokensRefreshSubscriber(EventHandler? handler)
		=> _semanticTokensRefreshSubscribers.Add(handler);

	/// <summary>
	/// Removes one subscriber from the serialized semantic-token refresh signal set.
	/// </summary>
	/// <param name="handler">The subscriber to remove.</param>
	internal void RemoveSemanticTokensRefreshSubscriber(EventHandler? handler)
		=> _semanticTokensRefreshSubscribers.Remove(handler);

	/// <summary>
	/// Adds one subscriber to the serialized diagnostic refresh signal set.
	/// </summary>
	/// <param name="handler">The subscriber to add.</param>
	internal void AddDiagnosticRefreshSubscriber(EventHandler? handler)
		=> _diagnosticRefreshSubscribers.Add(handler);

	/// <summary>
	/// Removes one subscriber from the serialized diagnostic refresh signal set.
	/// </summary>
	/// <param name="handler">The subscriber to remove.</param>
	internal void RemoveDiagnosticRefreshSubscriber(EventHandler? handler)
		=> _diagnosticRefreshSubscribers.Remove(handler);

	/// <summary>
	/// Queues a diagnostics payload for later publication on the diagnostics pump.
	/// </summary>
	/// <param name="transportGeneration">The transport generation that published the diagnostics.</param>
	/// <param name="parameters">The diagnostics payload.</param>
	internal void QueueDiagnosticsPublished(long transportGeneration, PublishDiagnosticsParams parameters)
	{
		// A raise after the signal channels completed has no pump left to drain it; drop it instead of retaining it
		// in a pending dictionary for the remainder of the client's lifetime.
		if (Volatile.Read(ref _signalsCompleted) != 0)
			return;

		// Keep only the newest diagnostics payload per file within one transport generation and wake the pump if it is idle.
		// First snapshot level: detach the queued payload from the caller's arrays so a caller that reuses or mutates its
		// payload after this call cannot change what the pump eventually observes. A second per-subscriber snapshot in
		// SerializedDiagnosticsSubscriberSet.Dispatch keeps subscribers from sharing payload instances with each other.
		// Both snapshots copy the outer diagnostics sequence only: the diagnostic entries and their nested
		// related-information, tag, code and data values stay shared and are read-only (see PublishDiagnosticsParams.CreateSnapshot).
		_pendingDiagnostics[GetDiagnosticsQueueKey(transportGeneration, parameters)] = new QueuedDiagnostics(transportGeneration, parameters.CreateSnapshot());
		_diagnosticsSignal.Writer.TryWrite(true);
	}

	/// <summary>
	/// Publishes queued diagnostics for the current transport generation while coalescing repeated updates.
	/// </summary>
	internal async Task PumpDiagnosticsAsync()
	{
		ChannelReader<bool> reader = _diagnosticsSignal.Reader;

		try
		{
			while (await reader.WaitToReadAsync(_lifetimeToken).ConfigureAwait(false))
			{
				Interlocked.Increment(ref _activePumpIterations);

				try
				{
					while (reader.TryRead(out _))
					{ }

					while (!_pendingDiagnostics.IsEmpty)
					{
						// The drain materializes the whole pending set per iteration. That is intentional at the routing
						// scale (one workspace with a bounded set of tracked documents): the snapshot keeps the iteration
						// race-free against concurrent raise calls and stays cheap for that document count.
						KeyValuePair<DiagnosticsQueueKey, QueuedDiagnostics>[] pendingDiagnostics = [.. _pendingDiagnostics];

						for (int i = 0; i < pendingDiagnostics.Length; i++)
						{
							if (!_pendingDiagnostics.TryRemove(pendingDiagnostics[i].Key, out QueuedDiagnostics queuedDiagnostics)
								|| !ShouldDeliverQueuedCallback(queuedDiagnostics))
							{
								continue;
							}

							QueueDiagnosticsCallback(pendingDiagnostics[i].Key.DocumentKey, queuedDiagnostics);
						}
					}
				}
				finally
				{
					Interlocked.Decrement(ref _activePumpIterations);
					SignalDrainProgress();
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Expected on dispose.
		}
	}

	/// <summary>
	/// Queues a semantic tokens refresh callback for background subscriber dispatch.
	/// </summary>
	internal void QueueSemanticTokensRefreshRequested()
	{
		// A refresh callback queued after the signal channels completed has no pump left to drain it; drop it
		// instead of retaining the pending flag for the remainder of the client's lifetime. The sibling
		// QueueDiagnosticsPublished applies the same guard.
		if (Volatile.Read(ref _signalsCompleted) != 0)
			return;

		Interlocked.Exchange(ref _pendingSemanticTokensRefresh, 1);
		_callbackSignal.Writer.TryWrite(true);
	}

	/// <summary>
	/// Queues a diagnostic refresh callback for background subscriber dispatch.
	/// </summary>
	internal void QueueDiagnosticRefreshRequested()
	{
		// A refresh callback queued after the signal channels completed has no pump left to drain it; drop it
		// instead of retaining the pending flag for the remainder of the client's lifetime. The sibling
		// QueueSemanticTokensRefreshRequested applies the same guard.
		if (Volatile.Read(ref _signalsCompleted) != 0)
			return;

		Interlocked.Exchange(ref _pendingDiagnosticRefresh, 1);
		_callbackSignal.Writer.TryWrite(true);
	}

	/// <summary>
	/// Dispatches queued client callbacks on the background callback pump.
	/// </summary>
	internal async Task PumpCallbacksAsync()
	{
		ChannelReader<bool> reader = _callbackSignal.Reader;

		try
		{
			while (await reader.WaitToReadAsync(_lifetimeToken).ConfigureAwait(false))
			{
				Interlocked.Increment(ref _activePumpIterations);

				try
				{
					while (reader.TryRead(out _))
					{ }

					while (true)
					{
						bool dispatchedCallbacks = false;

						if (Interlocked.Exchange(ref _pendingSemanticTokensRefresh, 0) != 0)
						{
							InvokeSemanticTokensRefreshRequested();
							dispatchedCallbacks = true;
						}

						if (Interlocked.Exchange(ref _pendingDiagnosticRefresh, 0) != 0)
						{
							InvokeDiagnosticRefreshRequested();
							dispatchedCallbacks = true;
						}

						if (!_pendingCallbackDiagnostics.IsEmpty)
						{
							// Same intentional whole-set snapshot as the diagnostics pump; the callback routing scale is one workspace.
							KeyValuePair<DiagnosticsDocumentKey, QueuedDiagnostics>[] pendingDiagnostics = [.. _pendingCallbackDiagnostics];

							for (int i = 0; i < pendingDiagnostics.Length; i++)
							{
								if (!_pendingCallbackDiagnostics.TryRemove(pendingDiagnostics[i].Key, out QueuedDiagnostics queuedDiagnostics)
									|| !ShouldDeliverQueuedCallback(queuedDiagnostics))
								{
									continue;
								}

								InvokeDiagnosticsPublished(pendingDiagnostics[i].Key, queuedDiagnostics.Parameters);
								dispatchedCallbacks = true;
							}
						}

						if (!dispatchedCallbacks
							&& Volatile.Read(ref _pendingSemanticTokensRefresh) == 0
							&& Volatile.Read(ref _pendingDiagnosticRefresh) == 0
							&& _pendingCallbackDiagnostics.IsEmpty)
						{
							break;
						}
					}
				}
				finally
				{
					Interlocked.Decrement(ref _activePumpIterations);
					SignalDrainProgress();
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Expected on dispose.
		}
	}

	/// <summary>
	/// Dispatches one semantic tokens refresh signal to every current subscriber.
	/// </summary>
	internal void InvokeSemanticTokensRefreshRequested()
		=> _semanticTokensRefreshSubscribers.Dispatch();

	/// <summary>
	/// Dispatches one diagnostic refresh signal to every current subscriber.
	/// </summary>
	internal void InvokeDiagnosticRefreshRequested()
		=> _diagnosticRefreshSubscribers.Dispatch();

	/// <summary>
	/// Dispatches one diagnostics payload to every current subscriber.
	/// </summary>
	/// <param name="documentKey">The document identity used to coalesce repeated payloads per subscriber.</param>
	/// <param name="parameters">The diagnostics payload to dispatch.</param>
	internal void InvokeDiagnosticsPublished(DiagnosticsDocumentKey documentKey, PublishDiagnosticsParams parameters)
		=> _diagnosticsPublishedSubscribers.Dispatch(documentKey, parameters);

	/// <summary>
	/// Ensures the callback pump and, optionally, the diagnostics pump are running for the current client instance.
	/// Completed or faulted pumps are recreated so a transport restart can recover callback delivery.
	/// </summary>
	/// <param name="includeDiagnosticsPump">Whether the diagnostics pump should also be ensured.</param>
	internal void EnsurePumpsRunning(bool includeDiagnosticsPump)
	{
		lock (_backgroundLoopSyncRoot)
		{
			if (_callbackPumpTask.IsCompleted)
			{
				ForgetObservedBackgroundLoopTermination(_callbackPumpTask);

				_callbackPumpTask = StartObservedBackgroundLoop(
					PumpCallbacksAsync,
					"callback dispatcher",
					markTransportUnhealthyOnUnexpectedTermination: true);
			}

			if (includeDiagnosticsPump && _diagnosticsPumpTask.IsCompleted)
			{
				ForgetObservedBackgroundLoopTermination(_diagnosticsPumpTask);

				_diagnosticsPumpTask = StartObservedBackgroundLoop(
					PumpDiagnosticsAsync,
					"diagnostics pump",
					markTransportUnhealthyOnUnexpectedTermination: true);
			}
		}
	}

	/// <summary>
	/// Completes both pump wake signals so the background pumps drain and finish during disposal.
	/// </summary>
	internal void CompleteSignalChannels()
	{
		// Mark completion first so a concurrent raise becomes a no-op instead of queueing a payload that this
		// teardown will never deliver; then complete the wake signals and drop whatever is still queued. Without
		// this the pending dictionaries would retain every payload that arrived between completion and detach.
		Interlocked.Exchange(ref _signalsCompleted, 1);
		_callbackSignal.Writer.TryComplete();
		_diagnosticsSignal.Writer.TryComplete();
		_pendingDiagnostics.Clear();
		_pendingCallbackDiagnostics.Clear();

		// Release a parked drain waiter so it observes the completed state instead of blocking.
		SignalDrainProgress();
	}

	/// <summary>
	/// Drops the recorded unexpected-termination markers once disposal has finished observing the pump tasks.
	/// </summary>
	/// <remarks>
	/// The markers de-duplicate the disposal warning for an already-observed loop, so they must survive until the
	/// teardown stages have run; only then can they be released instead of retaining one Task reference per
	/// un-restarted terminated pump for the client's lifetime.
	/// </remarks>
	internal void ClearObservedBackgroundLoopTerminations()
	{
		lock (_observedBackgroundLoopSyncRoot)
			_observedBackgroundLoopTerminations.Clear();
	}

	/// <summary>
	/// Gets a value indicating whether the router has delivered every queued payload: both pump queues are empty, no
	/// semantic-token refresh is pending, no pump is mid-iteration, and every subscriber drain has finished.
	/// </summary>
	internal bool IsDrained =>
		_pendingDiagnostics.IsEmpty
		&& _pendingCallbackDiagnostics.IsEmpty
		&& Volatile.Read(ref _pendingSemanticTokensRefresh) == 0
		&& Volatile.Read(ref _pendingDiagnosticRefresh) == 0
		&& Volatile.Read(ref _activePumpIterations) == 0
		&& _diagnosticsPublishedSubscribers.IsIdle
		&& _semanticTokensRefreshSubscribers.IsIdle
		&& _diagnosticRefreshSubscribers.IsIdle;

	/// <summary>
	/// Waits until every payload queued before this call has been delivered to subscribers, so an absence proof can
	/// assert the settled delivery state instead of waiting out a fixed time window.
	/// </summary>
	/// <remarks>
	/// A test-visible quiescence seam: the client never calls this method. It parks on a completion signal that
	/// every pipeline step raises as it advances toward the drained state, so an observation returns as soon as the
	/// exact drain state is reached rather than after a polling interval.
	/// </remarks>
	/// <returns>A task that completes once the router is drained.</returns>
	internal async Task WaitForDrainAsync()
	{
		while (!IsDrained)
		{
			Task drainSignal = GetOrCreateDrainSignal();

			// Re-check after capturing the signal: a step that advanced the pipeline between the first check and the
			// capture would otherwise leave this wait parked on a signal that has already been raised and cleared.
			if (IsDrained)
				return;

			await drainSignal.ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Gets the completion signal a drain waiter should park on, creating it on first use.
	/// </summary>
	/// <returns>The current drain completion signal.</returns>
	private Task GetOrCreateDrainSignal()
	{
		// The task source is created under the lock but awaited outside it; RunContinuationsAsynchronously keeps the
		// waiter's continuation off the signalling thread, so completing the signal never runs waiter code inline.
		lock (_drainSignalSyncRoot)
		{
			_drainSignal ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			return _drainSignal.Task;
		}
	}

	/// <summary>
	/// Releases a parked drain waiter by completing the current completion signal, if one exists.
	/// </summary>
	private void SignalDrainProgress()
	{
		lock (_drainSignalSyncRoot)
		{
			TaskCompletionSource? drainSignal = _drainSignal;
			_drainSignal = null;
			drainSignal?.TrySetResult();
		}
	}

	/// <summary>
	/// Completes subscriber delivery for disposal, so a payload that is still queued for a subscriber is dropped
	/// instead of being delivered on a thread-pool thread after disposal returns.
	/// </summary>
	/// <remarks>
	/// A handler that already started still runs to completion; only the queued-but-not-started payloads are dropped.
	/// </remarks>
	internal void CompleteSubscribers()
	{
		_diagnosticsPublishedSubscribers.Complete();
		_semanticTokensRefreshSubscribers.Complete();
		_diagnosticRefreshSubscribers.Complete();
	}

	/// <summary>
	/// Observes one background loop task so unexpected termination is logged immediately while the client is still active.
	/// </summary>
	/// <param name="task">The background loop task to observe.</param>
	/// <param name="loopName">The logical loop name used for diagnostics.</param>
	/// <param name="markTransportUnhealthyOnUnexpectedTermination">Whether unexpected termination should mark the transport unhealthy.</param>
	internal void ObserveBackgroundLoop(
		Task task,
		string loopName,
		bool markTransportUnhealthyOnUnexpectedTermination)
	{
		task.ContinueWith(
			static (completedTask, state) =>
			{
				if (state is not BackgroundLoopObservation observation)
					return;

				observation.Owner.LogUnexpectedBackgroundLoopTermination(
					completedTask,
					observation.LoopName,
					observation.MarkTransportUnhealthyOnUnexpectedTermination);
			},
			new BackgroundLoopObservation(this, loopName, markTransportUnhealthyOnUnexpectedTermination),
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
	}

	/// <summary>
	/// Reports whether the supplied background loop task already logged its unexpected termination before disposal.
	/// </summary>
	/// <param name="task">The completed background loop task.</param>
	/// <returns><see langword="true"/> when the task already logged unexpected termination; otherwise, <see langword="false"/>.</returns>
	internal bool WasObservedBackgroundLoopTermination(Task task)
	{
		lock (_observedBackgroundLoopSyncRoot)
			return _observedBackgroundLoopTerminations.Contains(task);
	}

	/// <summary>
	/// Starts one background loop task and attaches immediate fault observation.
	/// </summary>
	/// <param name="backgroundLoop">The background loop delegate.</param>
	/// <param name="loopName">The logical loop name used for diagnostics.</param>
	/// <param name="markTransportUnhealthyOnUnexpectedTermination">Whether unexpected termination should mark the transport unhealthy.</param>
	/// <returns>The started background loop task.</returns>
	private Task StartObservedBackgroundLoop(
		Func<Task> backgroundLoop,
		string loopName,
		bool markTransportUnhealthyOnUnexpectedTermination)
	{
		Task task = Task.Run(backgroundLoop, CancellationToken.None);
		ObserveBackgroundLoop(task, loopName, markTransportUnhealthyOnUnexpectedTermination);
		return task;
	}

	/// <summary>
	/// Logs unexpected background loop termination while the client is still active.
	/// </summary>
	/// <param name="task">The completed background loop task.</param>
	/// <param name="loopName">The logical loop name used for diagnostics.</param>
	/// <param name="markTransportUnhealthyOnUnexpectedTermination">Whether unexpected termination should mark the transport unhealthy.</param>
	private void LogUnexpectedBackgroundLoopTermination(
		Task task,
		string loopName,
		bool markTransportUnhealthyOnUnexpectedTermination)
	{
		if (_isDisposed() || _lifetimeToken.IsCancellationRequested)
			return;

		if (!TryMarkObservedBackgroundLoopTermination(task))
			return;

		if (markTransportUnhealthyOnUnexpectedTermination)
			_markTransportUnhealthy();

		if (task.IsFaulted && task.Exception is { } aggregateException)
		{
			Exception loggedException = aggregateException.Flatten().InnerExceptions.Count == 1
				? aggregateException.Flatten().InnerExceptions[0]
				: aggregateException.Flatten();

			_logger.LogWarning(loggedException, "Language server background loop '{LoopName}' terminated unexpectedly while the client was still active.", loopName);
			return;
		}

		if (task.IsCanceled)
		{
			_logger.LogWarning("Language server background loop '{LoopName}' was canceled unexpectedly while the client was still active.", loopName);
			return;
		}

		_logger.LogWarning("Language server background loop '{LoopName}' completed unexpectedly while the client was still active.", loopName);
	}

	/// <summary>
	/// Records that one background loop termination has already been logged before disposal.
	/// </summary>
	/// <param name="task">The completed background loop task.</param>
	/// <returns><see langword="true"/> when the task was newly marked; otherwise, <see langword="false"/>.</returns>
	private bool TryMarkObservedBackgroundLoopTermination(Task task)
	{
		lock (_observedBackgroundLoopSyncRoot)
			return _observedBackgroundLoopTerminations.Add(task);
	}

	/// <summary>
	/// Clears the unexpected-termination marker for one completed background loop task before that loop is restarted.
	/// </summary>
	/// <param name="task">The completed background loop task to forget.</param>
	private void ForgetObservedBackgroundLoopTermination(Task task)
	{
		lock (_observedBackgroundLoopSyncRoot)
			_observedBackgroundLoopTerminations.Remove(task);
	}

	/// <summary>
	/// Builds the queue key used to coalesce diagnostics payloads.
	/// </summary>
	/// <param name="transportGeneration">The transport generation that published the diagnostics.</param>
	/// <param name="parameters">The diagnostics payload.</param>
	/// <returns>The queue key for the payload.</returns>
	private DiagnosticsQueueKey GetDiagnosticsQueueKey(long transportGeneration, PublishDiagnosticsParams parameters)
	{
		if (!string.IsNullOrWhiteSpace(parameters.Uri))
			return new(transportGeneration, DiagnosticsDocumentKey.FromUri(parameters.Uri));

		// A payload without a URI has no document identity to coalesce on, so every one gets its own slot.
		return new(transportGeneration,
			new DiagnosticsDocumentKey(false, "diagnostics:" + Interlocked.Increment(ref _diagnosticsFallbackSequence)));
	}

	/// <summary>
	/// Reports whether one queued diagnostics callback may still be delivered to subscribers.
	/// </summary>
	/// <param name="queuedDiagnostics">The queued payload to inspect.</param>
	/// <returns><see langword="true"/> when the payload should be delivered; otherwise, <see langword="false"/>.</returns>
	private bool ShouldDeliverQueuedCallback(QueuedDiagnostics queuedDiagnostics)
	{
		// A queued payload is delivered only while its generation still accepts server callbacks, which also
		// drops payloads after their transport was marked unhealthy.
		return _canAcceptServerCallbacksForGeneration(queuedDiagnostics.TransportGeneration);
	}

	/// <summary>
	/// Queues a diagnostics callback for background subscriber dispatch.
	/// </summary>
	/// <param name="documentKey">The document identity used to coalesce the callback payload.</param>
	/// <param name="parameters">The queued payload, including the transport generation that must still accept callbacks when it is delivered.</param>
	private void QueueDiagnosticsCallback(DiagnosticsDocumentKey documentKey, QueuedDiagnostics parameters)
	{
		_pendingCallbackDiagnostics[documentKey] = parameters;
		_callbackSignal.Writer.TryWrite(true);
	}

	/// <summary>
	/// Stores the state needed to log one observed background loop termination.
	/// </summary>
	/// <param name="Owner">The owning diagnostics router.</param>
	/// <param name="LoopName">The logical loop name.</param>
	/// <param name="MarkTransportUnhealthyOnUnexpectedTermination">Whether unexpected termination should mark the transport unhealthy.</param>
	private readonly record struct BackgroundLoopObservation(
		DiagnosticsRouter Owner,
		string LoopName,
		bool MarkTransportUnhealthyOnUnexpectedTermination);
}
