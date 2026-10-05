using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>Delivers diagnostics payloads to each subscriber one at a time while coalescing repeated updates by document key.</summary>
/// <typeparam name="THandler">The subscriber delegate type.</typeparam>
internal sealed class SequentialDiagnosticsSubscriberSet<THandler> : SequentialSubscriberSet<THandler, SequentialDiagnosticsSubscription<THandler>>
	where THandler : Delegate
{
	private readonly Action<THandler, PublishDiagnosticsParams> _invokeHandler;
	private readonly Action<Exception> _logHandlerFailure;
	private readonly LanguageServerClientTestHooks? _testHooks;

	/// <param name="invokeHandler">Invokes one subscribed handler with one diagnostics payload.</param>
	/// <param name="logHandlerFailure">Logs one handler exception without interrupting later subscribers.</param>
	/// <param name="testHooks">The test-only hooks seam threaded to each subscription, or <see langword="null"/> for production.</param>
	internal SequentialDiagnosticsSubscriberSet(
		Action<THandler, PublishDiagnosticsParams> invokeHandler,
		Action<Exception> logHandlerFailure,
		LanguageServerClientTestHooks? testHooks = null
	)
	{
		_invokeHandler = invokeHandler;
		_logHandlerFailure = logHandlerFailure;
		_testHooks = testHooks;
	}

	/// <inheritdoc/>
	internal override SequentialDiagnosticsSubscription<THandler> CreateSubscription(THandler handler) =>
		new(handler, invokeHandler: _invokeHandler, logHandlerFailure: _logHandlerFailure, testHooks: _testHooks);

	/// <summary>Queues one diagnostics payload for each current subscriber.</summary>
	/// <param name="documentKey">The document key that coalesces repeated payloads per subscriber.</param>
	/// <param name="parameters">The diagnostics payload to dispatch.</param>
	internal void Dispatch(DiagnosticsDocumentKey documentKey, PublishDiagnosticsParams parameters)
	{
		SequentialDiagnosticsSubscription<THandler>[] subscriptions = SnapshotSubscriptions();

		for (int i = 0; i < subscriptions.Length; i++)
		{
			// Per-subscriber snapshot: each subscriber owns its payload instance so concurrent subscribers (or a still-busy
			// subscriber) cannot observe another subscriber's array mutations, even though the queued payload was already
			// detached once in DiagnosticsCoalescer.Queue.
			subscriptions[i].Enqueue(documentKey, parameters.CreateSnapshot());
		}
	}
}
