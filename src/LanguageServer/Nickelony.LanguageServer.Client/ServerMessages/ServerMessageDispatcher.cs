using System.Collections.Concurrent;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>Delivers the coalesced refresh requests and diagnostics payloads to the subscriber sets, one round at a time.</summary>
/// <remarks>
/// A round delivers the pending semantic-token refresh, then the pending diagnostic refresh, then the pending diagnostics of every document,
/// and the dispatcher runs rounds until nothing is pending. Repeated refresh requests collapse into one flag, and a newer diagnostics payload
/// for a document replaces the queued one. A round snapshots the whole pending set, which keeps it race-free against concurrent queue calls and stays
/// cheap at the routing scale of one workspace.
/// </remarks>
internal sealed class ServerMessageDispatcher
{
	private readonly Func<long, bool> _canAcceptServerMessagesForGeneration;

	private readonly DrainWorker _worker;

	private readonly Action _invokeSemanticTokensRefreshRequested;
	private readonly Action _invokeDiagnosticRefreshRequested;

	private readonly Action<DiagnosticsDocumentKey, PublishDiagnosticsParams> _invokeDiagnosticsPublished;
	private readonly Action<DiagnosticsDocumentKey, QueuedDiagnostics> _deliverDiagnostics;

	private readonly ConcurrentDictionary<DiagnosticsDocumentKey, QueuedDiagnostics> _pendingDiagnostics = new(DiagnosticsDocumentKey.Comparer);

	private int _pendingSemanticTokensRefresh;
	private int _pendingDiagnosticRefresh;

	/// <param name="canAcceptServerMessagesForGeneration">Reports whether a connection generation still accepts server messages.</param>
	/// <param name="invokeSemanticTokensRefreshRequested">Delivers one semantic-token refresh request to the subscribers.</param>
	/// <param name="invokeDiagnosticRefreshRequested">Delivers one diagnostic refresh request to the subscribers.</param>
	/// <param name="invokeDiagnosticsPublished">Delivers one diagnostics payload to the subscribers.</param>
	/// <param name="lifetimeToken">Signals client disposal to the dispatcher.</param>
	internal ServerMessageDispatcher(
		Func<long, bool> canAcceptServerMessagesForGeneration,
		Action invokeSemanticTokensRefreshRequested,
		Action invokeDiagnosticRefreshRequested,
		Action<DiagnosticsDocumentKey, PublishDiagnosticsParams> invokeDiagnosticsPublished,
		CancellationToken lifetimeToken
	)
	{
		_canAcceptServerMessagesForGeneration = canAcceptServerMessagesForGeneration;
		_worker = new DrainWorker(DeliverRounds, lifetimeToken);
		_invokeSemanticTokensRefreshRequested = invokeSemanticTokensRefreshRequested;
		_invokeDiagnosticRefreshRequested = invokeDiagnosticRefreshRequested;
		_invokeDiagnosticsPublished = invokeDiagnosticsPublished;
		_deliverDiagnostics = (documentKey, queuedDiagnostics) => _invokeDiagnosticsPublished(documentKey, queuedDiagnostics.Parameters);
	}

	/// <summary>
	/// Whether no server message is waiting and no round is in flight; tests read it through <see cref="ServerMessageRouter.IsDrained"/>.
	/// </summary>
	internal bool IsIdle =>
		_pendingDiagnostics.IsEmpty
		&& Volatile.Read(ref _pendingSemanticTokensRefresh) == 0
		&& Volatile.Read(ref _pendingDiagnosticRefresh) == 0
		&& !_worker.HasPassInFlight;

	/// <summary>Queues a semantic tokens refresh request for background subscriber dispatch.</summary>
	internal void QueueSemanticTokensRefreshRequested()
	{
		// A refresh request queued after the signal channel completed has no dispatcher left to drain it; drop it
		// instead of retaining the pending flag for the remainder of the client's lifetime. The sibling
		// QueueDiagnosticRefreshRequested applies the same guard.
		if (_worker.IsCompleted)
			return;

		Interlocked.Exchange(ref _pendingSemanticTokensRefresh, 1);

		if (!_worker.TrySignal())
		{
			// The signal channel completed concurrently, so no dispatcher will drain the pending flag; clear it
			// instead of retaining it for the remainder of the client's lifetime.
			Interlocked.Exchange(ref _pendingSemanticTokensRefresh, 0);
		}
	}

	/// <summary>Queues a diagnostic refresh request for background subscriber dispatch.</summary>
	internal void QueueDiagnosticRefreshRequested()
	{
		// A refresh request queued after the signal channel completed has no dispatcher left to drain it; drop it
		// instead of retaining the pending flag for the remainder of the client's lifetime. The sibling
		// QueueSemanticTokensRefreshRequested applies the same guard.
		if (_worker.IsCompleted)
			return;

		Interlocked.Exchange(ref _pendingDiagnosticRefresh, 1);

		if (!_worker.TrySignal())
		{
			// The signal channel completed concurrently, so no dispatcher will drain the pending flag; clear it
			// instead of retaining it for the remainder of the client's lifetime.
			Interlocked.Exchange(ref _pendingDiagnosticRefresh, 0);
		}
	}

	/// <summary>Queues a diagnostics payload for background subscriber dispatch.</summary>
	/// <param name="documentKey">The document identity that coalesces the payload.</param>
	/// <param name="parameters">
	/// The queued payload, including the connection generation that must still accept server messages when it is delivered.
	/// </param>
	internal void QueueDiagnostics(DiagnosticsDocumentKey documentKey, QueuedDiagnostics parameters)
	{
		// The diagnostics coalescer can forward a payload after this dispatcher completed; drop it like a refresh request instead of retaining it.
		if (_worker.IsCompleted)
			return;

		_pendingDiagnostics[documentKey] = parameters;

		if (!_worker.TrySignal())
		{
			// The signal channel completed concurrently, so no dispatcher will drain the payload; drop it with the pending flags.
			_pendingDiagnostics.TryRemove(documentKey, out _);
		}
	}

	/// <summary>Delivers queued server messages in rounds until the wake signal completes or the lifetime is canceled.</summary>
	internal Task RunAsync() => _worker.RunAsync();

	/// <summary>Completes the wake signal so the dispatcher finishes during disposal, and drops whatever is still queued.</summary>
	internal void Complete()
	{
		// Mark completion first so a concurrent raise becomes a no-op instead of queueing a server message that this
		// teardown will never deliver; then complete the wake signal and drop whatever is still queued.
		_worker.Complete();

		Interlocked.Exchange(ref _pendingSemanticTokensRefresh, 0);
		Interlocked.Exchange(ref _pendingDiagnosticRefresh, 0);

		_pendingDiagnostics.Clear();
	}

	/// <summary>Delivers rounds of the pending server messages until a round finds nothing to deliver and nothing is pending.</summary>
	private void DeliverRounds()
	{
		while (true)
		{
			bool dispatchedMessages = false;

			if (Interlocked.Exchange(ref _pendingSemanticTokensRefresh, 0) != 0)
			{
				_invokeSemanticTokensRefreshRequested();
				dispatchedMessages = true;
			}

			if (Interlocked.Exchange(ref _pendingDiagnosticRefresh, 0) != 0)
			{
				_invokeDiagnosticRefreshRequested();
				dispatchedMessages = true;
			}

			if (
				!_pendingDiagnostics.IsEmpty
				&& QueuedDiagnostics.DeliverPending(_pendingDiagnostics, _canAcceptServerMessagesForGeneration, _deliverDiagnostics)
			)
			{
				dispatchedMessages = true;
			}

			if (
				!dispatchedMessages
				&& Volatile.Read(ref _pendingSemanticTokensRefresh) == 0
				&& Volatile.Read(ref _pendingDiagnosticRefresh) == 0
				&& _pendingDiagnostics.IsEmpty
			)
			{
				break;
			}
		}
	}
}
