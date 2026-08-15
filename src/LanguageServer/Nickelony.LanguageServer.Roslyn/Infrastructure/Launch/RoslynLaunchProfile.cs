using System.Globalization;

namespace Nickelony.LanguageServer.Roslyn;

/// <summary>
/// Builds the command-line arguments the Roslyn language server is launched with.
/// </summary>
/// <remarks>
/// <para>
/// The profile is fixed apart from the automatic-project-loading maximum: the server always speaks LSP over
/// standard I/O, and telemetry is always pinned to <c>off</c>, because a library must not enable telemetry on
/// the host's behalf. Log verbosity and the remaining advanced switches are left at the server's defaults, so
/// they re-open only when a host needs them.
/// </para>
/// <para>
/// Automatic project loading is enabled by default, so the server discovers projects and solutions under the
/// advertised workspace folders. The maximum is passed with the switch (<c>--autoLoadProjects &lt;n&gt;</c>); a
/// maximum of zero omits the switch, because the server rejects a zero command-line maximum and reads an absent
/// switch as automatic loading off.
/// </para>
/// </remarks>
internal static class RoslynLaunchProfile
{
	private const string StdioArgument = "--stdio";
	private const string AutoLoadProjectsArgument = "--autoLoadProjects";
	private const string TelemetryLevelArgument = "--telemetryLevel";
	private const string TelemetryLevelOffValue = "off";

	/// <summary>
	/// Builds the argument list for one session.
	/// </summary>
	/// <param name="options">The session options that select the automatic-project-loading maximum.</param>
	/// <returns>The argument list, in the order it is passed to the server.</returns>
	internal static IReadOnlyList<string> CreateServerArguments(RoslynLanguageServerOptions options)
	{
		var arguments = new List<string>(4)
		{
			StdioArgument
		};

		int? maximum = options.AutoLoadProjectsMaximum;

		// A maximum of zero disables automatic project loading, so the switch is omitted entirely: the server
		// rejects a zero command-line maximum (it must be positive) and reads an absent switch as "off".
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
