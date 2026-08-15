namespace Nickelony.IDEKit.Core.Requests;

/// <summary>
/// Describes how an admitted asynchronous request completed.
/// </summary>
/// <remarks>
/// <para>
/// Every admitted request that reaches its classification point (including cancellation and
/// supersession) reports exactly one member. A run whose compute delegate fails with any other
/// exception propagates the exception instead of reporting an outcome. The two comparable members
/// are <see cref="Superseded"/> (a newer request or an invalidation replaced it before
/// its result could be published) and <see cref="RejectedByCurrentState"/> (it is still the latest
/// request, but the caller's current-state predicate rejected the result).
/// </para>
/// <para>
/// <see cref="Unknown"/> is the first member, so <c>default(RequestOutcome)</c> does not read as a
/// completed request; a request run never reports it, so treat every other member as meaningful
/// only when it was returned by a run.
/// </para>
/// </remarks>
public enum RequestOutcome
{
	/// <summary>
	/// The outcome was not produced by a request run. This is the default value of the enum, so a
	/// default-initialized member reports it instead of an accidental success.
	/// </summary>
	Unknown = 0,

	/// <summary>
	/// The request completed and its result was published.
	/// </summary>
	Completed = 1,

	/// <summary>
	/// Cancellation was observed at a checkpoint before the result could be published, so the compute
	/// delegate's result (if any) was discarded.
	/// </summary>
	/// <remarks>
	/// This covers a token that was already canceled when the run was admitted, a delegate that threw
	/// <see cref="OperationCanceledException"/>, and a delegate that returned after cancellation was
	/// requested. Cancellation is observed at the run's checkpoints (before the compute delegate runs,
	/// after it returns or throws, and before publication), so a cancellation that arrives while the
	/// publication callbacks run does not preempt publication and the run still reports
	/// <see cref="Completed"/>. The delegate is not still running when this outcome is observed.
	/// </remarks>
	Canceled = 2,

	/// <summary>
	/// The request was replaced by a newer request or an owner invalidation before its result could
	/// be published; its compute delegate may have been canceled first or may never have started.
	/// Compare with <see cref="RejectedByCurrentState"/>, which means the request is still the latest
	/// but its result was rejected by the current-state predicate.
	/// </summary>
	Superseded = 3,

	/// <summary>
	/// The request's compute delegate completed and the request is still the latest, but the host's
	/// <c>canApply</c> predicate rejected the result.
	/// </summary>
	/// <remarks>
	/// A host typically observes this when its state moved on while the work was in flight (for
	/// example a version or generation the predicate compares). Compare with
	/// <see cref="Superseded"/>, which means a newer request or an owner invalidation
	/// replaced this one.
	/// </remarks>
	RejectedByCurrentState = 4
}
