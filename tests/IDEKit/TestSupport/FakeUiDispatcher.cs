using Nickelony.IDEKit.Core.Threading;

namespace Nickelony.IDEKit.Testing;

/// <summary>A dispatcher on the test thread: it runs posted work inline and keeps scheduled callbacks until a test runs them.</summary>
/// <remarks>
/// <para>
/// It stays apart from <see cref="FakePumpedDispatcher"/>, which queues posted work and fires timers on a virtual clock.
/// The tests that use this double assert the delay that each <see cref="Schedule"/> call requested, make <see cref="Schedule"/> fail
/// as a shutting-down dispatcher does, and run a pending callback whatever its delay, none of which the pumped dispatcher records or allows.
/// The components under those tests reach <see cref="Schedule"/> only, or run inline work whose order no test pins.
/// </para>
/// <para>
/// A callback whose handle was disposed does not run, unless <see cref="RunsCanceledCallbacks"/> stands for a timer that had already fired.
/// </para>
/// </remarks>
internal sealed class FakeUiDispatcher : IUiDispatcher
{
	private readonly List<ScheduledCallback> _scheduled = [];
	private readonly List<TimeSpan> _requestedDelays = [];

	/// <summary>The delay of every <see cref="Schedule"/> call, in call order.</summary>
	public IReadOnlyList<TimeSpan> RequestedDelays => _requestedDelays;

	/// <summary>The number of scheduled callbacks that were neither canceled nor run.</summary>
	public int PendingCount => _scheduled.Count(callback => callback.IsPending);

	/// <summary>An exception that <see cref="Schedule"/> throws instead of scheduling, or <see langword="null"/> to schedule.</summary>
	public Exception? ScheduleFailure { get; set; }

	/// <summary>Whether a canceled callback still runs, as a timer that already fired does.</summary>
	public bool RunsCanceledCallbacks { get; set; }

	public bool CheckAccess() => true;

	public void VerifyAccess() { }

	public void Invoke(Action callback) => callback();

	public TResult Invoke<TResult>(Func<TResult> callback) => callback();

	public Task InvokeAsync(Action callback)
	{
		callback();
		return Task.CompletedTask;
	}

	public void Post(Action callback, UiDispatcherPriority priority = UiDispatcherPriority.Normal) => callback();

	public IDisposable Schedule(TimeSpan delay, Action callback)
	{
		if (ScheduleFailure is not null)
			throw ScheduleFailure;

		_requestedDelays.Add(delay);

		var scheduled = new ScheduledCallback(callback, this);
		_scheduled.Add(scheduled);

		return scheduled;
	}

	/// <summary>Runs every pending scheduled callback once, in the order it was scheduled.</summary>
	/// <remarks>
	/// A callback that schedules another callback while it runs leaves that one pending.
	/// </remarks>
	public void RunPending()
	{
		foreach (ScheduledCallback scheduled in _scheduled.ToArray())
			scheduled.Run();
	}

	private sealed class ScheduledCallback(Action callback, FakeUiDispatcher owner) : IDisposable
	{
		private bool _isCanceled;
		private bool _hasRun;

		public bool IsPending => !_isCanceled && !_hasRun;

		public void Run()
		{
			if (_hasRun || (_isCanceled && !owner.RunsCanceledCallbacks))
				return;

			_hasRun = true;
			callback();
		}

		public void Dispose() => _isCanceled = true;
	}
}
