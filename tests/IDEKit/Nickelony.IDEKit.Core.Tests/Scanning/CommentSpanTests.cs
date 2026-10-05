namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class CommentSpanTests
{
	[TestMethod]
	public void Constructor_ConsistentOffsets_ExposeDerivedValues()
	{
		var span = new CommentSpan(TextRange.FromBounds(2, 8), delimiterStart: 3, kind: CommentKind.Line);

		Assert.AreEqual(2, span.Range.Offset);
		Assert.AreEqual(3, span.DelimiterStart);
		Assert.AreEqual(8, span.Range.EndOffset);
		Assert.AreEqual(CommentKind.Line, span.Kind);
		Assert.IsTrue(span.IsLineComment);
		Assert.IsFalse(span.IsBlockComment);
		Assert.AreEqual(6, span.Range.Length);
	}

	[TestMethod]
	public void Constructor_BlockKind_ReportsBlockComment()
	{
		var span = new CommentSpan(TextRange.FromBounds(0, 7), delimiterStart: 0, kind: CommentKind.Block);

		Assert.AreEqual(CommentKind.Block, span.Kind);
		Assert.IsTrue(span.IsBlockComment);
		Assert.IsFalse(span.IsLineComment);
	}

	[TestMethod]
	public void Constructor_DelimiterBeforeStart_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			new CommentSpan(TextRange.FromBounds(5, 10), delimiterStart: 4, kind: CommentKind.Line)
		);
	}

	[TestMethod]
	public void Constructor_DelimiterAfterEnd_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			new CommentSpan(TextRange.FromBounds(0, 4), delimiterStart: 5, kind: CommentKind.Block)
		);
	}

	[TestMethod]
	public void Constructor_DelimiterAtEitherEnd_IsAccepted()
	{
		Assert.AreEqual(5, new CommentSpan(TextRange.FromBounds(5, 10), delimiterStart: 5, kind: CommentKind.Line).DelimiterStart);
		Assert.AreEqual(10, new CommentSpan(TextRange.FromBounds(5, 10), delimiterStart: 10, kind: CommentKind.Line).DelimiterStart);
	}

	[TestMethod]
	public void ToString_HalfOpenIntervalNotation_IncludesSpanAndEnd() =>
		Assert.AreEqual("[2..8)", new CommentSpan(TextRange.FromBounds(2, 8), delimiterStart: 3, kind: CommentKind.Line).ToString());

	[TestMethod]
	public void Equality_SameOffsetsAndKind_AreEqual()
	{
		var span = new CommentSpan(TextRange.FromBounds(2, 8), delimiterStart: 3, kind: CommentKind.Line);
		var same = new CommentSpan(TextRange.FromBounds(2, 8), delimiterStart: 3, kind: CommentKind.Line);
		var differentKind = new CommentSpan(TextRange.FromBounds(2, 8), delimiterStart: 3, kind: CommentKind.Block);

		Assert.AreEqual(span, same);
		Assert.IsTrue(span == same);
		Assert.IsFalse(span != same);
		Assert.AreEqual(span.GetHashCode(), same.GetHashCode());

		Assert.AreNotEqual(span, differentKind);
		Assert.IsTrue(span != differentKind);
	}
}
