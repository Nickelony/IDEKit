namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Holds the per-document disk gates of an active workspace operation until it completes.</summary>
/// <remarks>
/// Complete releases the held gates in reverse acquisition order before it completes the waiter task, so a waiting operation can observe the released
/// state after completion.
/// </remarks>
internal sealed class OperationLease
{
	private readonly IReadOnlyList<SemaphoreSlim> _gates;
	private int _completed;

	internal OperationLease(SemaphoreSlim gate)
		: this([gate]) { }

	internal OperationLease(IReadOnlyList<SemaphoreSlim> gates) => _gates = gates;

	internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

	internal void Complete()
	{
		// A second completion would over-release every gate and throw SemaphoreFullException; the
		// once-guard keeps the method idempotent even though every caller completes exactly once today.
		if (Interlocked.Exchange(ref _completed, 1) != 0)
			return;

		foreach (SemaphoreSlim gate in _gates.Reverse())
			gate.Release();

		Completion.TrySetResult();
	}
}
