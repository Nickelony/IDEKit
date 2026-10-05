using System.Runtime.ExceptionServices;

namespace Nickelony.IDEKit.Testing;

/// <summary>Runs a test action on a dedicated thread, so a thread-affinity check meets a caller that is not the editor thread.</summary>
/// <remarks>
/// A <see cref="Task.Run(Action)"/> work item can run inline on the headless dispatcher thread of the Avalonia suites, which would make
/// the "another thread" premise vacuous. A dedicated thread always leaves the editor thread.
/// </remarks>
internal static class TestThreads
{
	private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10.0);

	/// <summary>Runs the action on a new background thread and waits for it, rethrowing the action's exception on the calling thread.</summary>
	/// <param name="action">The action to run.</param>
	internal static void Run(Action action)
	{
		ExceptionDispatchInfo? failure = RunAndCapture(action);
		failure?.Throw();
	}

	/// <summary>Runs the action on a new background thread and waits for it.</summary>
	/// <param name="action">The action to run.</param>
	/// <returns>The exception the action threw, or <see langword="null"/> when it completed.</returns>
	internal static Exception? CaptureFailure(Action action) => RunAndCapture(action)?.SourceException;

	private static ExceptionDispatchInfo? RunAndCapture(Action action)
	{
		ExceptionDispatchInfo? failure = null;

		var thread = new Thread(() =>
		{
			try
			{
				action();
			}
			catch (Exception exception)
			{
				failure = ExceptionDispatchInfo.Capture(exception);
			}
		})
		{
			IsBackground = true,
		};

		thread.Start();

		Assert.IsTrue(thread.Join(s_timeout), "The action did not complete on the dedicated thread.");

		return failure;
	}
}
