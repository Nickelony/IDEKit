using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class ReferenceLocationTests
{
	[TestMethod]
	public void Constructor_StoresDocumentIdAndRange()
	{
		var range = new TextPositionRange(new TextPosition(2, 3), new TextPosition(2, 11));
		var location = new ReferenceLocation("scripts/objects.lua", range);

		Assert.AreEqual("scripts/objects.lua", location.DocumentId);
		Assert.AreEqual(range, location.Range);
	}

	[TestMethod]
	public void Constructor_ReversedRange_IsStoredAsSupplied()
	{
		var range = new TextPositionRange(new TextPosition(2, 11), new TextPosition(2, 3));
		var location = new ReferenceLocation("scripts/objects.lua", range);

		Assert.AreEqual(range, location.Range);
	}

	[TestMethod]
	public void Constructor_NullDocumentId_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new ReferenceLocation(null!, default));

	[TestMethod]
	public void Equals_SameValues_AreEqual()
	{
		var range = new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 8));
		var first = new ReferenceLocation("doc.lua", range);
		var second = new ReferenceLocation("doc.lua", range);

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equals_DifferentDocumentId_AreNotEqual()
	{
		var range = new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 8));
		Assert.AreNotEqual(new ReferenceLocation("a.lua", range), new ReferenceLocation("b.lua", range));
	}
}
