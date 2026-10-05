namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class LocalPathTests
{
	private static readonly string s_directory = Path.Combine(Path.GetTempPath(), "LocalPathTests");

	[TestMethod]
	public void Normalize_FullyQualifiedPath_TrimsTheTrailingSeparatorAndUnifiesSeparators()
	{
		string expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(s_directory, "Folder")));
		string raw = Path.Combine(s_directory, "Folder").Replace('\\', '/') + "/";

		Assert.AreEqual(expected, LocalPath.Normalize(raw));
		Assert.AreEqual(expected, LocalPath.Normalize(expected + Path.DirectorySeparatorChar));
	}

	[TestMethod]
	public void Normalize_PreservesTheInputCasing()
	{
		string normalized = LocalPath.Normalize(Path.Combine(s_directory, "MiXed.Ext"));
		Assert.IsTrue(normalized.EndsWith("MiXed.Ext", StringComparison.Ordinal), normalized);
	}

	[TestMethod]
	[DataRow("file.ext")]
	[DataRow("dir/file.ext")]
	[DataRow("./file.ext")]
	[DataRow("../file.ext")]
	public void Normalize_RelativePath_ThrowsInsteadOfResolvingAgainstTheCurrentDirectory(string relativePath) =>
		Assert.ThrowsExactly<ArgumentException>(() => LocalPath.Normalize(relativePath));

	[TestMethod]
	public void Normalize_NullPath_ThrowsArgumentNullException() => Assert.ThrowsExactly<ArgumentNullException>(() => LocalPath.Normalize(null!));

	[TestMethod]
	[DataRow("")]
	[DataRow("   ")]
	public void Normalize_BlankPath_ThrowsArgumentException(string blankPath) =>
		Assert.ThrowsExactly<ArgumentException>(() => LocalPath.Normalize(blankPath));

	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("   ")]
	[DataRow("file.ext")]
	[DataRow("dir/file.ext")]
	public void TryNormalize_BlankOrRelativePath_ReturnsFalseWithAnEmptyResult(string? path)
	{
		Assert.IsFalse(LocalPath.TryNormalize(path, out string normalizedPath));
		Assert.AreEqual(string.Empty, normalizedPath);
	}

	[TestMethod]
	public void TryNormalize_FullyQualifiedPath_ReturnsTheNormalizedPath()
	{
		string path = Path.Combine(s_directory, "Folder") + Path.DirectorySeparatorChar;

		Assert.IsTrue(LocalPath.TryNormalize(path, out string normalizedPath));
		Assert.AreEqual(LocalPath.Normalize(path), normalizedPath);
	}

	[TestMethod]
	public void NormalizeRoots_EmptyList_ModelsAFolderlessSession() =>
		Assert.IsEmpty(LocalPath.NormalizeRoots([], LocalPathComparisonPolicy.ForCurrentPlatform));

	[TestMethod]
	public void NormalizeRoots_NormalizesEachRootAndKeepsCallerOrder()
	{
		string first = Path.Combine(s_directory, "Second") + Path.DirectorySeparatorChar;
		string second = Path.Combine(s_directory, "First");

		IReadOnlyList<string> roots = LocalPath.NormalizeRoots([first, second], LocalPathComparisonPolicy.ForCurrentPlatform);

		Assert.AreSequenceEqual([LocalPath.Normalize(first), LocalPath.Normalize(second)], [.. roots]);
	}

	[TestMethod]
	public void NormalizeRoots_DuplicatesFollowTheComparisonPolicy()
	{
		string lower = Path.Combine(s_directory, "root");
		string upper = Path.Combine(s_directory, "ROOT");

		Assert.ThrowsExactly<ArgumentException>(() => LocalPath.NormalizeRoots([lower, upper], LocalPathComparisonPolicy.CaseInsensitive));
		Assert.HasCount(2, LocalPath.NormalizeRoots([lower, upper], LocalPathComparisonPolicy.CaseSensitive));
	}

	[TestMethod]
	public void NormalizeRoots_SpellingsOfOneRoot_AreDuplicatesUnderEveryPolicy()
	{
		string root = Path.Combine(s_directory, "root");

		Assert.ThrowsExactly<ArgumentException>(() =>
			LocalPath.NormalizeRoots([root, root + Path.DirectorySeparatorChar], LocalPathComparisonPolicy.CaseSensitive)
		);
	}

	[TestMethod]
	public void NormalizeRoots_NullBlankOrRelativeEntry_ThrowsArgumentException()
	{
		LocalPathComparisonPolicy policy = LocalPathComparisonPolicy.ForCurrentPlatform;

		Assert.ThrowsExactly<ArgumentException>(() => LocalPath.NormalizeRoots([null!], policy));
		Assert.ThrowsExactly<ArgumentException>(() => LocalPath.NormalizeRoots(["  "], policy));
		Assert.ThrowsExactly<ArgumentException>(() => LocalPath.NormalizeRoots(["relative/root"], policy));
	}

	[TestMethod]
	public void NormalizeRoots_NullList_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => LocalPath.NormalizeRoots(null!, LocalPathComparisonPolicy.ForCurrentPlatform));

	[TestMethod]
	public void NormalizeRoots_InvalidArguments_NameTheCallersExpression()
	{
		LocalPathComparisonPolicy policy = LocalPathComparisonPolicy.ForCurrentPlatform;

		IReadOnlyList<string> workspaceRoots = ["relative/root"];
		IReadOnlyList<string> missingRoots = null!;

		ArgumentException invalidEntry = Assert.ThrowsExactly<ArgumentException>(() => LocalPath.NormalizeRoots(workspaceRoots, policy));

		ArgumentNullException nullList = Assert.ThrowsExactly<ArgumentNullException>(() => LocalPath.NormalizeRoots(missingRoots, policy));

		Assert.AreEqual("workspaceRoots", invalidEntry.ParamName);
		Assert.AreEqual("missingRoots", nullList.ParamName);
	}

	[TestMethod]
	public void Normalize_RelativePath_NamesThePathParameter()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => LocalPath.Normalize("relative/file.ext"));
		Assert.AreEqual("path", exception.ParamName);
	}

	[TestMethod]
	[DataRow("Folder", "Folder", true)]
	[DataRow("Folder", "Folder/child.ext", true)]
	[DataRow("Folder", "Folder/sub/child.ext", true)]
	[DataRow("Folder/sub", "Folder/sub/child.ext", true)]
	[DataRow("Folder", "FolderSibling", false)]
	[DataRow("Folder", "FolderSibling/child.ext", false)]
	[DataRow("Folder/sub", "Folder", false)]
	[DataRow("Folder", "Other/child.ext", false)]
	public void IsDirectoryOrDescendant_UnderEitherPolicy_FollowsTheSeparatorBoundary(string directoryName, string pathName, bool expected)
	{
		string directory = LocalPath.Normalize(Path.Combine(s_directory, directoryName));
		string path = LocalPath.Normalize(Path.Combine(s_directory, pathName));

		Assert.AreEqual(expected, LocalPath.IsDirectoryOrDescendant(path, directory, LocalPathComparisonPolicy.CaseSensitive));
		Assert.AreEqual(expected, LocalPath.IsDirectoryOrDescendant(path, directory, LocalPathComparisonPolicy.CaseInsensitive));
	}

	[TestMethod]
	public void IsDirectoryOrDescendant_WhenOnlyTheCasingDiffers_FollowsThePolicy()
	{
		string directory = LocalPath.Normalize(Path.Combine(s_directory, "Folder"));
		string path = LocalPath.Normalize(Path.Combine(s_directory, "FOLDER", "child.ext"));
		string sameDirectory = LocalPath.Normalize(Path.Combine(s_directory, "FOLDER"));

		Assert.IsTrue(LocalPath.IsDirectoryOrDescendant(path, directory, LocalPathComparisonPolicy.CaseInsensitive));
		Assert.IsTrue(LocalPath.IsDirectoryOrDescendant(sameDirectory, directory, LocalPathComparisonPolicy.CaseInsensitive));
		Assert.IsFalse(LocalPath.IsDirectoryOrDescendant(path, directory, LocalPathComparisonPolicy.CaseSensitive));
		Assert.IsFalse(LocalPath.IsDirectoryOrDescendant(sameDirectory, directory, LocalPathComparisonPolicy.CaseSensitive));
	}

	[TestMethod]
	public void IsDirectoryOrDescendant_ForADriveRoot_ContainsEveryPathBeneathIt()
	{
		string root = OperatingSystem.IsWindows() ? @"C:\" : "/";
		string path = LocalPath.Normalize(root + "child.ext");

		Assert.IsTrue(LocalPath.IsDirectoryOrDescendant(path, root, LocalPathComparisonPolicy.ForCurrentPlatform));
		Assert.IsTrue(LocalPath.IsDirectoryOrDescendant(root, root, LocalPathComparisonPolicy.ForCurrentPlatform));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void IsDirectoryOrDescendant_ForAShareRoot_ContainsEveryPathBeneathItButNoSibling()
	{
		string share = LocalPath.Normalize(@"\\server\share");
		string child = LocalPath.Normalize(@"\\server\share\child.ext");
		string sibling = LocalPath.Normalize(@"\\server\share2\child.ext");

		Assert.IsTrue(LocalPath.IsDirectoryOrDescendant(child, share, LocalPathComparisonPolicy.CaseInsensitive));
		Assert.IsTrue(LocalPath.IsDirectoryOrDescendant(share, share, LocalPathComparisonPolicy.CaseInsensitive));
		Assert.IsFalse(LocalPath.IsDirectoryOrDescendant(sibling, share, LocalPathComparisonPolicy.CaseInsensitive));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void IsDirectoryOrDescendant_WhenTheBoundaryIsTheAlternateSeparator_DoesNotContinueTheDirectory()
	{
		string directory = LocalPath.Normalize(Path.Combine(s_directory, "Folder"));

		// An unnormalized spelling is outside the contract; the rule matches the directory separator only.
		Assert.IsFalse(
			LocalPath.IsDirectoryOrDescendant(
				directory + Path.AltDirectorySeparatorChar + "child.ext",
				directory,
				LocalPathComparisonPolicy.CaseInsensitive
			)
		);
	}

	[TestMethod]
	public void IsDirectoryOrDescendant_NullArguments_ThrowArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			LocalPath.IsDirectoryOrDescendant(null!, s_directory, LocalPathComparisonPolicy.CaseSensitive)
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			LocalPath.IsDirectoryOrDescendant(s_directory, null!, LocalPathComparisonPolicy.CaseSensitive)
		);
	}
}
