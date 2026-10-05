using Avalonia;
using Avalonia.Headless;

namespace Nickelony.Samples.Avalonia.Tests;

/// <summary>Runs the sample application on the Avalonia headless platform, so a test drives the real main window without a display.</summary>
internal static partial class SampleTestHost
{
	private static readonly object s_gate = new();

	private static HeadlessUnitTestSession? s_session;

	/// <summary>The shared session, started on first use.</summary>
	internal static HeadlessUnitTestSession Instance
	{
		get
		{
			lock (s_gate)
				return s_session ??= HeadlessUnitTestSession.StartNew(typeof(HeadlessEntry));
		}
	}

	/// <summary>
	/// Runs an asynchronous test body on the UI thread while the session pumps its dispatcher, so continuations return to that thread.
	/// </summary>
	internal static Task RunAsync(Func<Task> body)
	{
		return Instance.Dispatch(
			async () =>
			{
				await body();
				return true;
			},

			CancellationToken.None
		);
	}

	/// <summary>Disposes the workbench of the window and closes the window, so no watcher or timer outlives the test.</summary>
	internal static async Task CloseWindowAsync(SampleWindow window)
	{
		await window.Workbench.DisposeAsync();
		window.Close();
	}

	/// <summary>The entry point the headless session builds its application from: the sample application over Skia rendering.</summary>
	internal static class HeadlessEntry
	{
		public static AppBuilder BuildAvaloniaApp() =>
			AppBuilder.Configure<SampleApplication>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
	}
}
