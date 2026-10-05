#if AVALONIAEDIT
namespace Nickelony.Samples.Avalonia.Tests;

#else
namespace Nickelony.Samples.Wpf.Tests;

#endif

/// <summary>
/// Holds what both sample heads share of the test host: the workbench, the window and the wait, which run on the dispatcher that each head starts.
/// </summary>
internal static partial class SampleTestHost
{
	/// <summary>Opens the main window over fresh sample files in the directory.</summary>
	internal static async Task<SampleWindow> OpenWindowAsync(TemporaryDirectory directory, SampleServerOptions? options = null)
	{
		SampleWorkbench workbench = await SampleWorkbench.CreateAsync(new SampleFiles(directory.Path), options);
		var window = new SampleWindow(workbench);
		window.Show();

		return window;
	}

	/// <summary>
	/// Runs the test body on a workbench over fresh sample files in the directory and disposes the workbench afterwards, even when the body fails.
	/// </summary>
	internal static Task RunWorkbenchAsync(TemporaryDirectory directory, Func<SampleWorkbench, Task> body)
	{
		return RunAsync(async () =>
		{
			SampleWorkbench workbench = await SampleWorkbench.CreateAsync(new SampleFiles(directory.Path));

			try
			{
				await body(workbench);
			}
			finally
			{
				await workbench.DisposeAsync();
			}
		});
	}

	/// <summary>Opens the main window, runs the test body on it and closes the window afterwards, even when the body fails.</summary>
	internal static Task RunWindowAsync(TemporaryDirectory directory, Func<SampleWindow, Task> body, SampleServerOptions? options = null)
	{
		return RunAsync(async () =>
		{
			SampleWindow window = await OpenWindowAsync(directory, options);

			try
			{
				await body(window);
			}
			finally
			{
				await CloseWindowAsync(window);
			}
		});
	}

	/// <summary>Waits until the condition holds, letting the UI thread run in between, or fails after the timeout.</summary>
	internal static async Task WaitUntilAsync(Func<bool> condition, string because, int timeoutMilliseconds = 10_000)
	{
		DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);

		while (!condition())
		{
			Assert.IsLessThan(deadline, DateTime.UtcNow, because);
			await Task.Delay(25);
		}
	}
}
