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
		ArgumentOutOfRangeException beyondText = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextRange(3, 0).GetText("ab"));
		ArgumentOutOfRangeException beyondRange = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextRange(1, 5).GetText("ab"));

		// The text is the argument that cannot satisfy the range, and each message identifies
		// the offending component with its value.
		Assert.AreEqual("text", beyondText.ParamName);
		Assert.AreEqual("text", beyondRange.ParamName);
		Assert.Contains("offset (3)", beyondText.Message);
		Assert.Contains("length 2", beyondText.Message);
		Assert.Contains("end (6)", beyondRange.Message);
		Assert.Contains("length 2", beyondRange.Message);
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

		// A negative text length, which only a faulty snapshot reports, contains no range.
		Assert.IsFalse(new TextRange(0, 0).FitsWithin(-1));
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

		ArgumentOutOfRangeException beyondText = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextRange(3, 0).GetTextFrom(snapshot));
		ArgumentOutOfRangeException beyondRange = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextRange(1, 5).GetTextFrom(snapshot));

		// The snapshot overload carries the same component-and-value message contract as the string
		// overload, naming the snapshot as the argument that cannot satisfy the range.
		Assert.AreEqual("snapshot", beyondText.ParamName);
		Assert.AreEqual("snapshot", beyondRange.ParamName);
		Assert.Contains("offset (3)", beyondText.Message);
		Assert.Contains("length 2", beyondText.Message);
		Assert.Contains("end (6)", beyondRange.Message);
	}

	[TestMethod]
	public void GetTextFromSnapshot_NullSnapshot_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextRange(0, 0).GetTextFrom((ITextSnapshot)null!));

	[TestMethod]
	public void FromBounds_StartAndEnd_BuildsTheRange()
	{
		Assert.AreEqual(new TextRange(2, 3), TextRange.FromBounds(2, 5));
		Assert.AreEqual(new TextRange(4, 0), TextRange.FromBounds(4, 4));
		Assert.AreEqual(new TextRange(int.MaxValue, 0), TextRange.FromBounds(int.MaxValue, int.MaxValue));
	}

	[TestMethod]
	public void FromBounds_InvalidBounds_NamesTheOffendingArgument()
	{
		ArgumentOutOfRangeException negativeStart = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TextRange.FromBounds(-1, 3));
		ArgumentOutOfRangeException reversed = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TextRange.FromBounds(5, 2));

		Assert.AreEqual("startOffset", negativeStart.ParamName);
		Assert.AreEqual("endOffset", reversed.ParamName);
	}

	[TestMethod]
	public void Intersects_OverlappingRanges_IsTrueInBothDirections()
	{
		var range = TextRange.FromBounds(5, 10);

		Assert.IsTrue(range.Intersects(TextRange.FromBounds(0, 6)));
		Assert.IsTrue(range.Intersects(TextRange.FromBounds(9, 20)));
		Assert.IsTrue(range.Intersects(TextRange.FromBounds(6, 8)));
		Assert.IsTrue(TextRange.FromBounds(0, 6).Intersects(range));
	}

	[TestMethod]
	public void Intersects_TouchingRanges_IsTrue()
	{
		var range = TextRange.FromBounds(5, 10);

		Assert.IsTrue(range.Intersects(TextRange.FromBounds(0, 5)));
		Assert.IsTrue(range.Intersects(TextRange.FromBounds(10, 20)));
		Assert.IsTrue(TextRange.FromBounds(10, 20).Intersects(range));
	}

	[TestMethod]
	public void Intersects_EmptyRange_MatchesInsideAndAtEitherBoundaryOfTheOther()
	{
		var range = TextRange.FromBounds(5, 10);

		Assert.IsTrue(range.Intersects(new TextRange(5, 0)));
		Assert.IsTrue(range.Intersects(new TextRange(7, 0)));
		Assert.IsTrue(range.Intersects(new TextRange(10, 0)));
		Assert.IsTrue(new TextRange(7, 0).Intersects(range));
	}

	[TestMethod]
	public void Intersects_DisjointRanges_IsFalse()
	{
		var range = TextRange.FromBounds(5, 10);

		Assert.IsFalse(range.Intersects(TextRange.FromBounds(0, 4)));
		Assert.IsFalse(range.Intersects(TextRange.FromBounds(11, 20)));
		Assert.IsFalse(range.Intersects(new TextRange(4, 0)));
		Assert.IsFalse(range.Intersects(new TextRange(11, 0)));
		Assert.IsFalse(new TextRange(11, 0).Intersects(range));
	}

	[TestMethod]
	public void Intersects_TwoEmptyRanges_MatchWhenTheyShareAnOffset()
	{
		Assert.IsTrue(new TextRange(7, 0).Intersects(new TextRange(7, 0)));
		Assert.IsFalse(new TextRange(7, 0).Intersects(new TextRange(8, 0)));
	}

	[TestMethod]
	public void Intersects_RangesAtTheMaximumOffset_DoNotOverflow()
	{
		var atMaximum = new TextRange(int.MaxValue, 0);

		Assert.IsTrue(atMaximum.Intersects(TextRange.FromBounds(int.MaxValue - 1, int.MaxValue)));
		Assert.IsFalse(atMaximum.Intersects(TextRange.FromBounds(0, 5)));
	}

	[TestMethod]
	public void IsEmpty_ReflectsZeroLength()
	{
		Assert.IsTrue(new TextRange(4, 0).IsEmpty);
		Assert.IsFalse(new TextRange(4, 1).IsEmpty);
	}

	[TestMethod]
	public void ToString_UsesHalfOpenIntervalNotation() => Assert.AreEqual("[4..10)", new TextRange(4, 6).ToString());
}
