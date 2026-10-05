namespace Nickelony.QuickPick;

/// <summary>Runs the accept actions of the providers one after another, so a slow action never overlaps the next.</summary>
/// <remarks>
/// An action starts when the task of the earlier action completed, in the order the actions were queued. The gate belongs to the dispatcher thread.
/// An action never faults, because the controller wraps each one in containment, so a failed action does not stop the ones behind it.
/// </remarks>
internal sealed class QuickPickAcceptGate
{
	private Task _tail = Task.CompletedTask;

	/// <summary>Queues an action behind the earlier ones.</summary>
	/// <param name="action">The action, which does not fault.</param>
	/// <returns>A task that completes when the action ended.</returns>
	internal Task RunAsync(Func<Task> action)
	{
		Task current = RunAfterAsync(_tail, action);

		_tail = current;

		return current;
	}

	private static async Task RunAfterAsync(Task earlier, Func<Task> action)
	{
		await earlier.ConfigureAwait(true);
		await action().ConfigureAwait(true);
	}
}
