namespace Nickelony.LanguageServer.Roslyn;

/// <summary>
/// Describes the workspace file patterns a Roslyn-backed provider mirrors to the language server.
/// </summary>
/// <remarks>
/// The build inputs below are language-neutral: they drive the project and solution loading the Roslyn language
/// server performs for both C# and Visual Basic. A language package adds its own source and project patterns and
/// composes them with these through the provider constructor, so the build inputs stay declared once.
/// </remarks>
internal static class RoslynWorkspaceConventions
{
	private const string SolutionFilePattern = "*.sln";

	private const string XmlSolutionFilePattern = "*.slnx";

	private const string ProjectAssetsFilePattern = "project.assets.json";

	private const string BuildPropertiesFilePattern = "Directory.Build.props";

	private const string BuildPackagesFilePattern = "Directory.Packages.props";

	/// <summary>
	/// The build inputs that affect how the Roslyn language server discovers and loads projects. They are watched
	/// recursively because a solution or build-properties file can live in any directory of the workspace tree,
	/// and the server reloads the affected projects when one changes.
	/// </summary>
	private static readonly WorkspaceWatchSpecification[] s_buildWatchSpecifications =
	[
		new WorkspaceWatchSpecification(SolutionFilePattern, IncludeSubdirectories: true),
		new WorkspaceWatchSpecification(XmlSolutionFilePattern, IncludeSubdirectories: true),
		new WorkspaceWatchSpecification(ProjectAssetsFilePattern, IncludeSubdirectories: true),
		new WorkspaceWatchSpecification(BuildPropertiesFilePattern, IncludeSubdirectories: true),
		new WorkspaceWatchSpecification(BuildPackagesFilePattern, IncludeSubdirectories: true)
	];

	/// <summary>
	/// Composes a language's own watch patterns with the shared build-input patterns.
	/// </summary>
	/// <param name="languageSpecifications">The language-specific watch patterns, for example a C# or Visual Basic source pattern.</param>
	/// <returns>The language patterns followed by the shared build-input patterns.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="languageSpecifications"/> is <see langword="null"/>.</exception>
	internal static IReadOnlyList<WorkspaceWatchSpecification> ComposeWatchSpecifications(IReadOnlyList<WorkspaceWatchSpecification> languageSpecifications)
	{
		ArgumentNullException.ThrowIfNull(languageSpecifications);

		var composed = new List<WorkspaceWatchSpecification>(languageSpecifications.Count + s_buildWatchSpecifications.Length);

		composed.AddRange(languageSpecifications);
		composed.AddRange(s_buildWatchSpecifications);
		return composed;
	}
}
