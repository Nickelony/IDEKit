namespace Nickelony.LanguageServer.Roslyn.Tests;

/// <summary>
/// Covers the shared workspace conventions: a language's own patterns are composed with the language-neutral build inputs the Roslyn language server
/// reloads projects from.
/// </summary>
[TestClass]
public sealed class WorkspaceConventionsTests
{
	[TestMethod]
	public void ComposeWatchSpecifications_PutsTheLanguagePatternsFirstAndAddsTheSharedBuildInputs()
	{
		IReadOnlyList<FileWatchSpecification> composed = WorkspaceConventions.ComposeWatchSpecifications(TestRoslynLanguages.TestWatchSpecifications);

		string[] filters = [.. composed.Select(specification => specification.Filter)];

		// The language pattern comes first; the shared build inputs follow so a solution, project-assets, or
		// build-properties change is still mirrored to the server for every language.
		Assert.AreSequenceEqual(
			[
				"*.vbtest",
				"*.sln",
				"*.slnx",
				"project.assets.json",
				"Directory.Build.props",
				"Directory.Build.targets",
				"Directory.Packages.props",
				"global.json",
			],

			filters
		);

		Assert.IsTrue(composed.All(specification => specification.IncludeSubdirectories));
	}

	[TestMethod]
	[DynamicData(nameof(TestRoslynLanguages.ShippedLanguageRows), typeof(TestRoslynLanguages))]
	public void ComposeWatchSpecifications_ForAShippedLanguage_PutsItsPatternsFirstAndAddsTheSharedBuildInputs(RoslynLanguage language)
	{
		IReadOnlyList<FileWatchSpecification> composed = WorkspaceConventions.ComposeWatchSpecifications(
			RoslynLanguageDescriptor.For(language).WatchSpecifications
		);

		string[] filters = [.. composed.Select(specification => specification.Filter)];

		// The real language patterns reach the shared composition, so the language patterns come first and the
		// language-neutral build inputs follow.
		Assert.AreSequenceEqual(
			[
				.. RoslynLanguageDescriptor.For(language).WatchSpecifications.Select(specification => specification.Filter),
				"*.sln",
				"*.slnx",
				"project.assets.json",
				"Directory.Build.props",
				"Directory.Build.targets",
				"Directory.Packages.props",
				"global.json",
			],

			filters
		);

		Assert.IsTrue(composed.All(specification => specification.IncludeSubdirectories));
	}

	[TestMethod]
	public void ComposeWatchSpecifications_WithNoLanguagePatterns_ReturnsTheSharedBuildInputs()
	{
		IReadOnlyList<FileWatchSpecification> composed = WorkspaceConventions.ComposeWatchSpecifications([]);
		Assert.HasCount(7, composed);
	}
}
