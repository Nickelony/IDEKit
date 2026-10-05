using Nickelony.IDEKit.Core.Notifications;

namespace Nickelony.IDEKit.Core.LineStatus;

/// <summary>Owns the change-source subscription and the coalesced visual-invalidation state machine of a line-status margin.</summary>
/// <remarks>
/// The editor bindings share it across engines: a margin keeps its text-view wiring, measuring and painting, and this type owns the source
/// subscription and the connection stamp that discards stale invalidations. The engine-specific parts are supplied as delegates.
/// </remarks>
public sealed class LineStatusInvalidationGate
{
	private readonly Func<bool> _isConnected;
	private readonly Func<bool> _canPost;

	private readonly Action<Action> _post;
	private readonly Action _invalidate;

	private IChangeNotificationSource? _source;

	private int _textViewConnectionVersion;
	private int _invalidationQueued;

	/// <param name="isConnected">A predicate that reports whether the margin is connected to a text view.</param>
	/// <param name="invalidate">The callback that invalidates the margin's visual.</param>
	/// <param name="post">The callback that posts an action to the engine dispatcher at render priority.</param>
	/// <param name="canPost">
	/// An optional predicate that reports whether a post can still run; a binding whose dispatcher exposes a shutdown probe
	/// passes it here, while a dispatcher that exposes none keeps the default (always true).
	/// </param>
	public LineStatusInvalidationGate(Func<bool> isConnected, Action invalidate, Action<Action> post, Func<bool>? canPost = null)
	{
		ArgumentNullException.ThrowIfNull(isConnected);
		ArgumentNullException.ThrowIfNull(invalidate);
		ArgumentNullException.ThrowIfNull(post);

		_isConnected = isConnected;
		_invalidate = invalidate;
		_post = post;
		_canPost = canPost ?? (static () => true);
	}

	/// <summary>Indicates whether the margin is currently subscribed to the notifying source's changes.</summary>
	public bool IsSourceSubscribed { get; private set; }

	/// <summary>Indicates whether a coalesced invalidation is posted and has not run yet.</summary>
	public bool IsInvalidationQueued => Volatile.Read(ref _invalidationQueued) != 0;

	/// <summary>Assigns the notifying source that drives the margin's invalidations.</summary>
	/// <param name="source">The source to follow.</param>
	/// <exception cref="InvalidOperationException">A source is already assigned.</exception>
	public void SetSource(IChangeNotificationSource source)
	{
		ArgumentNullException.ThrowIfNull(source);

		if (_source is not null)
			throw new InvalidOperationException("A notifying source is already assigned.");

		_source = source;

		// A margin that is already connected when the source is registered subscribes right away;
		// without this the registration would silently never subscribe.
		UpdateSourceSubscription(_isConnected());
	}

	/// <summary>Advances the connection stamp so a queued invalidation for the previous connection is discarded.</summary>
	public void BumpConnectionVersion() => Interlocked.Increment(ref _textViewConnectionVersion);

	/// <summary>Aligns the source subscription with the margin's connection state.</summary>
	/// <param name="connected">Whether the margin is connected to a text view.</param>
	public void UpdateSubscription(bool connected) => UpdateSourceSubscription(connected);

	/// <summary>Queues a coalesced visual invalidation, releasing the gate when the queued action runs.</summary>
	public void QueueInvalidation()
	{
		if (!_canPost())
			return;

		if (Interlocked.Exchange(ref _invalidationQueued, 1) != 0)
			return;

		int connectionVersion = Volatile.Read(ref _textViewConnectionVersion);

		try
		{
			_post(() =>
			{
				Volatile.Write(ref _invalidationQueued, 0);

				if (connectionVersion == Volatile.Read(ref _textViewConnectionVersion) && _isConnected())
					_invalidate();
			});
		}
		catch (InvalidOperationException)
		{
			// The dispatcher can begin shutting down between the check above and the post; the queued action
			// never runs then, so the gate must be released here or every later invalidation is suppressed.
			Volatile.Write(ref _invalidationQueued, 0);
		}
	}

	private void UpdateSourceSubscription(bool connected)
	{
		if (_source is null)
			return;

		if (connected == IsSourceSubscribed)
			return;

		IsSourceSubscribed = connected;

		if (connected)
			_source.Changed += HandleSourceChanged;
		else
			_source.Changed -= HandleSourceChanged;
	}

	private void HandleSourceChanged(object? sender, EventArgs e) => QueueInvalidation();
}
