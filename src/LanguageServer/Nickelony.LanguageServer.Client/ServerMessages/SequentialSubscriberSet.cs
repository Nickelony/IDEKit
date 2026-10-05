namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Provides the subscriber list management shared by the sequential subscriber sets: one lock guards the subscriber list so addition and removal
/// are atomic with respect to dispatch, and completion is terminal so a later addition cannot re-enable delivery during teardown.
/// </summary>
/// <typeparam name="THandler">The subscriber delegate type.</typeparam>
/// <typeparam name="TSubscription">The per-subscriber subscription type this set stores.</typeparam>
internal abstract class SequentialSubscriberSet<THandler, TSubscription>
	where THandler : Delegate
	where TSubscription : SequentialSubscription<THandler>
{
	private readonly object _syncRoot = new();
	private readonly List<TSubscription> _subscriptions = [];

	private bool _isComplete;

	/// <summary>Creates the per-subscriber subscription for one handler.</summary>
	/// <param name="handler">The subscriber to wrap.</param>
	internal abstract TSubscription CreateSubscription(THandler handler);

	/// <summary>Adds one subscriber to the set.</summary>
	/// <param name="handler">The subscriber to add.</param>
	/// <remarks>
	/// An addition is ignored once the set is completed.
	/// </remarks>
	internal void Add(THandler? handler)
	{
		if (handler is null)
			return;

		lock (_syncRoot)
		{
			if (_isComplete)
				return;

			_subscriptions.Add(CreateSubscription(handler));
		}
	}

	/// <summary>Removes one subscriber from the set.</summary>
	/// <param name="handler">The subscriber to remove.</param>
	internal void Remove(THandler? handler)
	{
		if (handler is null)
			return;

		lock (_syncRoot)
		{
			for (int i = _subscriptions.Count - 1; i >= 0; i--)
			{
				if (!Equals(_subscriptions[i].Handler, handler))
					continue;

				_subscriptions[i].Dispose();
				_subscriptions.RemoveAt(i);

				break;
			}
		}
	}

	/// <summary>
	/// Completes the set for disposal: the set is sealed against further additions, every subscription is marked disposed, and its pending work
	/// is dropped so no further work is delivered to any subscriber.
	/// </summary>
	internal void Complete()
	{
		TSubscription[] subscriptions;

		lock (_syncRoot)
		{
			_isComplete = true;

			if (_subscriptions.Count == 0)
				return;

			subscriptions = [.. _subscriptions];
			_subscriptions.Clear();
		}

		for (int i = 0; i < subscriptions.Length; i++)
			subscriptions[i].Dispose();
	}

	/// <summary>Whether every subscription has drained its pending work; tests read it through <see cref="ServerMessageRouter.IsDrained"/>.</summary>
	internal bool IsIdle
	{
		get
		{
			TSubscription[] subscriptions = SnapshotSubscriptions();

			for (int i = 0; i < subscriptions.Length; i++)
			{
				if (!subscriptions[i].IsIdle)
					return false;
			}

			return true;
		}
	}

	/// <summary>Copies the current subscriber list so a caller can dispatch outside the lock.</summary>
	/// <returns>A snapshot of the current subscriptions; empty when none are registered, without allocating.</returns>
	internal TSubscription[] SnapshotSubscriptions()
	{
		lock (_syncRoot)
		{
			if (_subscriptions.Count == 0)
				return [];

			return [.. _subscriptions];
		}
	}
}
