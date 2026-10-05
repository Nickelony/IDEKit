using Nickelony.IDEKit.Core.Paths;

namespace Nickelony.IDEKit.Workspace.QuickPick.Tests;

/// <summary>
/// Covers the globs that keep files and folders out of the file list: what a glob with and without a slash matches, and the casing.
/// </summary>
[TestClass]
public sealed class WorkspaceFileFilterTests
{
	private static WorkspaceFileFilter FilterOf(string[] globs, bool ignoreCase = true) => new(globs, new LocalPathComparisonPolicy(ignoreCase));

	[TestMethod]
	public void ExcludesPath_NoGlobs_ExcludesNothing() => Assert.IsFalse(FilterOf([]).ExcludesPath("bin/debug/a.dll"));

	[TestMethod]
	public void ExcludesPath_AGlobWithoutASlash_MatchesTheNameOfAFolderOrAFileAtAnyDepth()
	{
		WorkspaceFileFilter filter = FilterOf(["bin", "*.tmp"]);

		Assert.IsTrue(filter.ExcludesPath("bin"));
		Assert.IsTrue(filter.ExcludesPath("bin/a.dll"));
		Assert.IsTrue(filter.ExcludesPath("src/lib/bin/deep/a.dll"));
		Assert.IsTrue(filter.ExcludesPath("src/x.tmp"));
		Assert.IsFalse(filter.ExcludesPath("src/binary/a.dll"));
		Assert.IsFalse(filter.ExcludesPath("src/a.cs"));
	}

	[TestMethod]
	public void ExcludesPath_AGlobWithASlash_MatchesThePathFromTheRootAndEverythingBelowIt()
	{
		WorkspaceFileFilter filter = FilterOf(["docs/build", "src/*/generated"]);

		Assert.IsTrue(filter.ExcludesPath("docs/build"));
		Assert.IsTrue(filter.ExcludesPath("docs/build/a.html"));
		Assert.IsTrue(filter.ExcludesPath("src/app/generated/a.cs"));
		Assert.IsFalse(filter.ExcludesPath("other/docs/build/a.html"));
		Assert.IsFalse(filter.ExcludesPath("docs/source/a.md"));
	}

	[TestMethod]
	public void ExcludesPath_ALeadingAnyDepthMark_IsTheSameAsTheGlobWithoutIt()
	{
		WorkspaceFileFilter filter = FilterOf(["**/obj", "**/**/.git/"]);

		Assert.IsTrue(filter.ExcludesPath("obj/a.cs"));
		Assert.IsTrue(filter.ExcludesPath("src/obj/a.cs"));
		Assert.IsTrue(filter.ExcludesPath(".git/config"));
	}

	[TestMethod]
	[DataRow("**/bin/**", "bin", true)]
	[DataRow("**/bin/**", "bin/a.dll", true)]
	[DataRow("**/bin/**", "src/bin", true)]
	[DataRow("**/bin/**", "src/lib/bin/deep/a.dll", true)]
	[DataRow("**/bin/**", "src/binary/a.dll", false)]
	[DataRow("**/bin/**", "src/a.dll", false)]
	[DataRow("src/**/obj", "src/obj", true)]
	[DataRow("src/**/obj", "src/obj/a.dll", true)]
	[DataRow("src/**/obj", "src/x/y/obj/a.dll", true)]
	[DataRow("src/**/obj", "obj/a.dll", false)]
	[DataRow("src/**/obj", "other/src/obj/a.dll", false)]
	[DataRow("**/docs/build", "docs/build/a.html", true)]
	[DataRow("**/docs/build", "site/docs/build/a.html", true)]
	[DataRow("**/docs/build", "docs/builds/a.html", false)]
	[DataRow("**/docs/build", "docs/a.html", false)]
	[DataRow("docs/**", "docs", true)]
	[DataRow("docs/**", "docs/a/b.md", true)]
	[DataRow("docs/**", "other/docs/a.md", false)]
	[DataRow("a/**/b/**/c", "a/b/c", true)]
	[DataRow("a/**/b/**/c", "a/x/b/y/z/c/d", true)]
	[DataRow("a/**/b/**/c", "a/x/c", false)]
	[DataRow("**/**/bin", "x/bin/a", true)]
	[DataRow("**/*.tmp", "a.tmp", true)]
	[DataRow("**/*.tmp", "x/y/a.tmp", true)]
	[DataRow("**/*.tmp", "x/a.tmpl", false)]
	[DataRow("**", "a/b.cs", true)]
	[DataRow("**/", "a.cs", true)]
	public void ExcludesPath_TheAnyDepthSegment_MatchesZeroOrMoreSegmentsAnywhereInAGlob(string glob, string path, bool expected) =>
		Assert.AreEqual(expected, FilterOf([glob]).ExcludesPath(path));

	[TestMethod]
	public void ExcludesPath_AStarInAPathGlob_StaysInsideOneSegment()
	{
		WorkspaceFileFilter filter = FilterOf(["src/*/generated"]);

		Assert.IsTrue(filter.ExcludesPath("src/app/generated/a.cs"));
		Assert.IsFalse(filter.ExcludesPath("src/app/deep/generated/a.cs"));
		Assert.IsFalse(filter.ExcludesPath("src/generated/a.cs"));
	}

	[TestMethod]
	public void ExcludesPath_BackslashesInAGlob_ReadAsSlashes() => Assert.IsTrue(FilterOf(["docs\\build"]).ExcludesPath("docs/build/a.html"));

	[TestMethod]
	public void ExcludesPath_TheCaseFollowsThePolicy()
	{
		Assert.IsTrue(FilterOf(["BIN"], ignoreCase: true).ExcludesPath("src/bin/a.dll"));
		Assert.IsFalse(FilterOf(["BIN"], ignoreCase: false).ExcludesPath("src/bin/a.dll"));
		Assert.IsTrue(FilterOf(["BIN"], ignoreCase: false).ExcludesPath("src/BIN/a.dll"));
	}

	[TestMethod]
	public void Excludes_ChecksOnlyTheEntryItself()
	{
		WorkspaceFileFilter filter = FilterOf(["bin"]);

		Assert.IsTrue(filter.Excludes("bin", "src/bin"));
		Assert.IsFalse(filter.Excludes("a.dll", "src/bin/a.dll"));
	}

	[TestMethod]
	public void Constructor_BlankAndSlashOnlyGlobs_AreIgnored()
	{
		WorkspaceFileFilter filter = FilterOf(["  ", "/", " \\ "]);
		Assert.IsFalse(filter.ExcludesPath("a/b.cs"));
	}
}
