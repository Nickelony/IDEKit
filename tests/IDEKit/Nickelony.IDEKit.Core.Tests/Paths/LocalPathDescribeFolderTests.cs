namespace Nickelony.IDEKit.Core.Tests;

/// <summary>Covers how a folder is described by its path below the workspace root that holds it.</summary>
[TestClass]
public sealed class LocalPathDescribeFolderTests
{
	private static readonly string s_base = Path.Combine(Path.GetTempPath(), "DescribeFolderTests");
	private static readonly string s_work = Path.Combine(s_base, "work");
	private static readonly string s_lib = Path.Combine(s_base, "lib");

	private static readonly LocalPathComparisonPolicy s_comparison = LocalPathComparisonPolicy.CaseInsensitive;

	[TestMethod]
	public void DescribeFolder_AFolderBelowTheOnlyRoot_IsItsPathBelowTheRootWithSlashes() =>
		Assert.AreEqual("src/deep", LocalPath.DescribeFolder([s_work], Path.Combine(s_work, "src", "deep"), s_comparison));

	[TestMethod]
	public void DescribeFolder_TheOnlyRootItself_HasNoDescription() => Assert.IsNull(LocalPath.DescribeFolder([s_work], s_work, s_comparison));

	[TestMethod]
	public void DescribeFolder_SeveralRoots_PutTheNameOfTheRootInFront()
	{
		string[] roots = [s_work, s_lib];

		Assert.AreEqual("lib/src", LocalPath.DescribeFolder(roots, Path.Combine(s_lib, "src"), s_comparison));
		Assert.AreEqual("work", LocalPath.DescribeFolder(roots, s_work, s_comparison));
	}

	[TestMethod]
	public void DescribeFolder_AFolderNoRootHolds_IsItsOwnPath()
	{
		string elsewhere = Path.Combine(s_base, "elsewhere", "src");

		Assert.AreEqual(elsewhere, LocalPath.DescribeFolder([s_work], elsewhere, s_comparison));
		Assert.AreEqual(elsewhere, LocalPath.DescribeFolder([], elsewhere, s_comparison));
	}

	[TestMethod]
	public void DescribeFolder_ASiblingThatSharesTheNamePrefix_IsNotBelowTheRoot()
	{
		string sibling = s_work + "-extra";
		Assert.AreEqual(sibling, LocalPath.DescribeFolder([s_work], sibling, s_comparison));
	}

	[TestMethod]
	public void DescribeFolder_TheCaseFollowsThePolicy()
	{
		string upper = Path.Combine(s_work.ToUpperInvariant(), "src");

		Assert.AreEqual("src", LocalPath.DescribeFolder([s_work], upper, LocalPathComparisonPolicy.CaseInsensitive));
		Assert.AreEqual(upper, LocalPath.DescribeFolder([s_work], upper, LocalPathComparisonPolicy.CaseSensitive));
	}

	[TestMethod]
	public void DescribeFolder_NullArguments_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => LocalPath.DescribeFolder(null!, s_work, s_comparison));
		Assert.ThrowsExactly<ArgumentNullException>(() => LocalPath.DescribeFolder([s_work], null!, s_comparison));
		Assert.ThrowsExactly<ArgumentNullException>(() => LocalPath.DescribeFolder([null!], s_work, s_comparison));
	}
}
