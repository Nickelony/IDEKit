using System.Windows.Threading;

namespace Nickelony.Samples.Wpf.Tests;

/// <summary>Runs the sample application on the dispatcher of the test thread, which is how the application runs it, on a desktop session.</summary>
internal static partial class SampleTestHost
{
	/// <summary>
	/// Starts the test body with the dispatcher's synchronization context installed, pumps the dispatcher until it completes and returns
	/// the completed task.
	/// </summary>
	/// <remarks>
	/// The context makes every continuation return to the test thread, because a WPF editor is created and used on the thread
	/// that owns it. The task is complete when the call returns, so a test that awaits it continues on the test thread without a pump of its own.
	/// </remarks>
	internal static Task RunAsync(Func<Task> body)
	{
		SynchronizationContext? previous = SynchronizationContext.Current;
		SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

		try
		{
			Task task = body();
			var frame = new DispatcherFrame();
			_ = task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
			Dispatcher.PushFrame(frame);

			task.GetAwaiter().GetResult();

			return task;
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext(previous);
		}
	}

	/// <summary>
	/// Disposes the workbench of the window, closes the window and waits until it is gone, so no watcher or timer outlives the test.
	/// </summary>
	/// <remarks>
	/// The window asks about unsaved text before it closes, and the test may have set the answer to Cancel, so the answers discard the text.
	/// A window that the test closed is not closed again, because WPF refuses to close a window twice.
	/// </remarks>
	internal static async Task CloseWindowAsync(SampleWindow window)
	{
		await window.Workbench.DisposeAsync();

		if (!window.IsVisible)
			return;

		bool closed = false;
		window.Closed += (_, _) => closed = true;
		window.Workbench.DecideSaveAsync = static _ => Task.FromResult(SampleSaveDecision.Discard);
		window.Workbench.DecideSaveAllAsync = static _ => Task.FromResult(SampleSaveDecision.Discard);

		window.Close();

		await WaitUntilAsync(() => closed, "The window did not close.");
	}
}
