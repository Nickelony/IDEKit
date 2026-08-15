namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Serializes diagnostics callback delivery per subscriber while coalescing repeated updates by document key.
/// </summary>
/// <typeparam name="THandler">The subscriber delegate type.</typeparam>
internal class SerializedDiagnosticsSubscriberSet<THandler>
	: SerializedSubscriberSet<THandler, SerializedDiagnosticsSubscription<THandler>>
	where THandler : Delegate
{
	private readonly Action<THandler, PublishDiagnosticsParams> _invokeHandler;
	private readonly Action<Exception> _logHandlerFailure;
	private readonly ClientTestHooks? _testHooks;

	/// <summary>
	/// Initializes a new instance of the <see cref="SerializedDiagnosticsSubscriberSet{THandler}"/> class.
	/// </summary>
	/// <param name="invokeHandler">Invokes one subscribed handler with one diagnostics payload.</param>
	/// <param name="logHandlerFailure">Logs one handler exception without interrupting later subscribers.</param>
	/// <param name="onProgress">The callback each subscription invokes as it advances toward its idle state, or <see langword="null"/>.</param>
	/// <param name="testHooks">The test-only hooks seam threaded to each subscription, or <see langword="null"/> for production.</param>
	public SerializedDiagnosticsSubscriberSet(Action<THandler, PublishDiagnosticsParams> invokeHandler, Action<Exception> logHandlerFailure, Action? onProgress = null, ClientTestHooks? testHooks = null)
		: base(onProgress)
	{
		_invokeHandler = invokeHandler;
		_logHandlerFailure = logHandlerFailure;
		_testHooks = testHooks;
	}

	/// <inheritdoc/>
	protected override SerializedDiagnosticsSubscription<THandler> CreateSubscription(THandler handler)
		=> new(handler, _invokeHandler, _logHandlerFailure, _testHooks, OnProgress);

	/// <summary>
	/// Queues one diagnostics payload for each current subscriber.
	/// </summary>
	/// <param name="documentKey">The document key used to coalesce repeated payloads per subscriber.</param>
	/// <param name="parameters">The diagnostics payload to dispatch.</param>
	public void Dispatch(DiagnosticsDocumentKey documentKey, PublishDiagnosticsParams parameters)
	{
		SerializedDiagnosticsSubscription<THandler>[] subscriptions = SnapshotSubscriptions();

		for (int i = 0; i < subscriptions.Length; i++)
		{
			// Per-subscriber snapshot: each subscriber owns its payload instance so concurrent subscribers (or a still-busy
			// subscriber) cannot observe another subscriber's array mutations, even though the queued payload was already
			// detached once in DiagnosticsRouter.QueueDiagnosticsPublished.
			subscriptions[i].Enqueue(documentKey, parameters.CreateSnapshot());
		}
	}
}
