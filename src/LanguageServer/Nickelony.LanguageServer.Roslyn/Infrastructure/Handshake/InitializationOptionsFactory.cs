using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Roslyn;

/// <summary>
/// Builds the initialization-options payload sent to the Roslyn language server.
/// </summary>
/// <remarks>
/// The server enables automatic project loading from its launch command line; the initialization options carry
/// the per-session override for it, so a session can cap or disable loading independently of the command line.
/// A <see langword="null"/> maximum defers to the command line and leaves the payload empty, and an explicit
/// value - including zero, which disables loading - overrides the command line for the session.
/// </remarks>
internal static class InitializationOptionsFactory
{
	/// <summary>
	/// Builds the initialization options for one session.
	/// </summary>
	/// <param name="options">The session options that select the automatic-project-loading maximum.</param>
	/// <returns>An anonymous or record object serialized into the initialize request's initialization options.</returns>
	internal static object Create(RoslynLanguageServerOptions options)
	{
		int? maximum = options.AutoLoadProjectsMaximum;

		return maximum is null
			? new { }
			: new AutoLoadProjectsInitializationOptions(maximum.Value);
	}

	/// <summary>
	/// The initialization-options document that overrides automatic project loading for the session.
	/// </summary>
	/// <param name="Maximum">
	/// The maximum number of auto-discovered projects, or zero to disable automatic loading.
	/// </param>
	private sealed record AutoLoadProjectsInitializationOptions(
		[property: JsonPropertyName("autoLoadProjects")] int Maximum);
}
