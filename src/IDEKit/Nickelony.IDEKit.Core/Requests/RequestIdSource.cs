namespace Nickelony.IDEKit.Core.Requests;

/// <summary>
/// Owns the request-identifier rule shared by the request primitives: a monotonic counter whose
/// zero value (produced by the wrap after 2^64 requests) is skipped, so an uninitialized identifier
/// never matches a live request.
/// </summary>
internal static class RequestIdSource
{
	/// <summary>
	/// Advances the counter and returns a nonzero identifier.
	/// </summary>
	internal static long NextNonZero(ref long counter)
	{
		long requestId = Interlocked.Increment(ref counter);

		return requestId != 0 ? requestId : Interlocked.Increment(ref counter);
	}
}
