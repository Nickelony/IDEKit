#if QUICKPICKFAMILY
namespace Nickelony.QuickPick;

#else
namespace Nickelony.IDEKit.Core.Requests;

#endif

/// <summary>Describes how an admitted asynchronous request completed.</summary>
/// <remarks>
/// Every admitted request that reaches its classification point reports exactly one member. An exception other than the compute delegate's
/// <see cref="OperationCanceledException"/> propagates out of the run instead of being reported as an outcome.
/// </remarks>
#if QUICKPICKFAMILY
internal
#else
public
#endif
enum RequestOutcome
{
	/// <summary>The outcome was not produced by a request run.</summary>
	/// <remarks>
	/// It is the enum's default value, so a default-initialized member reports it instead of an accidental success.
	/// </remarks>
	Unknown = 0,

	/// <summary>The request completed and its result was published.</summary>
	Completed = 1,

	/// <summary>
	/// Cancellation was observed at a checkpoint before the result could be published, so the compute delegate's result (if any) was discarded.
	/// </summary>
	/// <remarks>
	/// Covers a token already canceled at admission, a delegate that threw <see cref="OperationCanceledException"/>, and a delegate
	/// that returned after cancellation was requested. A run whose admission predicate rejected it also reports this member and runs no delegate.
	/// Cancellation is observed only at the run's checkpoints, so one that arrives while the publication callbacks run does not preempt publication
	/// and the run still reports <see cref="Completed"/>. The delegate is not still running when this is observed.
	/// </remarks>
	Canceled = 2,

	/// <summary>
	/// The request was replaced by a newer request or an owner invalidation before its result could be published; its compute delegate
	/// may have been canceled first or may never have started.
	/// </summary>
	Superseded = 3,

	/// <summary>
	/// The request's compute delegate completed and the request is still the latest, but the host's <c>canApply</c> predicate rejected the result.
	/// </summary>
	/// <remarks>
	/// A host observes this when its state moved on while the work was in flight, for example a version the predicate compares.
	/// </remarks>
	RejectedByCurrentState = 4,
}
