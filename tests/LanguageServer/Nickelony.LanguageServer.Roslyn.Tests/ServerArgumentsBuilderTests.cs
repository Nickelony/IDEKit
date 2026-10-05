namespace Nickelony.LanguageServer.Roslyn.Tests;

/// <summary>Pins the command-line arguments the Roslyn language server is launched with.</summary>
[TestClass]
public sealed class ServerArgumentsBuilderTests
{
	[TestMethod]
	public void DefaultOptions_LaunchWithStdioAutoLoadAndTelemetryOff()
	{
		IReadOnlyList<string> arguments = ServerArgumentsBuilder.Build(RoslynLanguageServerOptions.Default);
		Assert.AreSequenceEqual(["--stdio", "--autoLoadProjects", "--telemetryLevel", "off"], [.. arguments]);
	}

	[TestMethod]
	public void ConfiguredMaximum_IsPassedToTheAutoLoadSwitch()
	{
		IReadOnlyList<string> arguments = ServerArgumentsBuilder.Build(new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 200 });
		Assert.AreSequenceEqual(["--stdio", "--autoLoadProjects", "200", "--telemetryLevel", "off"], [.. arguments]);
	}

	[TestMethod]
	public void ZeroMaximum_OmitsTheAutoLoadSwitchEntirely()
	{
		// A zero command-line maximum is invalid (the server requires a positive value), so disabling automatic
		// loading is expressed by omitting the switch; the initialization options then carry the explicit zero.
		IReadOnlyList<string> arguments = ServerArgumentsBuilder.Build(new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 0 });
		Assert.AreSequenceEqual(["--stdio", "--telemetryLevel", "off"], [.. arguments]);
	}
}
