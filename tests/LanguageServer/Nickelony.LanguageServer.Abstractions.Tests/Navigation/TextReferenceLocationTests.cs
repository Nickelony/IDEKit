using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions.Tests;

[TestClass]
public sealed class TextReferenceLocationTests
{
	[TestMethod]
	public void Constructor_StoresFilePathAndRange()
	{
		var range = new TextPositionRange(new TextPosition(2, 3), new TextPosition(2, 11));
		var location = new TextReferenceLocation("scripts/objects.lua", range);

		Assert.AreEqual("scripts/objects.lua", location.FilePath);
		Assert.AreEqual(range, location.Range);
	}

	[TestMethod]
	public void Constructor_ReversedRange_IsStoredAsSupplied()
	{
		var range = new TextPositionRange(new TextPosition(2, 11), new TextPosition(2, 3));
		var location = new TextReferenceLocation("scripts/objects.lua", range);

		Assert.AreEqual(range, location.Range);
	}

	[TestMethod]
	public void Constructor_NullFilePath_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextReferenceLocation(null!, default));
	}

	[TestMethod]
	public void Equals_SameValues_AreEqual()
	{
		var range = new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 8));
		var first = new TextReferenceLocation("doc.lua", range);
		var second = new TextReferenceLocation("doc.lua", range);

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equals_DifferentFilePath_AreNotEqual()
	{
		var range = new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 8));

		Assert.AreNotEqual(new TextReferenceLocation("a.lua", range), new TextReferenceLocation("b.lua", range));
	}
}
