namespace Nickelony.Matching.Tests;

[TestClass]
public sealed class EnumValueStabilityTests
{
	[TestMethod]
	public void FuzzyMatchModeValues_AreStable() => AssertPinnedValues((FuzzyMatchMode.Text, 0), (FuzzyMatchMode.Path, 1));

	/// <summary>
	/// Pins every member of one enum exactly once: an added member, a renumbered member, a duplicated pin, or a duplicated numeric value all fail
	/// the test.
	/// </summary>
	private static void AssertPinnedValues<TEnum>(params (TEnum Member, int Value)[] pinned)
		where TEnum : struct, Enum
	{
		TEnum[] members = Enum.GetValues<TEnum>();

		Assert.HasCount(members.Length, pinned, $"Every {typeof(TEnum).Name} member must be pinned exactly once.");

		var pinnedNames = new HashSet<string>(StringComparer.Ordinal);
		var pinnedValues = new HashSet<int>();

		foreach ((TEnum member, int value) in pinned)
		{
			string name = member.ToString();

			Assert.IsTrue(pinnedNames.Add(name), $"The {typeof(TEnum).Name} member {name} is pinned more than once.");
			Assert.AreEqual(value, Convert.ToInt32(member), $"The value of {typeof(TEnum).Name}.{name} changed.");
			Assert.IsTrue(pinnedValues.Add(value), $"The value {value} is used by more than one {typeof(TEnum).Name} member.");
		}

		foreach (TEnum member in members)
			Assert.Contains(member.ToString(), pinnedNames, $"The {typeof(TEnum).Name} member {member} is not pinned.");
	}
}
