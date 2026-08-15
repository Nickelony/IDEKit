namespace Nickelony.LanguageServer.CSharp;

/// <summary>
/// Describes the workspace file patterns the C# provider mirrors to the language server.
/// </summary>
/// <remarks>
/// Only the language's own source and project patterns are declared here; the shared solution, project-asset,
/// build-properties, and build-packages patterns are added by the Roslyn core when the watch set is composed, so the build inputs stay
/// declared once for every language the server serves.
/// </remarks>
internal static class CSharpWorkspaceConventions
{
	private const string CSharpFilePattern = "*.cs";

	private const string CSharpProjectFilePattern = "*.csproj";

	/// <summary>
	/// The watch specifications mirrored to the language server: every C# source file and every C# project file in
	/// the workspace tree.
	/// </summary>
	/// <remarks>
	/// Both patterns are recursive because the Roslyn language server analyzes the whole workspace tree: the source
	/// pattern drives document synchronization, and the project pattern tells the server which projects to load.
	/// The shared build inputs that also affect project loading are appended by the provider base.
	/// </remarks>
	internal static readonly IReadOnlyList<WorkspaceWatchSpecification> WatchSpecifications =
	[
		new WorkspaceWatchSpecification(CSharpFilePattern, IncludeSubdirectories: true),
		new WorkspaceWatchSpecification(CSharpProjectFilePattern, IncludeSubdirectories: true)
	];
}
