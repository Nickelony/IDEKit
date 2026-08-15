namespace Nickelony.LanguageServer.Roslyn.Tests;

/// <summary>
/// Pins the command-line arguments the Roslyn language server is launched with.
/// </summary>
[TestClass]
public sealed class RoslynLaunchProfileTests
{
	[TestMethod]
	public void DefaultOptions_LaunchWithStdioAutoLoadAndTelemetryOff()
	{
		IReadOnlyList<string> arguments = RoslynLaunchProfile.CreateServerArguments(RoslynLanguageServerOptions.Default);

		CollectionAssert.AreEqual(
			new[] { "--stdio", "--autoLoadProjects", "--telemetryLevel", "off" },
			arguments.ToArray());
	}

	[TestMethod]
	public void ConfiguredMaximum_IsPassedToTheAutoLoadSwitch()
	{
		IReadOnlyList<string> arguments = RoslynLaunchProfile.CreateServerArguments(
			new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 200 });

		CollectionAssert.AreEqual(
			new[] { "--stdio", "--autoLoadProjects", "200", "--telemetryLevel", "off" },
			arguments.ToArray());
	}

	[TestMethod]
	public void ZeroMaximum_OmitsTheAutoLoadSwitchEntirely()
	{
		// A zero command-line maximum is invalid (the server requires a positive value), so disabling automatic
		// loading is expressed by omitting the switch; the initialization options then carry the explicit zero.
		IReadOnlyList<string> arguments = RoslynLaunchProfile.CreateServerArguments(
			new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 0 });

		CollectionAssert.AreEqual(
			new[] { "--stdio", "--telemetryLevel", "off" },
			arguments.ToArray());
	}
}
