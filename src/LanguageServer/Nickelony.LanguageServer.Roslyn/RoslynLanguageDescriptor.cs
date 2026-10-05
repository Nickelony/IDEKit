namespace Nickelony.LanguageServer.Roslyn;

/// <summary>Describes what one language of the Roslyn language server contributes to its profile.</summary>
/// <param name="LanguageId">The document language identifier that the server uses to pick the language.</param>
/// <param name="DisplayName">The name that the profile uses in its failure messages and logs.</param>
/// <param name="ProjectFileExtension">The extension of the language's project files, which the profile asks the server to open.</param>
/// <param name="WatchSpecifications">
/// The language's own source and project patterns, without the shared build inputs. Both patterns are recursive because the Roslyn language server
/// analyzes the whole workspace tree: the source pattern drives document synchronization, and the project pattern tells the server which projects
/// to load.
/// </param>
internal sealed record RoslynLanguageDescriptor(
	string LanguageId,
	string DisplayName,
	string ProjectFileExtension,
	IReadOnlyList<FileWatchSpecification> WatchSpecifications
)
{
	private static readonly RoslynLanguageDescriptor s_csharp = new(
		LanguageId: "csharp",
		DisplayName: "C#",
		ProjectFileExtension: ".csproj",

		WatchSpecifications:
		[
			new FileWatchSpecification("*.cs", IncludeSubdirectories: true),
			new FileWatchSpecification("*.csproj", IncludeSubdirectories: true),
		]
	);

	private static readonly RoslynLanguageDescriptor s_visualBasic = new(
		LanguageId: "vb",
		DisplayName: "Visual Basic",
		ProjectFileExtension: ".vbproj",

		WatchSpecifications:
		[
			new FileWatchSpecification("*.vb", IncludeSubdirectories: true),
			new FileWatchSpecification("*.vbproj", IncludeSubdirectories: true),
		]
	);

	/// <summary>Returns the descriptor of a shipped language.</summary>
	/// <param name="language">The language to describe.</param>
	/// <returns>The descriptor of <paramref name="language"/>.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="language"/> is not a defined value.</exception>
	internal static RoslynLanguageDescriptor For(RoslynLanguage language)
	{
		return language switch
		{
			RoslynLanguage.CSharp => s_csharp,
			RoslynLanguage.VisualBasic => s_visualBasic,
			_ => throw new ArgumentOutOfRangeException(nameof(language), language, "The language is not a defined value."),
		};
	}
}
