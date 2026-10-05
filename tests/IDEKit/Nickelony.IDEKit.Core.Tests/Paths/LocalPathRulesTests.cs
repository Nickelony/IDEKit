using Nickelony.Infrastructure;

namespace Nickelony.IDEKit.Core.Tests;

/// <summary>Pins the local-path identity rules that Core and Client compile from one source file.</summary>
[TestClass]
public sealed class LocalPathRulesTests
{
	private static readonly string s_root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "LocalPathRulesTests"));

	[TestMethod]
	public void DefaultComparer_FollowsTheOperatingSystemCaseDefault()
	{
		bool ignoresCase = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

		Assert.AreEqual(ignoresCase, LocalPathRules.IgnoresCaseByDefault);
		Assert.AreEqual(ignoresCase, LocalPathRules.DefaultComparer.Equals("Ab", "aB"));
	}

	[TestMethod]
	public void Normalize_ResolvesDotSegmentsAndTrimsTheTrailingSeparator()
	{
		string path = Path.Combine(Path.Combine(s_root, "a", ".."), "b") + Path.DirectorySeparatorChar;
		Assert.AreEqual(Path.Combine(s_root, "b"), LocalPathRules.Normalize(path));
	}

	[TestMethod]
	public void Normalize_UnifiesTheAlternateSeparator()
	{
		string path = s_root.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.AltDirectorySeparatorChar + "child";
		Assert.AreEqual(Path.Combine(s_root, "child"), LocalPathRules.Normalize(path));
	}

	[TestMethod]
	public void Normalize_KeepsTheCasingOfTheInput() =>
		Assert.AreEqual(Path.Combine(s_root, "MixedCase.TXT"), LocalPathRules.Normalize(Path.Combine(s_root, "MixedCase.TXT")));

	[TestMethod]
	public void Normalize_ANullPath_ThrowsAndNamesTheArgumentExpression()
	{
		string missing = null!;

		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() => LocalPathRules.Normalize(missing));

		Assert.AreEqual("missing", exception.ParamName);
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("   ")]
	public void Normalize_ABlankPath_Throws(string blank)
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => LocalPathRules.Normalize(blank));

		Assert.AreEqual("blank", exception.ParamName);
		Assert.Contains("must not be empty", exception.Message);
	}

	[TestMethod]
	public void Normalize_ARelativePath_ThrowsInsteadOfBindingToTheCurrentDirectory()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
			LocalPathRules.Normalize(Path.Combine("relative", "file.txt"), "workspacePath")
		);

		Assert.AreEqual("workspacePath", exception.ParamName);
		Assert.Contains("must be fully qualified", exception.Message);
	}

	[TestMethod]
	public void TryNormalize_AFullyQualifiedPath_ReturnsTheNormalizedPath()
	{
		Assert.IsTrue(LocalPathRules.TryNormalize(Path.Combine(Path.Combine(s_root, "a", ".."), "b"), out string normalized));
		Assert.AreEqual(Path.Combine(s_root, "b"), normalized);
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("   ")]
	[DataRow("relative/file.txt")]
	public void TryNormalize_ABlankOrRelativePath_ReturnsFalseWithAnEmptyPath(string? path)
	{
		Assert.IsFalse(LocalPathRules.TryNormalize(path, out string normalized));
		Assert.AreEqual(string.Empty, normalized);
	}

	[TestMethod]
	public void NormalizeRoots_KeepsCallerOrderAndCasingAndTrimsSeparators()
	{
		string[] roots = [Path.Combine(s_root, "Zeta") + Path.DirectorySeparatorChar, Path.Combine(s_root, "alpha")];

		string[] normalized = LocalPathRules.NormalizeRoots(roots, StringComparer.Ordinal, "roots");

		Assert.AreSequenceEqual([Path.Combine(s_root, "Zeta"), Path.Combine(s_root, "alpha")], normalized);
	}

	[TestMethod]
	public void NormalizeRoots_AnEmptyList_IsAFolderlessSession() =>
		Assert.IsEmpty(LocalPathRules.NormalizeRoots([], StringComparer.Ordinal, "roots"));

	[TestMethod]
	public void NormalizeRoots_RootsThatDifferOnlyByCase_FollowTheComparer()
	{
		string[] roots = [Path.Combine(s_root, "Case"), Path.Combine(s_root, "CASE")];

		Assert.HasCount(2, LocalPathRules.NormalizeRoots(roots, StringComparer.Ordinal, "roots"));

		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
			LocalPathRules.NormalizeRoots(roots, StringComparer.OrdinalIgnoreCase, "workspaceRoots")
		);

		Assert.AreEqual("workspaceRoots", exception.ParamName);
		Assert.Contains("duplicate path", exception.Message);
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("   ")]
	[DataRow("relative/root")]
	public void NormalizeRoots_ABlankOrRelativeRoot_ThrowsWithTheCallersParameterName(string root)
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
			LocalPathRules.NormalizeRoots([Path.Combine(s_root, "ok"), root], StringComparer.Ordinal, "workspaceRoots")
		);

		Assert.AreEqual("workspaceRoots", exception.ParamName);
	}

	[TestMethod]
	public void NormalizeRoots_ANullListOrComparer_Throws()
	{
		ArgumentNullException list = Assert.ThrowsExactly<ArgumentNullException>(() =>
			LocalPathRules.NormalizeRoots(null!, StringComparer.Ordinal, "roots")
		);

		ArgumentNullException comparer = Assert.ThrowsExactly<ArgumentNullException>(() => LocalPathRules.NormalizeRoots([], null!, "roots"));

		Assert.AreEqual("roots", list.ParamName);
		Assert.AreEqual("comparer", comparer.ParamName);
	}
}
