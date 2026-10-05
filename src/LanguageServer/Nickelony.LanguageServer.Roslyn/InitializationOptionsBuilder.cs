using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Roslyn;

/// <summary>Builds the initialization-options payload sent to the Roslyn language server.</summary>
/// <remarks>
/// The payload carries the same maximum as the launch command line under the <c>autoLoadProjects</c> key, and a <see langword="null"/> maximum
/// leaves it empty. The Roslyn guide records that the server it was measured against ignores the value, so the launch arguments and the project-open
/// notifications decide what loads.
/// </remarks>
internal static class InitializationOptionsBuilder
{
	/// <summary>Builds the initialization options for one session.</summary>
	/// <param name="options">The session options that select the automatic-project-loading maximum.</param>
	/// <returns>An anonymous or record object serialized into the initialize request's initialization options.</returns>
	internal static object Build(RoslynLanguageServerOptions options)
	{
		int? maximum = options.AutoLoadProjectsMaximum;
		return maximum is null ? new { } : new AutoLoadProjectsInitializationOptions(maximum.Value);
	}

	/// <summary>The initialization-options document that carries the automatic-project-loading maximum.</summary>
	/// <param name="Maximum">The maximum number of auto-discovered projects, or zero to disable automatic loading.</param>
	private sealed record AutoLoadProjectsInitializationOptions([property: JsonPropertyName("autoLoadProjects")] int Maximum);
}
