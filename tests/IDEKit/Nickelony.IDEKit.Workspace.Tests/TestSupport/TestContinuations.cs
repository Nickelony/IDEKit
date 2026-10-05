namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>Observes the thread a task continuation runs on.</summary>
internal static class TestContinuations
{
	/// <summary>Reports, once the task completes, whether its continuation ran on a thread that holds the lock.</summary>
	/// <param name="task">The task to attach the continuation to; a task that resumes its waiters inline runs them on the completing thread.</param>
	/// <param name="syncRoot">The lock the completing thread holds while it completes the task.</param>
	internal static Task<bool> RunsUnderLock(Task task, object syncRoot)
	{
		return task.ContinueWith(
			continuationFunction: _ => Monitor.IsEntered(syncRoot),
			cancellationToken: CancellationToken.None,
			continuationOptions: TaskContinuationOptions.ExecuteSynchronously,
			scheduler: TaskScheduler.Default
		);
	}
}
