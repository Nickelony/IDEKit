namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class WorkspaceWatchSpecificationTests
{
	[TestMethod]
	public void MatchesFileName_AllFilesPattern_MatchesDotlessName()
	{
		var specification = new WorkspaceWatchSpecification("*.*", IncludeSubdirectories: false);

		// The runtime watcher normalizes "*.*" to "*" on every platform, so dotless names match everywhere.
		Assert.IsTrue(specification.MatchesFileName(@"C:\Workspace\README"));
		Assert.IsTrue(specification.MatchesFileName(@"C:\Workspace\README.md"));
	}

	[TestMethod]
	public void MatchesFileName_ExtensionPattern_RequiresTheLiteralSuffix()
	{
		var specification = new WorkspaceWatchSpecification("*.ext", IncludeSubdirectories: false);

		Assert.IsTrue(specification.MatchesFileName(@"C:\Workspace\file.ext"));
		Assert.IsFalse(specification.MatchesFileName(@"C:\Workspace\file.other"));
		Assert.IsFalse(specification.MatchesFileName(@"C:\Workspace\fileext"));
	}

	[TestMethod]
	public void MatchesFileName_ExtensionPattern_FollowsTheLocalPathCasingPolicy()
	{
		var specification = new WorkspaceWatchSpecification("*.ext", IncludeSubdirectories: false);

		// The watcher's match mirrors the configured local-path comparison policy, so the same filter is
		// case-insensitive where the file system is (Windows and macOS) and ordinal where it is not.
		if (LanguageServerPaths.UsesCaseSensitiveLocalPaths)
			Assert.IsFalse(specification.MatchesFileName(@"C:\Workspace\file.EXT"));
		else
			Assert.IsTrue(specification.MatchesFileName(@"C:\Workspace\file.EXT"));
	}

	[TestMethod]
	public void MatchesFileName_QuestionMark_MatchesExactlyOneCharacter()
	{
		var specification = new WorkspaceWatchSpecification("file?.ext", IncludeSubdirectories: false);

		// The runtime watcher uses the simple wildcard grammar on every platform, where '?' matches
		// exactly one character.
		Assert.IsTrue(specification.MatchesFileName(@"C:\Workspace\file1.ext"));
		Assert.IsFalse(specification.MatchesFileName(@"C:\Workspace\file.ext"));
		Assert.IsFalse(specification.MatchesFileName(@"C:\Workspace\file12.ext"));
	}

	[TestMethod]
	public void Validate_RejectsEmptyFilter()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new WorkspaceSnapshotTracker(@"C:\Workspace",
			[new WorkspaceWatchSpecification(string.Empty, IncludeSubdirectories: false)]));
	}

	[TestMethod]
	public void Validate_RejectsEmptySpecificationList()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new WorkspaceSnapshotTracker(@"C:\Workspace", []));
	}

	[TestMethod]
	public void Validate_RejectsDirectoryAliasFilters()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new WorkspaceSnapshotTracker(@"C:\Workspace",
			[new WorkspaceWatchSpecification(".", IncludeSubdirectories: false)]));

		Assert.ThrowsExactly<ArgumentException>(() => new WorkspaceSnapshotTracker(@"C:\Workspace",
			[new WorkspaceWatchSpecification("..", IncludeSubdirectories: false)]));
	}
}
