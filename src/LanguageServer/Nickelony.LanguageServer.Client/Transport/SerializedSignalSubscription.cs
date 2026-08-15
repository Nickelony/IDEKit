namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Collapses repeated signal notifications for one subscriber into a serialized background drain.
/// </summary>
/// <typeparam name="THandler">The subscriber delegate type this drain delivers to.</typeparam>
internal sealed class SerializedSignalSubscription<THandler> : SerializedSubscriberDrain<THandler>
	where THandler : Delegate
{
	private readonly Action<THandler> _invokeHandler;
	private readonly Action<Exception> _logHandlerFailure;

	private int _pendingSignal;

	/// <summary>
	/// Initializes a new instance of the <see cref="SerializedSignalSubscription{THandler}"/> class.
	/// </summary>
	/// <param name="handler">The subscriber this drain delivers to.</param>
	/// <param name="invokeHandler">Invokes the subscribed handler.</param>
	/// <param name="logHandlerFailure">Logs one handler exception without interrupting later subscribers.</param>
	/// <param name="onProgress">
	/// Invoked whenever this drain advances toward (or reaches) its idle state, so a quiescence waiter can be
	/// released by a completion signal instead of polling.
	/// </param>
	public SerializedSignalSubscription(THandler handler, Action<THandler> invokeHandler, Action<Exception> logHandlerFailure, Action? onProgress = null)
		: base(handler, onProgress)
	{
		_invokeHandler = invokeHandler;
		_logHandlerFailure = logHandlerFailure;
	}

	protected override bool HasPendingWork => Volatile.Read(ref _pendingSignal) != 0;

	/// <summary>
	/// Queues one signal notification for this subscriber.
	/// </summary>
	public void Enqueue()
	{
		if (IsDisposed)
			return;

		Interlocked.Exchange(ref _pendingSignal, 1);
		TryScheduleDrain();
	}

	protected override void OnDisposed() => Interlocked.Exchange(ref _pendingSignal, 0);

	protected override bool TryDrainNext()
	{
		if (Interlocked.Exchange(ref _pendingSignal, 0) == 0 || IsDisposed)
			return false;

		try
		{
			_invokeHandler(Handler);
		}
		catch (Exception exception)
		{
			TryLogHandlerFailure(_logHandlerFailure, exception);
		}

		return true;
	}

	protected override void ReportDrainFailure(Exception exception)
		=> TryLogHandlerFailure(_logHandlerFailure, exception);
}
