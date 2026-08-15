namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class CommentSpanTests
{
	[TestMethod]
	public void Constructor_ConsistentOffsets_ExposeDerivedValues()
	{
		var span = new CommentSpan(2, 3, 8, CommentKind.Line);

		Assert.AreEqual(2, span.StartOffset);
		Assert.AreEqual(3, span.DelimiterStart);
		Assert.AreEqual(8, span.EndOffset);
		Assert.AreEqual(CommentKind.Line, span.Kind);
		Assert.IsTrue(span.IsLineComment);
		Assert.IsFalse(span.IsBlockComment);
		Assert.AreEqual(6, span.Length);
	}

	[TestMethod]
	public void Constructor_BlockKind_ReportsBlockComment()
	{
		var span = new CommentSpan(0, 0, 7, CommentKind.Block);

		Assert.AreEqual(CommentKind.Block, span.Kind);
		Assert.IsTrue(span.IsBlockComment);
		Assert.IsFalse(span.IsLineComment);
	}

	[TestMethod]
	public void Constructor_NegativeStart_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CommentSpan(-1, 0, 1, CommentKind.Block));
	}

	[TestMethod]
	public void Constructor_DelimiterBeforeStart_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CommentSpan(5, 4, 10, CommentKind.Line));
	}

	[TestMethod]
	public void Constructor_EndBeforeDelimiter_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CommentSpan(0, 5, 4, CommentKind.Block));
	}

	[TestMethod]
	public void ToString_HalfOpenIntervalNotation_IncludesSpanAndEnd()
	{
		Assert.AreEqual("[2..8)", new CommentSpan(2, 3, 8, CommentKind.Line).ToString());
	}

	[TestMethod]
	public void Equality_SameOffsetsAndKind_AreEqual()
	{
		var span = new CommentSpan(2, 3, 8, CommentKind.Line);
		var same = new CommentSpan(2, 3, 8, CommentKind.Line);
		var differentKind = new CommentSpan(2, 3, 8, CommentKind.Block);

		Assert.AreEqual(span, same);
		Assert.IsTrue(span == same);
		Assert.IsFalse(span != same);
		Assert.AreEqual(span.GetHashCode(), same.GetHashCode());

		Assert.AreNotEqual(span, differentKind);
		Assert.IsTrue(span != differentKind);
	}
}
