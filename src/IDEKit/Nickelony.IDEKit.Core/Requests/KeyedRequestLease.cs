namespace Nickelony.IDEKit.Core.Requests;

/// <summary>Holds the slot of one request in a <see cref="KeyedLatestRequestGate{TKey}"/> until the request settles.</summary>
/// <typeparam name="TKey">The key type of the gate.</typeparam>
/// <remarks>
/// Disposing the lease releases the slot and the token source of the request, unless a newer request for the same key
/// already replaced it. The default lease holds nothing, and disposing it does nothing.
/// </remarks>
public readonly struct KeyedRequestLease<TKey> : IDisposable
	where TKey : notnull
{
	private readonly KeyedLatestRequestGate<TKey>? _gate;
	private readonly TKey _key;
	private readonly CancellationTokenSource? _source;

	internal KeyedRequestLease(KeyedLatestRequestGate<TKey> gate, TKey key, CancellationTokenSource? source, CancellationToken cancellationToken)
	{
		_gate = gate;
		_key = key;
		_source = source;
		CancellationToken = cancellationToken;
	}

	/// <summary>The token that a newer request, a cancellation of the key, the close of the gate or the caller's token cancels.</summary>
	public CancellationToken CancellationToken { get; }

	/// <summary>Releases the slot the request held.</summary>
	public void Dispose()
	{
		if (_gate is not null && _source is not null)
			_gate.Settle(_key, _source);
	}
}
