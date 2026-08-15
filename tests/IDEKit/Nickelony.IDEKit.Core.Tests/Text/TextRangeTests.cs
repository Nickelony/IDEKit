namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextRangeTests
{
	[TestMethod]
	public void ConstructionAndGetText_ValidateZeroBasedRange()
	{
		var range = new TextRange(1, 1);

		Assert.AreEqual(2, range.EndOffset);
		Assert.AreEqual("\u00E9", range.GetText("a\u00E9bc"));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextRange(-1, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => range.GetText("a"));
	}

	[TestMethod]
	public void EqualityAndFormatting_UseOffsetAndLength()
	{
		var range = new TextRange(1, 2);
		var same = new TextRange(1, 2);
		var different = new TextRange(2, 1);

		Assert.IsTrue(range == same);
		Assert.IsFalse(range != same);
		Assert.IsTrue(range != different);
		Assert.AreEqual(range, same);
		Assert.AreEqual(range.GetHashCode(), same.GetHashCode());
		Assert.AreEqual("[1..3)", range.ToString());
	}

	[TestMethod]
	public void GetText_OutOfRange_NamesTextAndOffendingComponent()
	{
		var beyondText = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextRange(3, 0).GetText("ab"));
		var beyondRange = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextRange(1, 5).GetText("ab"));

		// The text is the argument that cannot satisfy the range, and each message identifies
		// the offending component with its value.
		Assert.AreEqual("text", beyondText.ParamName);
		Assert.AreEqual("text", beyondRange.ParamName);
		StringAssert.Contains(beyondText.Message, "offset (3)");
		StringAssert.Contains(beyondText.Message, "length 2");
		StringAssert.Contains(beyondRange.Message, "end (6)");
		StringAssert.Contains(beyondRange.Message, "length 2");
	}

	[TestMethod]
	public void Constructor_RangeEndBeyondMaximumOffset_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextRange(int.MaxValue, 1));

		// A range whose end is exactly int.MaxValue is still representable.
		var range = new TextRange(int.MaxValue - 1, 1);

		Assert.AreEqual(int.MaxValue, range.EndOffset);
	}

	[TestMethod]
	public void FitsWithin_AcceptsTheEndPositionAndRejectsARangePastTheEnd()
	{
		// An empty range at the very end of the text fits, and so does a range that ends exactly there.
		Assert.IsTrue(new TextRange(2, 0).FitsWithin(2));
		Assert.IsTrue(new TextRange(0, 2).FitsWithin(2));

		// An offset past the end and a range that extends past it do not fit.
		Assert.IsFalse(new TextRange(3, 0).FitsWithin(2));
		Assert.IsFalse(new TextRange(1, 2).FitsWithin(2));

		// A negative length is not representable, so the predicate is safe on an empty text.
		Assert.IsTrue(new TextRange(0, 0).FitsWithin(0));
		Assert.IsFalse(new TextRange(1, 0).FitsWithin(0));
	}

	[TestMethod]
	public void GetTextFromSnapshot_ReturnsTheSnapshotText()
	{
		var snapshot = new StringTextSnapshot("ab\ncd");
		var range = new TextRange(1, 3);

		Assert.AreEqual("b\nc", range.GetTextFrom(snapshot));
	}

	[TestMethod]
	public void GetTextFromSnapshot_OutOfRange_NamesSnapshotAndOffendingComponent()
	{
		var snapshot = new StringTextSnapshot("ab");
		var beyondText = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextRange(3, 0).GetTextFrom(snapshot));
		var beyondRange = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextRange(1, 5).GetTextFrom(snapshot));

		// The snapshot overload carries the same component-and-value message contract as the string
		// overload, naming the snapshot as the argument that cannot satisfy the range.
		Assert.AreEqual("snapshot", beyondText.ParamName);
		Assert.AreEqual("snapshot", beyondRange.ParamName);
		StringAssert.Contains(beyondText.Message, "offset (3)");
		StringAssert.Contains(beyondText.Message, "length 2");
		StringAssert.Contains(beyondRange.Message, "end (6)");
	}

	[TestMethod]
	public void GetTextFromSnapshot_NullSnapshot_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextRange(0, 0).GetTextFrom((ITextSnapshot)null!));
	}
}
