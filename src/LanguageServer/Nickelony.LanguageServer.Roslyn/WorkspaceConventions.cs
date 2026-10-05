namespace Nickelony.LanguageServer.Roslyn;

/// <summary>Describes the workspace file patterns a Roslyn-backed provider mirrors to the language server.</summary>
/// <remarks>
/// The shared build inputs - the solution, project-asset, build-properties, build-targets, build-packages,
/// and global-json patterns - are language-neutral: they drive the project and solution loading the Roslyn language server performs
/// for both C# and Visual Basic. A <see cref="RoslynLanguageDescriptor"/> declares its own source and project patterns, and the profile composes
/// them with these, so the build inputs stay declared once.
/// </remarks>
internal static class WorkspaceConventions
{
	private const string SolutionFilePattern = "*.sln";

	private const string XmlSolutionFilePattern = "*.slnx";

	private const string ProjectAssetsFilePattern = "project.assets.json";

	private const string BuildPropertiesFilePattern = "Directory.Build.props";

	private const string BuildTargetsFilePattern = "Directory.Build.targets";

	private const string BuildPackagesFilePattern = "Directory.Packages.props";

	private const string GlobalJsonFilePattern = "global.json";

	/// <summary>The build inputs that affect how the Roslyn language server discovers and loads projects.</summary>
	/// <remarks>
	/// They are watched recursively because a solution or build-properties file can live in any directory of the workspace tree, and the server
	/// reloads the affected projects when one changes.
	/// </remarks>
	private static readonly FileWatchSpecification[] s_buildWatchSpecifications =
	[
		new FileWatchSpecification(SolutionFilePattern, IncludeSubdirectories: true),
		new FileWatchSpecification(XmlSolutionFilePattern, IncludeSubdirectories: true),
		new FileWatchSpecification(ProjectAssetsFilePattern, IncludeSubdirectories: true),
		new FileWatchSpecification(BuildPropertiesFilePattern, IncludeSubdirectories: true),
		new FileWatchSpecification(BuildTargetsFilePattern, IncludeSubdirectories: true),
		new FileWatchSpecification(BuildPackagesFilePattern, IncludeSubdirectories: true),
		new FileWatchSpecification(GlobalJsonFilePattern, IncludeSubdirectories: true),
	];

	/// <summary>Composes a language's own watch patterns with the shared build-input patterns.</summary>
	/// <param name="languageWatchSpecifications">The language-specific watch patterns, for example a C# or Visual Basic source pattern.</param>
	/// <returns>The language patterns followed by the shared build-input patterns.</returns>
	internal static IReadOnlyList<FileWatchSpecification> ComposeWatchSpecifications(
		IReadOnlyList<FileWatchSpecification> languageWatchSpecifications
	)
	{
		var composed = new List<FileWatchSpecification>(languageWatchSpecifications.Count + s_buildWatchSpecifications.Length);

		composed.AddRange(languageWatchSpecifications);
		composed.AddRange(s_buildWatchSpecifications);

		return composed;
	}
}
