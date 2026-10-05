namespace Nickelony.LanguageServer.Provider;

/// <summary>Counts the consecutive request timeouts of the newest connection generation and decides when a timeout-driven restart is due.</summary>
/// <remarks>
/// <para>
/// A settlement from a superseded connection generation is ignored: only a generation at least as new as the tracked one may clear the streak
/// and the once-per-generation restart latch. A late success from a replaced connection cannot mask consecutive timeouts on the active one.
/// </para>
/// <para>
/// A timeout on a newer generation than the tracked one starts a streak for that generation. A restart is due once per generation, when the streak
/// reaches the threshold; a zero threshold disables the restart.
/// </para>
/// </remarks>
internal sealed class RequestTimeoutCounter
{
	private readonly object _syncRoot = new();

	private int _consecutiveTimeouts;

	// The connection generation the bookkeeping currently applies to; reset when a request settles on its generation or a new generation
	// finishes starting.
	private long _trackingGeneration = -1;

	private long _restartRequestedGeneration = -1;

	/// <param name="restartThreshold">
	/// The number of consecutive timeouts on one generation that makes a restart due, or <c>0</c> to disable timeout-driven restarts.
	/// </param>
	internal RequestTimeoutCounter(int restartThreshold) => RestartThreshold = restartThreshold;

	/// <summary>The number of consecutive timeouts on one generation that makes a restart due, or <c>0</c> when restarts are disabled.</summary>
	internal int RestartThreshold { get; }

	/// <summary>The connection generation the bookkeeping applies to, or <c>-1</c> before the first settlement or timeout.</summary>
	/// <remarks>
	/// A test reads this state; production code never branches on it.
	/// </remarks>
	internal long TrackedGeneration
	{
		get
		{
			lock (_syncRoot)
				return _trackingGeneration;
		}
	}

	/// <summary>The consecutive timeouts counted for the tracked generation.</summary>
	/// <remarks>
	/// A test reads this state; production code never branches on it.
	/// </remarks>
	internal int ConsecutiveTimeouts
	{
		get
		{
			lock (_syncRoot)
				return _consecutiveTimeouts;
		}
	}

	/// <summary>
	/// Resets the bookkeeping after a request settled on its connection generation without timing out, or after a new connection generation finished
	/// starting successfully.
	/// </summary>
	/// <param name="connectionGeneration">The connection generation that settled without timing out or finished starting.</param>
	internal void Reset(long connectionGeneration)
	{
		lock (_syncRoot)
		{
			if (connectionGeneration < _trackingGeneration)
				return;

			_trackingGeneration = connectionGeneration;
			_consecutiveTimeouts = 0;
			_restartRequestedGeneration = -1;
		}
	}

	/// <summary>Records one request timeout for its connection generation.</summary>
	/// <param name="connectionGeneration">The connection generation the timed-out attempt observed.</param>
	/// <returns>The consecutive timeouts on that generation, and whether a restart is due now, which is at most once per generation.</returns>
	internal (int ConsecutiveTimeouts, bool RestartDue) RecordTimeout(long connectionGeneration)
	{
		lock (_syncRoot)
		{
			if (_trackingGeneration != connectionGeneration)
			{
				_trackingGeneration = connectionGeneration;
				_consecutiveTimeouts = 0;
				_restartRequestedGeneration = -1;
			}

			int count = ++_consecutiveTimeouts;
			bool restartDue = false;

			if (RestartThreshold > 0 && count >= RestartThreshold && _restartRequestedGeneration != connectionGeneration)
			{
				_restartRequestedGeneration = connectionGeneration;
				restartDue = true;
			}

			return (count, restartDue);
		}
	}
}
