using System.Runtime.ExceptionServices;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Raises the attached-document changes the view tracker queued, on the dispatcher and in the order the tracker queued them.</summary>
/// <remarks>
/// <para>
/// One raise runs at a time. A raise that starts while another runs, such as one from a handler that calls the coordinator, leaves its changes
/// to the running one, which takes changes until the queue is empty.
/// </para>
/// <para>
/// Each handler of the invocation list runs on its own, so a handler that throws costs no other handler a change and no operation its outcome.
/// The fault is posted to the dispatcher and rethrown there, where the toolkit's unhandled-exception handler sees it; a dispatcher that rejects
/// the post or runs it inline drops it.
/// </para>
/// </remarks>
internal sealed class AttachedDocumentChangeForwarder
{
	private readonly object _syncRoot;
	private readonly ViewTracker _views;
	private readonly Func<Action, Task> _dispatchViewAction;
	private readonly object _sender;
	private readonly Func<EventHandler<AttachedDocumentChangedEventArgs>?> _readHandlers;
	private readonly Action<Action> _post;

	private bool _isRaising;

	/// <param name="syncRoot">The coordinator's lock, which the tracker queues its changes under.</param>
	/// <param name="views">The tracker that queues the changes.</param>
	/// <param name="dispatchViewAction">The delegate that runs actions on the dispatcher.</param>
	/// <param name="sender">The coordinator, which each handler receives as the sender.</param>
	/// <param name="readHandlers">Reads the coordinator's current handlers.</param>
	/// <param name="post">Queues an action on the dispatcher without waiting for it, which rethrows a handler fault there.</param>
	internal AttachedDocumentChangeForwarder(
		object syncRoot,
		ViewTracker views,
		Func<Action, Task> dispatchViewAction,
		object sender,
		Func<EventHandler<AttachedDocumentChangedEventArgs>?> readHandlers,
		Action<Action> post
	)
	{
		_syncRoot = syncRoot;
		_views = views;
		_dispatchViewAction = dispatchViewAction;
		_sender = sender;
		_readHandlers = readHandlers;
		_post = post;
	}

	/// <summary>Raises the queued changes through the dispatcher, which is not invoked when the queue is empty.</summary>
	internal Task RaisePendingAsync() => _views.HasPendingChanges ? _dispatchViewAction(RaisePending) : Task.CompletedTask;

	private void RaisePending()
	{
		lock (_syncRoot)
		{
			if (_isRaising)
				return;

			_isRaising = true;
		}

		bool ended = false;

		try
		{
			while (true)
			{
				AttachedDocumentChangedEventArgs? change;

				// The empty check and the end of the raise share one critical section, so a change queued after the check finds no raise running.
				lock (_syncRoot)
				{
					if (!_views.TryTakeChange(out change))
					{
						_isRaising = false;
						ended = true;
						return;
					}
				}

				if (_readHandlers() is not { } handlers)
					continue;

				foreach (Delegate handler in handlers.GetInvocationList())
					Raise((EventHandler<AttachedDocumentChangedEventArgs>)handler, change);
			}
		}
		finally
		{
			// A raise that ended early, such as on a fault of the tracker, frees the next raise; one that emptied the queue released it already.
			if (!ended)
			{
				lock (_syncRoot)
					_isRaising = false;
			}
		}
	}

	private void Raise(EventHandler<AttachedDocumentChangedEventArgs> handler, AttachedDocumentChangedEventArgs change)
	{
		try
		{
			handler(_sender, change);
		}
		catch (Exception exception)
		{
			Surface(ExceptionDispatchInfo.Capture(exception));
		}
	}

	private void Surface(ExceptionDispatchInfo fault)
	{
		try
		{
			_post(fault.Throw);
		}
		catch
		{
			// A dispatcher that is shutting down rejects the post, and one that runs it inline throws the fault back here; the operation keeps its
			// outcome either way.
		}
	}
}
