using System.Collections.Concurrent;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Keeps only the newest diagnostics payload per document key for one subscriber and drains them in enqueue order.
/// </summary>
/// <remarks>
/// The per-subscriber payload snapshot and the enqueue-order sort are deliberate delivery-contract choices, not
/// allocations to optimize away: each subscriber owns its payload instance, and a newer update must never be drained
/// before an older one.
/// </remarks>
/// <typeparam name="THandler">The subscriber delegate type this drain delivers to.</typeparam>
internal sealed class SerializedDiagnosticsSubscription<THandler> : SerializedSubscriberDrain<THandler>
	where THandler : Delegate
{
	private readonly Action<THandler, PublishDiagnosticsParams> _invokeHandler;
	private readonly Action<Exception> _logHandlerFailure;
	private readonly ClientTestHooks? _testHooks;

	private readonly ConcurrentDictionary<DiagnosticsDocumentKey, PendingDiagnosticsPayload> _pendingPayloads = new(DiagnosticsDocumentKey.Comparer);

	private long _nextSequence;

	private readonly record struct PendingDiagnosticsPayload(long Sequence, PublishDiagnosticsParams Parameters);
	private readonly record struct DrainedDiagnosticsPayload(long Sequence, PublishDiagnosticsParams Parameters);

	/// <summary>
	/// Initializes a new instance of the <see cref="SerializedDiagnosticsSubscription{THandler}"/> class.
	/// </summary>
	/// <param name="handler">The subscriber this drain delivers to.</param>
	/// <param name="invokeHandler">Invokes the subscribed handler with one diagnostics payload.</param>
	/// <param name="logHandlerFailure">Logs one handler exception without interrupting later subscribers.</param>
	/// <param name="testHooks">
	/// The test-only hooks whose <see cref="ClientTestHooks.BeforePendingPayloadReplacement"/> seam a concurrency test
	/// uses to hold a pending payload replacement mid-flight, or <see langword="null"/> for production.
	/// </param>
	/// <param name="onProgress">
	/// Invoked whenever this drain advances toward (or reaches) its idle state, so a quiescence waiter can be
	/// released by a completion signal instead of polling.
	/// </param>
	public SerializedDiagnosticsSubscription(
		THandler handler,
		Action<THandler, PublishDiagnosticsParams> invokeHandler,
		Action<Exception> logHandlerFailure,
		ClientTestHooks? testHooks = null,
		Action? onProgress = null)
		: base(handler, onProgress)
	{
		_invokeHandler = invokeHandler;
		_logHandlerFailure = logHandlerFailure;
		_testHooks = testHooks;
	}

	protected override bool HasPendingWork => !_pendingPayloads.IsEmpty;

	/// <summary>
	/// Queues one diagnostics payload for this subscriber, keeping only the newest payload per document key.
	/// </summary>
	/// <param name="documentKey">The document key used to coalesce repeated payloads.</param>
	/// <param name="parameters">The diagnostics payload to dispatch.</param>
	public void Enqueue(DiagnosticsDocumentKey documentKey, PublishDiagnosticsParams parameters)
	{
		if (IsDisposed)
			return;

		// Allocate the sequence when this enqueue begins, even when the document already
		// has a pending payload. A retry must never allow an older caller to replace a
		// payload published later by another concurrent caller.
		long incomingSequence = Interlocked.Increment(ref _nextSequence);

		while (true)
		{
			if (!_pendingPayloads.TryGetValue(documentKey, out PendingDiagnosticsPayload existingPayload))
			{
				if (_pendingPayloads.TryAdd(documentKey, new PendingDiagnosticsPayload(incomingSequence, parameters)))
					break;

				continue;
			}

			if (incomingSequence <= existingPayload.Sequence)
				break;

			_testHooks?.BeforePendingPayloadReplacement?.Invoke(parameters);

			if (_pendingPayloads.TryUpdate(documentKey,
				new PendingDiagnosticsPayload(incomingSequence, parameters),
				existingPayload))
			{
				break;
			}
		}

		TryScheduleDrain();
	}

	protected override void OnDisposed() => _pendingPayloads.Clear();

	protected override bool TryDrainNext()
	{
		if (_pendingPayloads.IsEmpty)
			return false;

		var drainedPayloads = new List<DrainedDiagnosticsPayload>();

		foreach (KeyValuePair<DiagnosticsDocumentKey, PendingDiagnosticsPayload> entry in _pendingPayloads)
		{
			if (_pendingPayloads.TryRemove(entry.Key, out PendingDiagnosticsPayload payload))
				drainedPayloads.Add(new DrainedDiagnosticsPayload(payload.Sequence, payload.Parameters));
		}

		drainedPayloads.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));

		for (int i = 0; i < drainedPayloads.Count; i++)
		{
			if (IsDisposed)
				return false;

			try
			{
				_invokeHandler(Handler, drainedPayloads[i].Parameters);
			}
			catch (Exception exception)
			{
				TryLogHandlerFailure(_logHandlerFailure, exception);
			}
		}

		return drainedPayloads.Count > 0;
	}

	protected override void ReportDrainFailure(Exception exception)
		=> TryLogHandlerFailure(_logHandlerFailure, exception);
}
