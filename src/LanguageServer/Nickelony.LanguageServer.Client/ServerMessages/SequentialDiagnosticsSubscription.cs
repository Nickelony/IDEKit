using System.Collections.Concurrent;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>Keeps only the newest diagnostics payload per document key for one subscriber and drains them in enqueue order.</summary>
/// <typeparam name="THandler">The subscriber delegate type this subscription delivers to.</typeparam>
/// <remarks>
/// The per-subscriber payload snapshot and the enqueue-order sort are deliberate delivery-contract choices, not allocations to optimize away:
/// each subscriber owns its payload instance, and a newer update must never be drained before an older one.
/// </remarks>
internal sealed class SequentialDiagnosticsSubscription<THandler> : SequentialSubscription<THandler>
	where THandler : Delegate
{
	private readonly Action<THandler, PublishDiagnosticsParams> _invokeHandler;
	private readonly Action<Exception> _logHandlerFailure;
	private readonly LanguageServerClientTestHooks? _testHooks;

	private readonly ConcurrentDictionary<DiagnosticsDocumentKey, PendingDiagnosticsEntry> _pendingEntries = new(DiagnosticsDocumentKey.Comparer);

	private long _nextSequence;

	private readonly record struct PendingDiagnosticsEntry(long Sequence, PublishDiagnosticsParams Parameters);

	private readonly record struct DrainedDiagnosticsEntry(long Sequence, PublishDiagnosticsParams Parameters);

	/// <param name="handler">The subscriber this subscription delivers to.</param>
	/// <param name="invokeHandler">Invokes the subscribed handler with one diagnostics payload.</param>
	/// <param name="logHandlerFailure">Logs one handler exception without interrupting later subscribers.</param>
	/// <param name="testHooks">The hooks seam threaded to the subscription, or <see langword="null"/> for production.</param>
	internal SequentialDiagnosticsSubscription(
		THandler handler,
		Action<THandler, PublishDiagnosticsParams> invokeHandler,
		Action<Exception> logHandlerFailure,
		LanguageServerClientTestHooks? testHooks = null
	)
		: base(handler)
	{
		_invokeHandler = invokeHandler;
		_logHandlerFailure = logHandlerFailure;
		_testHooks = testHooks;
	}

	internal override bool HasPendingWork => !_pendingEntries.IsEmpty;

	/// <summary>Queues one diagnostics payload for this subscriber, keeping only the newest payload per document key.</summary>
	/// <param name="documentKey">The document key that coalesces repeated payloads.</param>
	/// <param name="parameters">The diagnostics payload to dispatch.</param>
	internal void Enqueue(DiagnosticsDocumentKey documentKey, PublishDiagnosticsParams parameters)
	{
		if (IsDisposed)
			return;

		// Allocate the sequence when this enqueue begins, even when the document already
		// has a pending payload. A retry must never allow an older caller to replace a
		// payload published later by another concurrent caller.
		long incomingSequence = Interlocked.Increment(ref _nextSequence);

		while (true)
		{
			if (!_pendingEntries.TryGetValue(documentKey, out PendingDiagnosticsEntry existingEntry))
			{
				if (_pendingEntries.TryAdd(documentKey, new PendingDiagnosticsEntry(incomingSequence, parameters)))
					break;

				continue;
			}

			if (incomingSequence <= existingEntry.Sequence)
				break;

			_testHooks?.BeforePendingPayloadReplacement?.Invoke(parameters);

			if (_pendingEntries.TryUpdate(documentKey, new PendingDiagnosticsEntry(incomingSequence, parameters), existingEntry))
				break;
		}

		ScheduleDrain();
	}

	internal override void OnDisposed() => _pendingEntries.Clear();

	internal override bool TryDrainNext()
	{
		if (_pendingEntries.IsEmpty)
			return false;

		var drainedEntries = new List<DrainedDiagnosticsEntry>();

		foreach (KeyValuePair<DiagnosticsDocumentKey, PendingDiagnosticsEntry> pair in _pendingEntries)
		{
			if (_pendingEntries.TryRemove(pair.Key, out PendingDiagnosticsEntry pendingEntry))
				drainedEntries.Add(new DrainedDiagnosticsEntry(pendingEntry.Sequence, pendingEntry.Parameters));
		}

		drainedEntries.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));

		for (int i = 0; i < drainedEntries.Count; i++)
		{
			if (IsDisposed)
				return false;

			try
			{
				_invokeHandler(Handler, drainedEntries[i].Parameters);
			}
			catch (Exception exception)
			{
				LogHandlerFailure(_logHandlerFailure, exception);
			}
		}

		return drainedEntries.Count > 0;
	}

	internal override void ReportDrainFailure(Exception exception) => LogHandlerFailure(_logHandlerFailure, exception);
}
