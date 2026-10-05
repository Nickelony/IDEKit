using System.Runtime.InteropServices;

namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class LocalPathComparisonPolicyTests
{
	[TestMethod]
	public void ForCurrentPlatform_MatchesOperatingSystemAssumption()
	{
		LocalPathComparisonPolicy comparison = LocalPathComparisonPolicy.ForCurrentPlatform;

		bool expectedIgnoreCase = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

		Assert.AreEqual(expectedIgnoreCase, comparison.IgnoreCase);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows | OperatingSystems.OSX)]
	public void ForCurrentPlatform_OnACaseInsensitivePlatform_TreatsCasingVariantsAsEqual()
	{
		string lowerCasePath = LocalPath.Normalize(Path.Combine(Path.GetTempPath(), "Path Helper", "case.ext"));
		string upperCasePath = LocalPath.Normalize(Path.Combine(Path.GetTempPath(), "Path Helper", "CASE.ext"));

		Assert.IsTrue(LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(lowerCasePath, upperCasePath));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Linux)]
	public void ForCurrentPlatform_OnACaseSensitivePlatform_TreatsCasingVariantsAsDistinct()
	{
		string lowerCasePath = LocalPath.Normalize(Path.Combine(Path.GetTempPath(), "Path Helper", "case.ext"));
		string upperCasePath = LocalPath.Normalize(Path.Combine(Path.GetTempPath(), "Path Helper", "CASE.ext"));

		Assert.IsFalse(LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(lowerCasePath, upperCasePath));
	}

	[TestMethod]
	public void CaseInsensitive_UsesOrdinalIgnoreCaseProjections()
	{
		LocalPathComparisonPolicy comparison = LocalPathComparisonPolicy.CaseInsensitive;

		Assert.IsTrue(comparison.IgnoreCase);
		Assert.AreEqual(StringComparison.OrdinalIgnoreCase, comparison.Comparison);
		Assert.AreSame(StringComparer.OrdinalIgnoreCase, comparison.Comparer);
	}

	[TestMethod]
	public void CaseSensitive_UsesOrdinalProjections()
	{
		LocalPathComparisonPolicy comparison = LocalPathComparisonPolicy.CaseSensitive;

		Assert.IsFalse(comparison.IgnoreCase);
		Assert.AreEqual(StringComparison.Ordinal, comparison.Comparison);
		Assert.AreSame(StringComparer.Ordinal, comparison.Comparer);
	}

	[TestMethod]
	public void Values_WithSameIgnoreCase_AreEqual()
	{
		var first = new LocalPathComparisonPolicy(true);

		LocalPathComparisonPolicy second = LocalPathComparisonPolicy.CaseInsensitive;

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}
}
