namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Provides the subscriber list management shared by the serialized subscriber sets: one lock guards the subscriber
/// list so addition and removal are atomic with respect to dispatch, and completion is terminal so a later addition
/// cannot re-enable delivery during teardown.
/// </summary>
/// <typeparam name="THandler">The subscriber delegate type.</typeparam>
/// <typeparam name="TSubscription">The per-subscriber drain type this set stores.</typeparam>
internal abstract class SerializedSubscriberSet<THandler, TSubscription>
	where THandler : Delegate
	where TSubscription : SerializedSubscriberDrain<THandler>
{
	private readonly object _syncRoot = new();
	private readonly List<TSubscription> _subscriptions = [];
	private bool _isComplete;

	/// <summary>
	/// Initializes a new instance of the <see cref="SerializedSubscriberSet{THandler, TSubscription}"/> class.
	/// </summary>
	/// <param name="onProgress">
	/// The callback each subscription invokes as it advances toward its idle state, or <see langword="null"/> when
	/// no quiescence waiter is registered.
	/// </param>
	protected SerializedSubscriberSet(Action? onProgress = null)
	{
		OnProgress = onProgress;
	}

	/// <summary>
	/// Gets the callback each subscription invokes as it advances toward its idle state, or <see langword="null"/>.
	/// </summary>
	protected Action? OnProgress { get; }

	/// <summary>
	/// Creates the per-subscriber drain for one handler.
	/// </summary>
	/// <param name="handler">The subscriber to wrap.</param>
	/// <returns>The new subscription.</returns>
	protected abstract TSubscription CreateSubscription(THandler handler);

	/// <summary>
	/// Adds one subscriber to the set.
	/// </summary>
	/// <param name="handler">The subscriber to add.</param>
	/// <remarks>An addition is ignored once the set is completed.</remarks>
	public void Add(THandler? handler)
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

	/// <summary>
	/// Removes one subscriber from the set.
	/// </summary>
	/// <param name="handler">The subscriber to remove.</param>
	public void Remove(THandler? handler)
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
	/// Completes the set for disposal: the set is sealed against further additions, every subscription is marked
	/// disposed, and its pending work is dropped so no further work is delivered to any subscriber.
	/// </summary>
	public void Complete()
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

	/// <summary>
	/// Gets a value indicating whether every subscription has drained its pending work, so a caller can observe a
	/// quiescence point instead of waiting out a time window.
	/// </summary>
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

	/// <summary>
	/// Copies the current subscriber list so a caller can dispatch outside the lock.
	/// </summary>
	/// <returns>A snapshot of the current subscriptions; empty when none are registered, without allocating.</returns>
	protected TSubscription[] SnapshotSubscriptions()
	{
		lock (_syncRoot)
		{
			if (_subscriptions.Count == 0)
				return [];

			return [.. _subscriptions];
		}
	}
}
