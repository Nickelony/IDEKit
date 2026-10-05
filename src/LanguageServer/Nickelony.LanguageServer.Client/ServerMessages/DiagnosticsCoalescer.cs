using System.Collections.Concurrent;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>Coalesces the diagnostics payloads of each connection generation and document on their way to the server-message dispatcher.</summary>
/// <remarks>
/// A payload is forwarded only while its generation still accepts server messages. A bounded wake signal collapses a burst of payloads into one pass,
/// and a pass drains the whole pending set. That is intentional at the routing scale (one workspace with a bounded set of tracked documents):
/// the snapshot keeps the pass race-free against concurrent queue calls and stays cheap for that document count.
/// </remarks>
internal sealed class DiagnosticsCoalescer
{
	private readonly record struct QueueKey(long ConnectionGeneration, DiagnosticsDocumentKey DocumentKey);

	private sealed class QueueKeyComparer : IEqualityComparer<QueueKey>
	{
		internal static readonly QueueKeyComparer Instance = new();

		/// <inheritdoc/>
		public bool Equals(QueueKey left, QueueKey right) =>
			left.ConnectionGeneration == right.ConnectionGeneration && DiagnosticsDocumentKey.Comparer.Equals(left.DocumentKey, right.DocumentKey);

		/// <inheritdoc/>
		public int GetHashCode(QueueKey key) =>
			HashCode.Combine(key.ConnectionGeneration, DiagnosticsDocumentKey.Comparer.GetHashCode(key.DocumentKey));
	}

	private readonly Func<long, bool> _canAcceptServerMessagesForGeneration;
	private readonly Action<DiagnosticsDocumentKey, QueuedDiagnostics> _forward;
	private readonly Action<QueueKey, QueuedDiagnostics> _forwardPending;
	private readonly DrainWorker _worker;

	// The dictionary shares the document-key comparer: a file URI coalesces on its normalized local path with the platform's
	// local-path identity - so a case-only path difference cannot produce duplicate coalescing slots on a case-insensitive host -
	// while any other URI compares ordinally.
	private readonly ConcurrentDictionary<QueueKey, QueuedDiagnostics> _pendingDiagnostics = new(QueueKeyComparer.Instance);

	private long _fallbackSequence;

	/// <param name="canAcceptServerMessagesForGeneration">Reports whether a connection generation still accepts server messages.</param>
	/// <param name="forward">Receives each payload whose generation still accepts server messages, keyed by document.</param>
	/// <param name="lifetimeToken">Signals client disposal to the coalescer.</param>
	internal DiagnosticsCoalescer(
		Func<long, bool> canAcceptServerMessagesForGeneration,
		Action<DiagnosticsDocumentKey, QueuedDiagnostics> forward,
		CancellationToken lifetimeToken
	)
	{
		_canAcceptServerMessagesForGeneration = canAcceptServerMessagesForGeneration;
		_forward = forward;
		_forwardPending = (queueKey, queuedDiagnostics) => _forward(queueKey.DocumentKey, queuedDiagnostics);
		_worker = new DrainWorker(ForwardPending, lifetimeToken);
	}

	/// <summary>Whether no payload is waiting and no pass is in flight; tests read it through <see cref="ServerMessageRouter.IsDrained"/>.</summary>
	internal bool IsIdle => _pendingDiagnostics.IsEmpty && !_worker.HasPassInFlight;

	/// <summary>Queues a diagnostics payload for later forwarding on the coalescer.</summary>
	/// <param name="connectionGeneration">The connection generation that published the diagnostics.</param>
	/// <param name="parameters">The diagnostics payload.</param>
	internal void Queue(long connectionGeneration, PublishDiagnosticsParams parameters)
	{
		// A raise after the signal channel completed has no coalescer left to drain it; drop it instead of retaining it
		// in a pending dictionary for the remainder of the client's lifetime.
		if (_worker.IsCompleted)
			return;

		// Keep only the newest diagnostics payload per file within one connection generation and wake the coalescer if it is idle.
		// First snapshot level: detach the queued payload from the caller's arrays so a caller that reuses or mutates its
		// payload after this call cannot change what the coalescer eventually observes. A second per-subscriber snapshot in
		// SequentialDiagnosticsSubscriberSet.Dispatch keeps subscribers from sharing payload instances with each other.
		// The snapshots copy the outer diagnostics sequence only: the diagnostic entries and their nested
		// related-information, tag, code and data values stay shared and are read-only (see PublishDiagnosticsParams.CreateSnapshot).
		QueueKey queueKey = GetQueueKey(connectionGeneration, parameters);

		_pendingDiagnostics[queueKey] = new QueuedDiagnostics(connectionGeneration, parameters.CreateSnapshot());

		if (!_worker.TrySignal())
		{
			// The signal channel completed concurrently (teardown marks completion before it completes the channel), so
			// this payload has no coalescer left to drain it. Drop it instead of retaining it for the remainder of the
			// client's lifetime, which would keep IsIdle false forever.
			_pendingDiagnostics.TryRemove(queueKey, out _);
		}
	}

	/// <summary>Forwards queued diagnostics whose connection generation still accepts server messages, coalescing repeated updates.</summary>
	internal Task RunAsync() => _worker.RunAsync();

	/// <summary>Completes the wake signal so the coalescer finishes during disposal, and drops whatever is still queued.</summary>
	internal void Complete()
	{
		// Mark completion first so a concurrent raise becomes a no-op instead of queueing a payload that this
		// teardown will never deliver; then complete the wake signal and drop whatever is still queued. Without
		// this the pending dictionary would retain every payload that arrived between completion and detach.
		_worker.Complete();
		_pendingDiagnostics.Clear();
	}

	/// <summary>Forwards the pending payloads until none is left.</summary>
	private void ForwardPending()
	{
		while (!_pendingDiagnostics.IsEmpty)
			QueuedDiagnostics.DeliverPending(_pendingDiagnostics, _canAcceptServerMessagesForGeneration, _forwardPending);
	}

	/// <summary>Builds the queue key that coalesces diagnostics payloads.</summary>
	/// <param name="connectionGeneration">The connection generation that published the diagnostics.</param>
	/// <param name="parameters">The diagnostics payload.</param>
	private QueueKey GetQueueKey(long connectionGeneration, PublishDiagnosticsParams parameters)
	{
		if (!string.IsNullOrWhiteSpace(parameters.Uri))
			return new(connectionGeneration, DiagnosticsDocumentKey.FromUri(parameters.Uri));

		// A payload without a URI has no document identity to coalesce on, so every one gets its own slot.
		return new(connectionGeneration, new DiagnosticsDocumentKey(false, "diagnostics:" + Interlocked.Increment(ref _fallbackSequence)));
	}
}
