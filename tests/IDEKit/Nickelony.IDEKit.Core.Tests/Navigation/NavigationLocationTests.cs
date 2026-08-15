namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class NavigationLocationTests
{
	private static NavigationLocation CreateLocation(string filePath, int caretOffset)
		=> new(filePath, caretOffset, caretOffset, 0, null);

	[TestMethod]
	public void IsEquivalentTo_DefaultComparison_IsOrdinalAndIgnoresPreferredDocumentLine()
	{
		var first = new NavigationLocation("Path.lua", 5, 5, 3, 2);
		var second = new NavigationLocation("path.lua", 5, 5, 3, 9);
		var samePath = new NavigationLocation("Path.lua", 5, 5, 3, 9);

		Assert.IsFalse(first.IsEquivalentTo(second));
		Assert.IsTrue(first.IsEquivalentTo(second, StringComparison.OrdinalIgnoreCase));
		Assert.IsTrue(first.IsEquivalentTo(samePath));
	}

	[TestMethod]
	public void IsEquivalentTo_OrdinalComparison_DistinguishesPathCase()
	{
		var first = CreateLocation("Path.lua", 5);
		var second = CreateLocation("path.lua", 5);

		Assert.IsFalse(first.IsEquivalentTo(second, StringComparison.Ordinal));
		Assert.IsTrue(first.IsEquivalentTo(second, StringComparison.OrdinalIgnoreCase));
	}

	[TestMethod]
	public void IsEquivalentTo_ComparesCaretAndSelection()
	{
		var location = new NavigationLocation("a.lua", 5, 5, 3, null);

		Assert.IsFalse(location.IsEquivalentTo(new NavigationLocation("a.lua", 6, 5, 3, null)));
		Assert.IsFalse(location.IsEquivalentTo(new NavigationLocation("a.lua", 5, 4, 3, null)));
		Assert.IsFalse(location.IsEquivalentTo(new NavigationLocation("a.lua", 5, 5, 4, null)));
		Assert.IsTrue(location.IsEquivalentTo(new NavigationLocation("a.lua", 5, 5, 3, 42)));
	}

	[TestMethod]
	public void Equals_IgnoresPreferredDocumentLine()
	{
		// Equality is the navigation identity; the preferred line is scroll restoration state carried
		// alongside it. Untitled documents carry a null file path.
		var first = new NavigationLocation(null, 5, 5, 3, 1);
		var second = new NavigationLocation(null, 5, 5, 3, 2);

		Assert.IsTrue(first.Equals(second));
		Assert.IsTrue(first == second);
		Assert.IsTrue(first.IsEquivalentTo(second));
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equals_DistinguishesIdentityFields()
	{
		var location = new NavigationLocation("a.lua", 5, 5, 3, null);

		Assert.IsFalse(location.Equals(new NavigationLocation("b.lua", 5, 5, 3, null)));
		Assert.IsFalse(location.Equals(new NavigationLocation("a.lua", 6, 5, 3, null)));
		Assert.IsFalse(location.Equals(new NavigationLocation("a.lua", 5, 4, 3, null)));
		Assert.IsFalse(location.Equals(new NavigationLocation("a.lua", 5, 5, 4, null)));
	}

	[TestMethod]
	public void Equals_FilePathCase_IsOrdinal()
	{
		// Hosts with case-insensitive document identities compare with IsEquivalentTo instead.
		var first = new NavigationLocation("Path.lua", 5, 5, 3, null);
		var second = new NavigationLocation("path.lua", 5, 5, 3, null);

		Assert.IsFalse(first.Equals(second));
		Assert.IsTrue(first.IsEquivalentTo(second, StringComparison.OrdinalIgnoreCase));
	}
}
