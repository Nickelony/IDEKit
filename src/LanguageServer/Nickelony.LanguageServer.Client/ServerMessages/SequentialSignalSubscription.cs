namespace Nickelony.LanguageServer.Client;

/// <summary>Collapses repeated signal notifications for one subscriber into one sequential background drain.</summary>
/// <typeparam name="THandler">The subscriber delegate type this subscription delivers to.</typeparam>
internal sealed class SequentialSignalSubscription<THandler> : SequentialSubscription<THandler>
	where THandler : Delegate
{
	private readonly Action<THandler> _invokeHandler;
	private readonly Action<Exception> _logHandlerFailure;

	private int _pendingSignal;

	/// <param name="handler">The subscriber this subscription delivers to.</param>
	/// <param name="invokeHandler">Invokes the subscribed handler.</param>
	/// <param name="logHandlerFailure">Logs one handler exception without interrupting later subscribers.</param>
	internal SequentialSignalSubscription(THandler handler, Action<THandler> invokeHandler, Action<Exception> logHandlerFailure)
		: base(handler)
	{
		_invokeHandler = invokeHandler;
		_logHandlerFailure = logHandlerFailure;
	}

	internal override bool HasPendingWork => Volatile.Read(ref _pendingSignal) != 0;

	/// <summary>Queues one signal notification for this subscriber.</summary>
	internal void Enqueue()
	{
		if (IsDisposed)
			return;

		Interlocked.Exchange(ref _pendingSignal, 1);
		ScheduleDrain();
	}

	internal override void OnDisposed() => Interlocked.Exchange(ref _pendingSignal, 0);

	internal override bool TryDrainNext()
	{
		if (Interlocked.Exchange(ref _pendingSignal, 0) == 0 || IsDisposed)
			return false;

		try
		{
			_invokeHandler(Handler);
		}
		catch (Exception exception)
		{
			LogHandlerFailure(_logHandlerFailure, exception);
		}

		return true;
	}

	internal override void ReportDrainFailure(Exception exception) => LogHandlerFailure(_logHandlerFailure, exception);
}
