namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Delivers one signal-style callback stream to each subscriber one at a time while allowing different subscribers to run concurrently.
/// </summary>
/// <typeparam name="THandler">The subscriber delegate type.</typeparam>
internal sealed class SequentialSignalSubscriberSet<THandler> : SequentialSubscriberSet<THandler, SequentialSignalSubscription<THandler>>
	where THandler : Delegate
{
	private readonly Action<THandler> _invokeHandler;
	private readonly Action<Exception> _logHandlerFailure;

	/// <param name="invokeHandler">Invokes one subscribed handler.</param>
	/// <param name="logHandlerFailure">Logs one handler exception without interrupting later subscribers.</param>
	internal SequentialSignalSubscriberSet(Action<THandler> invokeHandler, Action<Exception> logHandlerFailure)
	{
		_invokeHandler = invokeHandler;
		_logHandlerFailure = logHandlerFailure;
	}

	/// <inheritdoc/>
	internal override SequentialSignalSubscription<THandler> CreateSubscription(THandler handler) =>
		new(handler, invokeHandler: _invokeHandler, logHandlerFailure: _logHandlerFailure);

	/// <summary>Queues one signal notification for each current subscriber.</summary>
	internal void Dispatch()
	{
		SequentialSignalSubscription<THandler>[] subscriptions = SnapshotSubscriptions();

		for (int i = 0; i < subscriptions.Length; i++)
			subscriptions[i].Enqueue();
	}
}
