namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class BlockCommentSyntaxTests
{
	[TestMethod]
	public void Constructor_ExposesDelimitersAndNesting()
	{
		var pair = new BlockCommentSyntax("/*", "*/", allowNesting: true);

		Assert.AreEqual("/*", pair.Open);
		Assert.AreEqual("*/", pair.Close);
		Assert.IsTrue(pair.AllowNesting);
	}

	[TestMethod]
	public void Constructor_NestingDefaultsToDisabled()
	{
		var pair = new BlockCommentSyntax("--[[", "]]");

		Assert.IsFalse(pair.AllowNesting);
	}

	[TestMethod]
	public void Constructor_NullDelimiter_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new BlockCommentSyntax(null!, "*/"));
		Assert.ThrowsExactly<ArgumentNullException>(() => new BlockCommentSyntax("/*", null!));
	}

	[TestMethod]
	public void Constructor_BlankDelimiter_Throws()
	{
		// A blank delimiter would make the pair behave like no block comments at all, so it is
		// rejected at construction.
		Assert.ThrowsExactly<ArgumentException>(() => new BlockCommentSyntax(string.Empty, "*/"));
		Assert.ThrowsExactly<ArgumentException>(() => new BlockCommentSyntax("/*", "   "));
	}
}
