namespace Nickelony.LanguageServer.Roslyn.Tests;

/// <summary>
/// Covers the shared workspace conventions: a language package's own patterns are composed with the
/// language-neutral build inputs the Roslyn language server reloads projects from.
/// </summary>
[TestClass]
public sealed class RoslynWorkspaceConventionsTests
{
	[TestMethod]
	public void ComposeWatchSpecifications_PutsTheLanguagePatternsFirstAndAddsTheSharedBuildInputs()
	{
		IReadOnlyList<WorkspaceWatchSpecification> composed = RoslynWorkspaceConventions.ComposeWatchSpecifications(
			TestRoslynLanguageServerIntelliSenseProvider.LanguageWatchSpecifications);

		string[] filters = composed.Select(specification => specification.Filter).ToArray();

		// The language pattern comes first; the shared build inputs follow so a solution, project-assets, or
		// build-properties change is still mirrored to the server for every language.
		CollectionAssert.AreEqual(
			new[] { "*.vbtest", "*.sln", "*.slnx", "project.assets.json", "Directory.Build.props", "Directory.Packages.props" },
			filters);

		Assert.IsTrue(composed.All(specification => specification.IncludeSubdirectories));
	}

	[TestMethod]
	public void ComposeWatchSpecifications_WithNoLanguagePatterns_ReturnsTheSharedBuildInputs()
	{
		IReadOnlyList<WorkspaceWatchSpecification> composed = RoslynWorkspaceConventions.ComposeWatchSpecifications([]);

		Assert.AreEqual(5, composed.Count);
	}
}
