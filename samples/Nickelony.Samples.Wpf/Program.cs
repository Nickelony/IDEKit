using System.Windows;

namespace Nickelony.Samples.Wpf;

internal static class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
		application.Startup += async (_, _) => await ShowMainWindowAsync(args);
		application.Run();
	}

	private static async Task ShowMainWindowAsync(string[] args)
	{
		SampleWorkbench workbench = await SampleWorkbench.CreateAsync(SampleFiles.FromCommandLine(args), SampleServerOptions.FromEnvironment());
		var window = new SampleWindow(workbench);

		Application.Current.MainWindow = window;
		window.Show();
	}
}
