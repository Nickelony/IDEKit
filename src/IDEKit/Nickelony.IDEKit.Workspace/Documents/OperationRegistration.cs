namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Tracks an active workspace operation and the per-document disk gates it holds.
/// </summary>
/// <remarks>
/// Completion releases the held gates in reverse acquisition order before completing the waiter
/// task, so a waiting operation can observe the released state after completion.
/// </remarks>
internal sealed class OperationRegistration
{
	private readonly IReadOnlyList<SemaphoreSlim> _gates;
	private int _completed;

	public OperationRegistration(SemaphoreSlim gate)
		: this([gate])
	{ }

	public OperationRegistration(IReadOnlyList<SemaphoreSlim> gates)
	{
		_gates = gates;
	}

	public TaskCompletionSource Completion { get; } =
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	public void Complete()
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
