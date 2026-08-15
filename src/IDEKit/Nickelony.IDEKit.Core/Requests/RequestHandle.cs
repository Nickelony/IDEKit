namespace Nickelony.IDEKit.Core.Requests;

/// <summary>
/// Pairs the identifier of an admitted request with that request's cancellation token, so admission,
/// cancellation, and publication checks all name one request.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="LatestRequestCoordinator"/> mints the handle atomically with the admission, so the
/// identifier and the token it carries always belong to the same request. Keeping the pair together
/// is what makes a manual request pipeline safe: reading a "latest token" accessor after beginning a
/// request can observe a newer request's token when an admission races the read, while a handle
/// cannot drift away from the request it was minted for.
/// </para>
/// <para>
/// <see cref="None"/> is the handle of no request. Its identifier is zero, which the coordinator
/// never mints, so no check accepts it and a rejected admission cannot be mistaken for an admitted
/// request.
/// </para>
/// </remarks>
public readonly record struct RequestHandle
{
	internal RequestHandle(long requestId, CancellationToken cancellationToken)
	{
		RequestId = requestId;
		CancellationToken = cancellationToken;
	}

	/// <summary>
	/// Gets the handle of no request: identifier zero paired with
	/// <see cref="CancellationToken.None"/>.
	/// </summary>
	public static RequestHandle None { get; } = new(0, CancellationToken.None);

	/// <summary>
	/// Gets the nonzero identifier of the request, or zero for <see cref="None"/>.
	/// </summary>
	public long RequestId { get; }

	/// <summary>
	/// Gets the request's cancellation token, or <see cref="CancellationToken.None"/> for
	/// <see cref="None"/>.
	/// </summary>
	public CancellationToken CancellationToken { get; }
}
