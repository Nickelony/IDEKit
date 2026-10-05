using System.Diagnostics;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Holds the serialized operation lane of each normalized document path and the one lane that every exclusive operation joins.</summary>
/// <param name="syncRoot">The scheduler's lock, which guards the lanes together with the latest-update registrations.</param>
/// <param name="pathComparison">The policy that decides which paths share a lane.</param>
/// <remarks>
/// <para>
/// A document lane exists while it holds a queued or running node: the first node to join creates it and the last node
/// to settle removes it, so an idle scheduler holds no lane. A node waits for the tail its lanes held when it joined and then becomes their new tail.
/// </para>
/// <para>
/// The members that change the table require the caller to hold the scheduler's lock, except <see cref="Release"/>, which takes it itself.
/// </para>
/// </remarks>
internal sealed class OperationLaneTracker(object syncRoot, LocalPathComparisonPolicy pathComparison)
{
	private readonly Dictionary<string, OperationLane> _documentLanes = new(pathComparison.Comparer);
	private readonly OperationLane _exclusiveLane = new(normalizedFilePath: null);

	/// <summary>The number of document lanes that hold a queued or running node.</summary>
	/// <remarks>
	/// A test reads it to see that a drained lane is removed; production code never branches on it.
	/// </remarks>
	internal int DocumentLaneCount
	{
		get
		{
			lock (syncRoot)
				return _documentLanes.Count;
		}
	}

	/// <summary>Captures the lanes one operation node joins, creating the document lanes that do not exist yet.</summary>
	/// <param name="normalizedFilePaths">The distinct normalized document paths whose lanes the node joins.</param>
	/// <param name="joinsExclusiveLane">Whether the node also joins the lane that every exclusive operation shares.</param>
	/// <returns>The lanes the node joins, with the exclusive lane first when it is joined.</returns>
	internal OperationLane[] Capture(string[] normalizedFilePaths, bool joinsExclusiveLane)
	{
		AssertLockHeld();

		var lanes = new OperationLane[(joinsExclusiveLane ? 1 : 0) + normalizedFilePaths.Length];
		int index = 0;

		if (joinsExclusiveLane)
			lanes[index++] = _exclusiveLane;

		for (int i = 0; i < normalizedFilePaths.Length; i++)
			lanes[index++] = GetOrCreate(normalizedFilePaths[i]);

		return lanes;
	}

	/// <summary>Returns the operation lane for the supplied normalized document path, creating it on first use.</summary>
	/// <param name="normalizedFilePath">The normalized document path.</param>
	/// <returns>The lane that owns the document path.</returns>
	internal OperationLane GetOrCreate(string normalizedFilePath)
	{
		AssertLockHeld();

		if (!_documentLanes.TryGetValue(normalizedFilePath, out OperationLane? lane))
		{
			lane = new OperationLane(normalizedFilePath);
			_documentLanes[normalizedFilePath] = lane;
		}

		return lane;
	}

	/// <summary>Clears the tail of a lane when the settled node still owns it, and removes the lane once it drained.</summary>
	/// <param name="lane">The lane the node was queued on.</param>
	/// <param name="node">The node that settled.</param>
	internal void Complete(OperationLane lane, LaneNode node)
	{
		AssertLockHeld();

		if (ReferenceEquals(lane._tail, node))
			lane._tail = null;

		Prune(lane);
	}

	/// <summary>Clears the tails of the lanes a settled operation node was queued on and removes the lanes that drained.</summary>
	/// <param name="lanes">The lanes the node joined.</param>
	/// <param name="node">The node that settled.</param>
	internal void Release(OperationLane[] lanes, LaneNode node)
	{
		lock (syncRoot)
		{
			for (int i = 0; i < lanes.Length; i++)
				Complete(lanes[i], node);
		}
	}

	/// <summary>Removes a document lane once it has fully drained and is still the registered lane of its path.</summary>
	/// <param name="lane">The lane to remove once it is idle.</param>
	private void Prune(OperationLane lane)
	{
		if (
			lane.IsIdle
			&& lane._normalizedFilePath is { } normalizedFilePath
			&& _documentLanes.TryGetValue(normalizedFilePath, out OperationLane? currentLane)
			&& ReferenceEquals(currentLane, lane)
		)
		{
			_documentLanes.Remove(normalizedFilePath);
		}
	}

	[Conditional("DEBUG")]
	private void AssertLockHeld() => Debug.Assert(Monitor.IsEntered(syncRoot), "The scheduler lock must be held.");

	/// <summary>
	/// One queued node of an operation lane: the start gate the enqueuing call releases and the task its successors await as their predecessor.
	/// </summary>
	internal sealed class LaneNode
	{
		/// <summary>The gate that keeps the delegate from running while the enqueuing call still holds the scheduler lock.</summary>
		internal readonly TaskCompletionSource<bool> _startGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

		/// <summary>The task that completes when the node settled.</summary>
		internal Task _completion = Task.CompletedTask;

		/// <summary>Releases the start gate so the queued delegate may run.</summary>
		internal void Release() => _startGate.TrySetResult(true);
	}

	/// <summary>Tracks the serialized operation lane of one key.</summary>
	/// <param name="normalizedFilePath">The normalized document path this lane serializes, or <see langword="null"/> for the exclusive lane.</param>
	internal sealed class OperationLane(string? normalizedFilePath)
	{
		/// <summary>The normalized document path this lane serializes, or <see langword="null"/> for the exclusive lane.</summary>
		internal readonly string? _normalizedFilePath = normalizedFilePath;

		/// <summary>The tail node of the serialized lane, or <see langword="null"/> when the lane is idle.</summary>
		internal LaneNode? _tail;

		/// <summary>The task a node queued on this lane awaits as its predecessor.</summary>
		internal Task TailTask => _tail?._completion ?? Task.CompletedTask;

		/// <summary>Indicates whether the lane holds no queued or running node.</summary>
		internal bool IsIdle => _tail is null;
	}
}
