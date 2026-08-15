namespace Nickelony.LanguageServer.CSharp.Tests;

[TestClass]
public sealed class CSharpWorkspaceConventionsTests
{
	[TestMethod]
	public void WatchSpecifications_CoverCSharpSourceAndProjectFilesRecursively()
	{
		IReadOnlyList<WorkspaceWatchSpecification> specifications = CSharpWorkspaceConventions.WatchSpecifications;

		Assert.AreEqual(2, specifications.Count);

		// The source pattern drives document synchronization, and the Roslyn language server analyzes the whole
		// workspace tree, so the pattern is recursive.
		Assert.AreEqual("*.cs", specifications[0].Filter);
		Assert.IsTrue(specifications[0].IncludeSubdirectories);

		// The project pattern tells the server which projects to load; a project file can live in any directory of
		// the workspace tree. The shared build inputs are composed in by the Roslyn core, not declared here.
		Assert.AreEqual("*.csproj", specifications[1].Filter);
		Assert.IsTrue(specifications[1].IncludeSubdirectories);
	}

	[TestMethod]
	public void WatchSpecifications_ComposedThroughTheRoslynCore_PutTheLanguagePatternsFirstAndAddTheSharedBuildInputs()
	{
		IReadOnlyList<WorkspaceWatchSpecification> composed = RoslynWorkspaceConventions.ComposeWatchSpecifications(
			CSharpWorkspaceConventions.WatchSpecifications);

		string[] filters = composed.Select(specification => specification.Filter).ToArray();

		// The real C# patterns reach the shared composition, so the language patterns come first and the
		// language-neutral build inputs follow.
		CollectionAssert.AreEqual(
			new[] { "*.cs", "*.csproj", "*.sln", "*.slnx", "project.assets.json", "Directory.Build.props", "Directory.Packages.props" },
			filters);

		Assert.IsTrue(composed.All(specification => specification.IncludeSubdirectories));
	}
}
