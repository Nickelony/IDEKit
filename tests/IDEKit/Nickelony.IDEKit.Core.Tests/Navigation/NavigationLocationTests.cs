namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class NavigationLocationTests
{
	private static NavigationLocation CreateLocation(string documentId, int caretOffset) =>
		new(documentId, caretOffset, Selection: new TextRange(caretOffset, 0), PreferredDocumentLine: null);

	[TestMethod]
	public void IsEquivalentTo_DefaultComparison_IsOrdinalAndIgnoresPreferredDocumentLine()
	{
		var first = new NavigationLocation(DocumentId: "Path.lua", CaretOffset: 5, Selection: new TextRange(5, 3), PreferredDocumentLine: 2);
		var second = new NavigationLocation(DocumentId: "path.lua", CaretOffset: 5, Selection: new TextRange(5, 3), PreferredDocumentLine: 9);
		var samePath = new NavigationLocation(DocumentId: "Path.lua", CaretOffset: 5, Selection: new TextRange(5, 3), PreferredDocumentLine: 9);

		Assert.IsFalse(first.IsEquivalentTo(second));
		Assert.IsTrue(first.IsEquivalentTo(second, StringComparison.OrdinalIgnoreCase));
		Assert.IsTrue(first.IsEquivalentTo(samePath));
	}

	[TestMethod]
	public void IsEquivalentTo_OrdinalComparison_DistinguishesPathCase()
	{
		NavigationLocation first = CreateLocation("Path.lua", 5);
		NavigationLocation second = CreateLocation("path.lua", 5);

		Assert.IsFalse(first.IsEquivalentTo(second, StringComparison.Ordinal));
		Assert.IsTrue(first.IsEquivalentTo(second, StringComparison.OrdinalIgnoreCase));
	}

	[TestMethod]
	public void IsEquivalentTo_ComparesCaretAndSelection()
	{
		var location = new NavigationLocation(DocumentId: "a.lua", CaretOffset: 5, Selection: new TextRange(5, 3), PreferredDocumentLine: null);

		Assert.IsFalse(
			location.IsEquivalentTo(
				new NavigationLocation(DocumentId: "a.lua", CaretOffset: 6, Selection: new TextRange(5, 3), PreferredDocumentLine: null)
			)
		);

		Assert.IsFalse(
			location.IsEquivalentTo(
				new NavigationLocation(DocumentId: "a.lua", CaretOffset: 5, Selection: new TextRange(4, 3), PreferredDocumentLine: null)
			)
		);

		Assert.IsFalse(
			location.IsEquivalentTo(
				new NavigationLocation(DocumentId: "a.lua", CaretOffset: 5, Selection: new TextRange(5, 4), PreferredDocumentLine: null)
			)
		);

		Assert.IsTrue(
			location.IsEquivalentTo(
				new NavigationLocation(DocumentId: "a.lua", CaretOffset: 5, Selection: new TextRange(5, 3), PreferredDocumentLine: 42)
			)
		);
	}

	[TestMethod]
	public void Equals_IgnoresPreferredDocumentLine()
	{
		// Equality is the navigation identity; the preferred line is scroll restoration state carried
		// alongside it. Untitled documents carry a null file path.
		var first = new NavigationLocation(DocumentId: null, CaretOffset: 5, Selection: new TextRange(5, 3), PreferredDocumentLine: 1);
		var second = new NavigationLocation(DocumentId: null, CaretOffset: 5, Selection: new TextRange(5, 3), PreferredDocumentLine: 2);

		Assert.IsTrue(first.Equals(second));
		Assert.IsTrue(first == second);
		Assert.IsTrue(first.IsEquivalentTo(second));
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equals_DistinguishesIdentityFields()
	{
		var location = new NavigationLocation(DocumentId: "a.lua", CaretOffset: 5, Selection: new TextRange(5, 3), PreferredDocumentLine: null);

		Assert.IsFalse(
			location.Equals(new NavigationLocation(DocumentId: "b.lua", CaretOffset: 5, Selection: new TextRange(5, 3), PreferredDocumentLine: null))
		);

		Assert.IsFalse(
			location.Equals(new NavigationLocation(DocumentId: "a.lua", CaretOffset: 6, Selection: new TextRange(5, 3), PreferredDocumentLine: null))
		);

		Assert.IsFalse(
			location.Equals(new NavigationLocation(DocumentId: "a.lua", CaretOffset: 5, Selection: new TextRange(4, 3), PreferredDocumentLine: null))
		);

		Assert.IsFalse(
			location.Equals(new NavigationLocation(DocumentId: "a.lua", CaretOffset: 5, Selection: new TextRange(5, 4), PreferredDocumentLine: null))
		);
	}

	[TestMethod]
	public void Equals_FilePathCase_IsOrdinal()
	{
		// Hosts with case-insensitive document identities compare with IsEquivalentTo instead.
		var first = new NavigationLocation(DocumentId: "Path.lua", CaretOffset: 5, Selection: new TextRange(5, 3), PreferredDocumentLine: null);
		var second = new NavigationLocation(DocumentId: "path.lua", CaretOffset: 5, Selection: new TextRange(5, 3), PreferredDocumentLine: null);

		Assert.IsFalse(first.Equals(second));
		Assert.IsTrue(first.IsEquivalentTo(second, StringComparison.OrdinalIgnoreCase));
	}
}
