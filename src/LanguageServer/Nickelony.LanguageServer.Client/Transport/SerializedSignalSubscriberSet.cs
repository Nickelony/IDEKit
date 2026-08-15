namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Serializes one signal-style callback stream per subscriber while allowing different subscribers to run concurrently.
/// </summary>
/// <typeparam name="THandler">The subscriber delegate type.</typeparam>
internal sealed class SerializedSignalSubscriberSet<THandler>
	: SerializedSubscriberSet<THandler, SerializedSignalSubscription<THandler>>
	where THandler : Delegate
{
	private readonly Action<THandler> _invokeHandler;
	private readonly Action<Exception> _logHandlerFailure;

	/// <summary>
	/// Initializes a new instance of the <see cref="SerializedSignalSubscriberSet{THandler}"/> class.
	/// </summary>
	/// <param name="invokeHandler">Invokes one subscribed handler.</param>
	/// <param name="logHandlerFailure">Logs one handler exception without interrupting later subscribers.</param>
	/// <param name="onProgress">The callback each subscription invokes as it advances toward its idle state, or <see langword="null"/>.</param>
	public SerializedSignalSubscriberSet(Action<THandler> invokeHandler, Action<Exception> logHandlerFailure, Action? onProgress = null)
		: base(onProgress)
	{
		_invokeHandler = invokeHandler;
		_logHandlerFailure = logHandlerFailure;
	}

	/// <inheritdoc/>
	protected override SerializedSignalSubscription<THandler> CreateSubscription(THandler handler)
		=> new(handler, _invokeHandler, _logHandlerFailure, OnProgress);

	/// <summary>
	/// Queues one signal notification for each current subscriber.
	/// </summary>
	public void Dispatch()
	{
		SerializedSignalSubscription<THandler>[] subscriptions = SnapshotSubscriptions();

		for (int i = 0; i < subscriptions.Length; i++)
			subscriptions[i].Enqueue();
	}
}
