using System.Runtime.InteropServices;

namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class LocalPathComparisonPolicyTests
{
	[TestMethod]
	public void ForCurrentPlatform_MatchesOperatingSystemAssumption()
	{
		LocalPathComparisonPolicy comparison = LocalPathComparisonPolicy.ForCurrentPlatform;

		bool expectedIgnoreCase = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			|| RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

		Assert.AreEqual(expectedIgnoreCase, comparison.IgnoreCase);
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
		var second = LocalPathComparisonPolicy.CaseInsensitive;

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}
}
