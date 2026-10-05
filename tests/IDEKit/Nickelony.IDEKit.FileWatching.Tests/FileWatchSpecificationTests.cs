namespace Nickelony.IDEKit.FileWatching.Tests;

[TestClass]
public sealed class FileWatchSpecificationTests
{
	[TestMethod]
	public void MatchesFileName_AllFilesPattern_MatchesDotlessName()
	{
		var specification = new FileWatchSpecification("*.*", IncludeSubdirectories: false);

		// The runtime watcher normalizes "*.*" to "*" on every platform, so dotless names match everywhere.
		Assert.IsTrue(specification.MatchesFileName(@"C:\Workspace\README", TestPaths.PathComparison));
		Assert.IsTrue(specification.MatchesFileName(@"C:\Workspace\README.md", TestPaths.PathComparison));
	}

	[TestMethod]
	public void MatchesFileName_ExtensionPattern_RequiresTheLiteralSuffix()
	{
		var specification = new FileWatchSpecification("*.ext", IncludeSubdirectories: false);

		Assert.IsTrue(specification.MatchesFileName(@"C:\Workspace\file.ext", TestPaths.PathComparison));
		Assert.IsFalse(specification.MatchesFileName(@"C:\Workspace\file.other", TestPaths.PathComparison));
		Assert.IsFalse(specification.MatchesFileName(@"C:\Workspace\fileext", TestPaths.PathComparison));
	}

	[TestMethod]
	public void MatchesFileName_ExtensionPattern_FollowsThePathComparison()
	{
		var specification = new FileWatchSpecification("*.ext", IncludeSubdirectories: false);

		// The watcher's match mirrors the configured path comparison, so the same filter is case-insensitive
		// where the host says the file system is and ordinal where it says it is not.
		Assert.IsFalse(specification.MatchesFileName(@"C:\Workspace\file.EXT", LocalPathComparisonPolicy.CaseSensitive));
		Assert.IsTrue(specification.MatchesFileName(@"C:\Workspace\file.EXT", LocalPathComparisonPolicy.CaseInsensitive));
	}

	[TestMethod]
	public void MatchesFileName_QuestionMark_MatchesExactlyOneCharacter()
	{
		var specification = new FileWatchSpecification("file?.ext", IncludeSubdirectories: false);

		// The runtime watcher uses the simple wildcard grammar on every platform, where '?' matches
		// exactly one character.
		Assert.IsTrue(specification.MatchesFileName(@"C:\Workspace\file1.ext", TestPaths.PathComparison));
		Assert.IsFalse(specification.MatchesFileName(@"C:\Workspace\file.ext", TestPaths.PathComparison));
		Assert.IsFalse(specification.MatchesFileName(@"C:\Workspace\file12.ext", TestPaths.PathComparison));
	}

	[TestMethod]
	public void Validate_RejectsEmptyFilter()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new FileSnapshotTracker(@"C:\Workspace", [new FileWatchSpecification(string.Empty, IncludeSubdirectories: false)])
		);
	}

	[TestMethod]
	public void Validate_RejectsEmptySpecificationList() =>
		Assert.ThrowsExactly<ArgumentException>(() => new FileSnapshotTracker(@"C:\Workspace", []));

	[TestMethod]
	public void Validate_RejectsDirectoryAliasFilters()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new FileSnapshotTracker(@"C:\Workspace", [new FileWatchSpecification(".", IncludeSubdirectories: false)])
		);

		Assert.ThrowsExactly<ArgumentException>(() =>
			new FileSnapshotTracker(@"C:\Workspace", [new FileWatchSpecification("..", IncludeSubdirectories: false)])
		);
	}
}
