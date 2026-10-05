using System.Diagnostics;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Keeps the order of the host's calls on a document while they wait for the connection, until each call joins the document's lane.
/// </summary>
/// <param name="syncRoot">
/// The scheduler's lock, which guards the reservations together with the operation lanes and the latest-update registrations.
/// </param>
/// <param name="pathComparison">The policy that decides which paths are one document.</param>
/// <remarks>
/// <para>
/// A call reserves its place when the host makes it, before the call waits for the connection, so the reservations of a path are the order
/// of the host's calls. A call joins the lane only after every reservation made before it is complete, and completes its own once the lane keeps
/// the order the call needs: an update completes when it has joined the lane, so a later update can still supersede it, and any other call completes
/// when its operation has finished.
/// </para>
/// <para>
/// A reservation is complete for the calls behind it only when the reservations before it are complete too, so a call that ends early never lets
/// a later call overtake an earlier one. A path has an entry while it holds an incomplete reservation, so an idle tracker holds nothing.
/// </para>
/// <para>
/// The members that change the table require the caller to hold the scheduler's lock, except <see cref="CallReservation.Complete"/>,
/// which takes it itself.
/// </para>
/// </remarks>
internal sealed class CallOrderTracker(object syncRoot, LocalPathComparisonPolicy pathComparison)
{
	private readonly Dictionary<string, PathCalls> _paths = new(pathComparison.Comparer);

	/// <summary>The number of document paths that hold an incomplete reservation.</summary>
	/// <remarks>
	/// A test reads it to see that a finished call leaves no entry; production code never branches on it.
	/// </remarks>
	internal int ReservedPathCount
	{
		get
		{
			lock (syncRoot)
				return _paths.Count;
		}
	}

	/// <summary>Reserves the next place of a call on one or two document paths.</summary>
	/// <param name="normalizedFilePaths">The distinct normalized document paths the call affects.</param>
	/// <returns>The reservation, which waits for the reservations made before it on any of the paths.</returns>
	/// <remarks>
	/// The caller must hold the scheduler's lock. The reservation records the cancellation epoch of its first path,
	/// which <see cref="IsSuperseded"/> compares, so an update reserves one path.
	/// </remarks>
	internal CallReservation Reserve(string[] normalizedFilePaths)
	{
		AssertLockHeld();

		var reservation = new CallReservation(this, normalizedFilePaths);
		var predecessors = new List<Task>(normalizedFilePaths.Length);

		int epoch = 0;

		for (int i = 0; i < normalizedFilePaths.Length; i++)
		{
			if (!_paths.TryGetValue(normalizedFilePaths[i], out PathCalls? calls))
			{
				calls = new PathCalls();
				_paths[normalizedFilePaths[i]] = calls;
			}

			if (calls.Newest is { } previous && !previous.Done.IsCompleted)
				predecessors.Add(previous.Done);

			if (i == 0)
				epoch = calls.CancelEpoch;

			calls.Newest = reservation;
			calls.Incomplete++;
		}

		reservation.Begin(predecessors, epoch);

		return reservation;
	}

	/// <summary>Supersedes the updates of a document path that reserved their place and have not joined the lane.</summary>
	/// <param name="normalizedFilePath">The normalized document path.</param>
	/// <remarks>
	/// The caller must hold the scheduler's lock. A path with no incomplete reservation has nothing to supersede.
	/// </remarks>
	internal void Supersede(string normalizedFilePath)
	{
		AssertLockHeld();

		if (_paths.TryGetValue(normalizedFilePath, out PathCalls? calls))
			calls.CancelEpoch++;
	}

	/// <summary>Supersedes the updates of every document path that reserved their place and have not joined the lane.</summary>
	/// <remarks>
	/// The caller must hold the scheduler's lock.
	/// </remarks>
	internal void SupersedeAll()
	{
		AssertLockHeld();

		foreach (PathCalls calls in _paths.Values)
			calls.CancelEpoch++;
	}

	/// <summary>Tells whether a reservation was superseded after it was made.</summary>
	/// <param name="reservation">The reservation of an update.</param>
	/// <returns><see langword="true"/> when its path was superseded since the reservation; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The caller must hold the scheduler's lock, so the answer and the update's join of the lane cannot be separated by a supersede.
	/// </remarks>
	internal bool IsSuperseded(CallReservation reservation)
	{
		AssertLockHeld();
		return _paths.TryGetValue(reservation.Paths[0], out PathCalls? calls) && calls.CancelEpoch != reservation.Epoch;
	}

	/// <summary>Removes a completed reservation from the paths it held, and removes the path once no incomplete reservation is left.</summary>
	/// <param name="reservation">The reservation that completed.</param>
	private void Release(CallReservation reservation)
	{
		lock (syncRoot)
		{
			foreach (string normalizedFilePath in reservation.Paths)
			{
				if (_paths.TryGetValue(normalizedFilePath, out PathCalls? calls) && --calls.Incomplete == 0)
					_paths.Remove(normalizedFilePath);
			}
		}
	}

	[Conditional("DEBUG")]
	private void AssertLockHeld() => Debug.Assert(Monitor.IsEntered(syncRoot), "The scheduler lock must be held.");

	/// <summary>The reservations of one document path.</summary>
	private sealed class PathCalls
	{
		/// <summary>The reservation made last, which the next reservation waits behind.</summary>
		internal CallReservation? Newest { get; set; }

		/// <summary>The number of reservations that are not complete.</summary>
		internal int Incomplete { get; set; }

		/// <summary>The count of the supersedes of this path, which an update reservation compares with the count it saw.</summary>
		internal int CancelEpoch { get; set; }
	}

	/// <summary>The place of one call among the calls on its document paths.</summary>
	internal sealed class CallReservation
	{
		private readonly CallOrderTracker _owner;
		private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

		private Task[] _predecessors = [];
		private int _isComplete;

		internal CallReservation(CallOrderTracker owner, string[] normalizedFilePaths)
		{
			_owner = owner;
			Paths = normalizedFilePaths;
			Done = _completion.Task;
		}

		/// <summary>The normalized document paths the call affects.</summary>
		internal string[] Paths { get; }

		/// <summary>The supersede count of the first path when the reservation was made.</summary>
		internal int Epoch { get; private set; }

		/// <summary>A task that completes when this reservation and every reservation made before it on its paths are complete.</summary>
		internal Task Done { get; private set; }

		/// <summary>Fixes the reservations this one waits behind and the supersede count it saw, once every path has been read.</summary>
		/// <param name="predecessors">The completion tasks of the reservations made before this one that are not complete.</param>
		/// <param name="epoch">The supersede count of the first path.</param>
		internal void Begin(List<Task> predecessors, int epoch)
		{
			Epoch = epoch;
			_predecessors = [.. predecessors];

			if (_predecessors.Length > 0)
				Done = Task.WhenAll([.. predecessors, _completion.Task]);
		}

		/// <summary>Waits until every reservation made before this one is complete.</summary>
		/// <param name="cancellationToken">A token that can cancel the wait.</param>
		/// <returns>A task that completes when the call may join the lane, and that is already complete when no earlier call is pending.</returns>
		/// <exception cref="OperationCanceledException">The wait was canceled before the earlier calls completed.</exception>
		internal Task WaitForTurnAsync(CancellationToken cancellationToken)
		{
			foreach (Task predecessor in _predecessors)
			{
				if (!predecessor.IsCompleted)
					return Task.WhenAll(_predecessors).WaitAsync(cancellationToken);
			}

			return Task.CompletedTask;
		}

		/// <summary>Completes the reservation, so the calls behind it may join the lane once the calls before it are complete as well.</summary>
		/// <remarks>
		/// The call is idempotent and takes the scheduler's lock itself. Every call completes its reservation on every exit path.
		/// </remarks>
		internal void Complete()
		{
			if (Interlocked.Exchange(ref _isComplete, 1) != 0)
				return;

			_owner.Release(this);
			_completion.TrySetResult();
		}
	}
}
