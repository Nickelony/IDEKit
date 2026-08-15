using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions.Tests;

[TestClass]
public sealed class TextEditTests
{
	[TestMethod]
	public void Constructor_EmptyReplacementText_IsAcceptedAsDeletion()
	{
		var range = new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 3));
		var edit = new TextEdit(range, string.Empty);

		Assert.AreEqual(string.Empty, edit.NewText);
		Assert.AreEqual(range, edit.Range);
	}

	[TestMethod]
	public void Constructor_ZeroLengthRange_IsAcceptedAsInsertion()
	{
		var range = new TextPositionRange(new TextPosition(1, 2), new TextPosition(1, 2));
		var edit = new TextEdit(range, "inserted");

		Assert.AreEqual(range, edit.Range);
		Assert.AreEqual("inserted", edit.NewText);
	}

	[TestMethod]
	public void Constructor_NullReplacementText_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextEdit(default, null!));
	}

	[TestMethod]
	public void Equals_SameValues_AreEqual()
	{
		var range = new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1));
		var first = new TextEdit(range, "text");
		var second = new TextEdit(range, "text");

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equals_DifferentReplacementText_AreNotEqual()
	{
		var range = new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1));

		Assert.AreNotEqual(new TextEdit(range, "first"), new TextEdit(range, "second"));
	}

	[TestMethod]
	public void Equals_DifferentRange_AreNotEqual()
	{
		var first = new TextEdit(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1)), "text");
		var second = new TextEdit(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 2)), "text");

		Assert.AreNotEqual(first, second);
	}
}
