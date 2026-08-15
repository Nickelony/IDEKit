namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// A stale-proof "operation started" signal for test file-system doubles.
/// </summary>
/// <remarks>
/// A one-shot <see cref="TaskCompletionSource{TResult}"/> cannot distinguish "the operation has not
/// started yet" from "an earlier operation completed the source": awaiting a stale completed source
/// passes immediately and can mask a race. Waiters instead name the ordinal of the occurrence they
/// need, so an earlier <see cref="Advance"/> can never complete a later wait.
/// </remarks>
internal sealed class TestSignal
{
	/// <summary>
	/// The time a test waits for an awaited ordinal before the wait fails.
	/// </summary>
	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(10);

	private readonly object _sync = new();
	private readonly List<(int Ordinal, TaskCompletionSource<object?> Source)> _waiters = [];
	private int _count;

	/// <summary>
	/// Gets the number of operations that have started.
	/// </summary>
	public int Count
	{
		get
		{
			lock (_sync)
				return _count;
		}
	}

	/// <summary>
	/// Records that one operation started and releases every waiter whose ordinal is reached.
	/// </summary>
	public void Advance()
	{
		List<TaskCompletionSource<object?>>? completed = null;

		lock (_sync)
		{
			_count++;

			for (int index = _waiters.Count - 1; index >= 0; index--)
			{
				if (_waiters[index].Ordinal > _count)
					continue;

				(completed ??= []).Add(_waiters[index].Source);
				_waiters.RemoveAt(index);
			}
		}

		if (completed is null)
			return;

		foreach (TaskCompletionSource<object?> source in completed)
			source.TrySetResult(null);
	}

	/// <summary>
	/// Returns a task that completes when the operation with the given ordinal has started, or
	/// immediately when that ordinal is already reached. The wait fails the test instead of hanging
	/// the suite when the ordinal does not arrive within the timeout.
	/// </summary>
	/// <param name="ordinal">The one-based ordinal of the awaited occurrence.</param>
	public async Task WhenReachedAsync(int ordinal)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(ordinal, 1);

		Task wait;

		lock (_sync)
		{
			if (_count >= ordinal)
				return;

			var source = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
			_waiters.Add((ordinal, source));
			wait = source.Task;
		}

		// A wait on an operation that is never reached would otherwise block the run until the test
		// framework times out without pointing at the signal; the timeout turns that into a failure
		// message that names the ordinal and the elapsed budget.
		Task completed = await Task.WhenAny(wait, Task.Delay(s_waitTimeout)).ConfigureAwait(false);
		if (completed != wait)
			Assert.Fail($"The operation signal did not reach ordinal {ordinal} within {s_waitTimeout.TotalSeconds:0} seconds.");
	}
}
