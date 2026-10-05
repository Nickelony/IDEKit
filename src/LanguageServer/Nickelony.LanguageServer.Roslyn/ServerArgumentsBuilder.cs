using System.Globalization;

namespace Nickelony.LanguageServer.Roslyn;

/// <summary>Builds the command-line arguments the Roslyn language server is launched with.</summary>
/// <remarks>
/// Only the automatic-project-loading maximum varies; <c>--stdio</c> and <c>--telemetryLevel off</c> are fixed. A <see langword="null"/> maximum
/// emits the bare <c>--autoLoadProjects</c> switch, which selects the server's own default cap, and a positive maximum appends its value.
/// Zero omits the switch because the server rejects a zero maximum, so an absent switch means automatic loading is off.
/// </remarks>
internal static class ServerArgumentsBuilder
{
	private const string StdioArgument = "--stdio";
	private const string AutoLoadProjectsArgument = "--autoLoadProjects";
	private const string TelemetryLevelArgument = "--telemetryLevel";
	private const string TelemetryLevelOffValue = "off";

	/// <summary>Builds the argument list for one session.</summary>
	/// <param name="options">The session options that select the automatic-project-loading maximum.</param>
	/// <returns>The argument list, in the order it is passed to the server.</returns>
	internal static IReadOnlyList<string> Build(RoslynLanguageServerOptions options)
	{
		var arguments = new List<string>(5) { StdioArgument };

		int? maximum = options.AutoLoadProjectsMaximum;

		if (maximum is not 0)
		{
			arguments.Add(AutoLoadProjectsArgument);

			if (maximum is > 0)
				arguments.Add(maximum.Value.ToString(CultureInfo.InvariantCulture));
		}

		arguments.Add(TelemetryLevelArgument);
		arguments.Add(TelemetryLevelOffValue);

		return arguments;
	}
}
