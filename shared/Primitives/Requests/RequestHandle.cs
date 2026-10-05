#if QUICKPICKFAMILY
namespace Nickelony.QuickPick;

#else
namespace Nickelony.IDEKit.Core.Requests;

#endif

/// <summary>
/// Pairs the identifier of an admitted request with that request's cancellation token, so admission, cancellation, and publication checks all name
/// one request.
/// </summary>
/// <remarks>
/// A <see cref="LatestRequestGate"/> mints the handle atomically with the admission, so the identifier and the token it carries always belong
/// to the same request. <see cref="None"/> carries identifier zero, which the gate never mints, so a rejected admission cannot be mistaken
/// for an admitted request.
/// </remarks>
#if QUICKPICKFAMILY
internal
#else
public
#endif
readonly record struct RequestHandle
{
	internal RequestHandle(long requestId, CancellationToken cancellationToken)
	{
		RequestId = requestId;
		CancellationToken = cancellationToken;
	}

	/// <summary>The handle of no request: identifier zero paired with <see cref="CancellationToken.None"/>.</summary>
	public static RequestHandle None { get; } = new(0, CancellationToken.None);

	/// <summary>The request's identifier: nonzero for an admitted request and zero for <see cref="None"/>.</summary>
	public long RequestId { get; }

	/// <summary>The request's cancellation token, or <see cref="CancellationToken.None"/> for <see cref="None"/>.</summary>
	public CancellationToken CancellationToken { get; }
}
