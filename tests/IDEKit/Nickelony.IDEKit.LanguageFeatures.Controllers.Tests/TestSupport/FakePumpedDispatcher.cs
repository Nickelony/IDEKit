using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.Core.Validation;
using Nickelony.Testing;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// An <see cref="IUiDispatcher"/> for the thread that created it, whose queued work and timers run only when a test pumps it, so a controller test
/// runs without a UI toolkit.
/// </summary>
/// <remarks>
/// <para>
/// It is the <see cref="IUiDispatcher"/> facade of <see cref="FakePumpedUiThread"/>, which owns the queue, the virtual clock
/// and the synchronization context. The facade adds what the interface asks for: the four priorities of <see cref="UiDispatcherPriority"/>,
/// the owner-thread checks, and <see cref="Schedule"/>, which rejects a negative delay where a timer of a <see cref="System.TimeProvider"/>
/// never comes due.
/// </para>
/// <para>
/// On the owner thread, <see cref="Invoke(Action)"/> and <see cref="InvokeAsync"/> run inline, as the engine adapters do. From another thread
/// they queue the call and block, or complete, when the owner pumps; a blocking call outranks normal work, like the engines' send priority.
/// <see cref="Post"/> always queues. Queued work runs by priority, then by arrival.
/// </para>
/// <para>
/// A scheduled callback waits on a virtual clock that only <see cref="Advance"/> moves, so a debounce costs no wall time and a test states
/// how much time passed, including that a timer must not have fired yet. A timer that comes due joins the queue at background priority, and a handle
/// disposed after that still suppresses the tick, as the engine's timer does.
/// </para>
/// <para>
/// The constructor either installs the dispatcher as the thread's <see cref="SynchronizationContext"/>,
/// which is the production shape because a continuation that captured it queues here, or clears the thread's context, so a continuation
/// resumes on the thread pool whatever the test runner installed. A test that installs it pumps with <see cref="PumpUntil"/> and never awaits
/// a pending task, because the continuation of a suspended await runs only when the owner pumps. <see cref="Dispose"/> restores the previous context,
/// because the test host reuses its threads, and then cancels the calls that wait for the owner and rejects later ones the way a dispatcher
/// shutdown does.
/// </para>
/// </remarks>
internal sealed class FakePumpedDispatcher : IUiDispatcher, IDisposable
{
	private readonly FakePumpedUiThread _thread;

	/// <param name="installSynchronizationContext">
	/// <see langword="true"/> to make the dispatcher the synchronization context of the creating thread until it is disposed;
	/// <see langword="false"/> to clear the thread's synchronization context for the same time.
	/// </param>
	public FakePumpedDispatcher(bool installSynchronizationContext) => _thread = new FakePumpedUiThread(installSynchronizationContext);

	/// <summary>The managed thread identifier of the owner thread.</summary>
	public int OwnerThreadId => _thread.OwnerThreadId;

	/// <summary>The number of queued items that have not run.</summary>
	public int QueuedCount => _thread.QueuedCount;

	/// <summary>The number of scheduled callbacks that were neither canceled nor due.</summary>
	public int PendingTimerCount => _thread.PendingTimerCount;

	/// <summary>The virtual clock reading.</summary>
	public TimeSpan Now => _thread.Now;

	/// <inheritdoc/>
	public bool CheckAccess() => _thread.CheckAccess();

	/// <inheritdoc/>
	public void VerifyAccess()
	{
		if (!CheckAccess())
			throw new InvalidOperationException("The calling thread cannot access this dispatcher because a different thread owns it.");
	}

	/// <inheritdoc/>
	public void Invoke(Action callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		_thread.Send(callback);
	}

	/// <inheritdoc/>
	public TResult Invoke<TResult>(Func<TResult> callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		return _thread.Send(callback);
	}

	/// <inheritdoc/>
	public Task InvokeAsync(Action callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		return _thread.SendAsync(callback);
	}

	/// <inheritdoc/>
	public void Post(Action callback, UiDispatcherPriority priority = UiDispatcherPriority.Normal)
	{
		ArgumentNullException.ThrowIfNull(callback);
		_thread.Post(callback, RankOf(priority));
	}

	/// <inheritdoc/>
	public IDisposable Schedule(TimeSpan delay, Action callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		return _thread.Schedule(NumericValidation.NonNegative(delay, nameof(delay)), callback);
	}

	/// <inheritdoc cref="FakePumpedUiThread.PumpUntilIdle"/>
	public void PumpUntilIdle() => _thread.PumpUntilIdle();

	/// <inheritdoc cref="FakePumpedUiThread.PumpUntil"/>
	public void PumpUntil(Func<bool> condition, TimeSpan? timeout = null) => _thread.PumpUntil(condition, timeout);

	/// <inheritdoc cref="FakePumpedUiThread.Advance"/>
	public void Advance(TimeSpan duration) => _thread.Advance(duration);

	/// <inheritdoc cref="FakePumpedUiThread.Dispose"/>
	public void Dispose() => _thread.Dispose();

	private static int RankOf(UiDispatcherPriority priority)
	{
		return priority switch
		{
			UiDispatcherPriority.Normal => FakePumpedUiThread.NormalRank,
			UiDispatcherPriority.Render => FakePumpedUiThread.RenderRank,
			UiDispatcherPriority.Background => FakePumpedUiThread.BackgroundRank,
			_ => FakePumpedUiThread.IdleRank,
		};
	}
}
