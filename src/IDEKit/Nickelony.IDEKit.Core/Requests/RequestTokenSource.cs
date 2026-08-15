namespace Nickelony.IDEKit.Core.Requests;

/// <summary>
/// Provides request tokens used to identify the latest asynchronous result and invalidate stale
/// ones.
/// </summary>
/// <remarks>
/// <para>
/// This is the lightweight option for callers that validate the token themselves and do not await
/// the work. Use <see cref="LatestRequestCoordinator"/> when Core should await the work, apply a
/// current-state predicate, and publish the result.
/// </para>
/// <para>
/// All members are safe for concurrent use.
/// </para>
/// </remarks>
public sealed class RequestTokenSource
{
	private long _currentToken;

	/// <summary>
	/// Begins a new request and returns its token. Earlier tokens are no longer current.
	/// </summary>
	/// <returns>A nonzero token identifying the new request.</returns>
	public long BeginRequest() => RequestIdSource.NextNonZero(ref _currentToken);

	/// <summary>
	/// Invalidates all outstanding requests so their tokens are no longer current.
	/// </summary>
	/// <remarks>
	/// The advance goes through the same nonzero-counter rule as <see cref="BeginRequest"/> - the one
	/// place that owns the rule - so an invalidation can never hand out the identifier zero either.
	/// </remarks>
	public void Invalidate() => RequestIdSource.NextNonZero(ref _currentToken);

	/// <summary>
	/// Determines whether the supplied token belongs to the most recent request.
	/// </summary>
	/// <param name="token">The request token to inspect.</param>
	/// <returns>
	/// <see langword="true"/> when <paramref name="token"/> is current; otherwise, <see langword="false"/>.
	/// The default token value zero is never current, so an uninitialized token cannot pass the check.
	/// </returns>
	public bool IsCurrent(long token)
		=> token != 0 && token == Volatile.Read(ref _currentToken);
}
