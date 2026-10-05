using Nickelony.IDEKit.Core.Threading;

namespace Nickelony.IDEKit.Core.Requests;

/// <summary>Keeps the newest request per key: a new request for a key cancels the previous one, and a close rejects every later request.</summary>
/// <typeparam name="TKey">The key that identifies a request slot. The comparer given to the gate decides which keys share a slot.</typeparam>
/// <remarks>
/// <para>
/// <see cref="Begin"/> admits a request and returns the lease that holds its slot until the request settles. A superseded request is canceled
/// outside the gate's lock, after the newer request is published, so a cancellation callback never runs under the lock.
/// </para>
/// <para>
/// <see cref="CancelAll"/> closes the gate and cancels every request. Closing never waits for a request in flight, so it can be requested
/// from inside one. A request that begins after the close receives a lease whose token is already canceled and holds no slot.
/// </para>
/// </remarks>
public sealed class KeyedLatestRequestGate<TKey>
	where TKey : notnull
{
	// Admission and the slot map are read and written under this one lock: a request either supersedes the key's previous source or is
	// rejected once the gate closed, with no window in which a request can publish a source the close already passed.
	private readonly object _sync = new();
	private readonly Dictionary<TKey, CancellationTokenSource> _requests;

	// The gate closes through the stop of this admission and admits no operation: a close never waits for a request.
	private readonly StopGate<CancellationTokenSource> _admission;

	/// <summary>Initializes a gate with no request.</summary>
	/// <param name="comparer">Decides which keys share a request slot.</param>
	public KeyedLatestRequestGate(IEqualityComparer<TKey> comparer)
	{
		ArgumentNullException.ThrowIfNull(comparer);

		_requests = new Dictionary<TKey, CancellationTokenSource>(comparer);
		_admission = new StopGate<CancellationTokenSource>(_sync);
	}

	/// <summary>Begins a request for a key, superseding any previous request for that key.</summary>
	/// <param name="key">The key whose slot the request takes.</param>
	/// <param name="cancellationToken">The caller token that the request's token links against.</param>
	/// <returns>
	/// The lease that holds the slot until it is disposed. Its token is canceled by a newer request for the key, by <see cref="Cancel"/>,
	/// by <see cref="CancelAll"/> and by <paramref name="cancellationToken"/>, and it is already canceled when the gate closed.
	/// </returns>
	public KeyedRequestLease<TKey> Begin(TKey key, CancellationToken cancellationToken)
	{
		CancellationToken freshToken;
		CancellationTokenSource? freshSource;

		CancellationTokenSource? previousSource;

		lock (_sync)
		{
			if (_admission.IsStopping)
				return new KeyedRequestLease<TKey>(gate: this, key, source: null, cancellationToken: new CancellationToken(canceled: true));

			freshSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

			// The token is captured while the source is still private to this call: once it is in the map a
			// concurrent cancel may dispose it, and reading Token from a disposed source throws.
			freshToken = freshSource.Token;

			_requests.TryGetValue(key, out previousSource);
			_requests[key] = freshSource;
		}

		// The superseded source is canceled outside the lock and after the fresh source is published, so the
		// caller can always release what this call tracked.
		CancelAndDispose(previousSource);

		return new KeyedRequestLease<TKey>(gate: this, key, source: freshSource, cancellationToken: freshToken);
	}

	/// <summary>Cancels the request for a key, if any.</summary>
	/// <param name="key">The key whose request is canceled.</param>
	public void Cancel(TKey key)
	{
		CancellationTokenSource? source;

		lock (_sync)
		{
			if (!_requests.Remove(key, out source))
				return;
		}

		CancelAndDispose(source);
	}

	/// <summary>Closes the gate and cancels every request.</summary>
	/// <remarks>
	/// The gate closes before the cancellation, so a request that begins after the lock is released is rejected instead of publishing a source
	/// that this call already passed. The method is idempotent.
	/// </remarks>
	public void CancelAll()
	{
		CancellationTokenSource[] sources;

		lock (_sync)
		{
			if (_admission.TryBeginStop(out _, out _))
				_admission.RecordStopTask(Task.CompletedTask);

			if (_requests.Count == 0)
				return;

			sources = [.. _requests.Values];
			_requests.Clear();
		}

		foreach (CancellationTokenSource source in sources)
			CancelAndDispose(source);
	}

	/// <summary>Removes a settled request's own source from the map and disposes it.</summary>
	/// <param name="key">The key whose request settled.</param>
	/// <param name="source">The source that <see cref="Begin"/> published for the request.</param>
	internal void Settle(TKey key, CancellationTokenSource source)
	{
		lock (_sync)
		{
			// Remove only this request's own source: a newer request has already published a replacement, and
			// dropping that would strand it.
			if (_requests.TryGetValue(key, out CancellationTokenSource? current) && ReferenceEquals(current, source))
				_requests.Remove(key);
		}

		source.Dispose();
	}

	private static void CancelAndDispose(CancellationTokenSource? source)
	{
		if (source is null)
			return;

		try
		{
			source.Cancel();
		}
		catch (Exception exception) when (exception is ObjectDisposedException or AggregateException)
		{
			// A cancellation callback must not escape the gate's cleanup: AggregateException surfaces a throwing
			// callback, and ObjectDisposedException a source that its request already released.
		}

		source.Dispose();
	}
}
