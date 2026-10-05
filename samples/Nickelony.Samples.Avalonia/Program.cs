using Avalonia;

namespace Nickelony.Samples.Avalonia;

internal static class Program
{
	[STAThread]
	private static int Main(string[] args) => BuildApplication().StartWithClassicDesktopLifetime(args);

	internal static AppBuilder BuildApplication() => AppBuilder.Configure<SampleApplication>().UsePlatformDetect();
}
