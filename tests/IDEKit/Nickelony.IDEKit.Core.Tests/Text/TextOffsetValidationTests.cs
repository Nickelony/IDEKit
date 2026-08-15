namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextOffsetValidationTests
{
	[TestMethod]
	public void ValidateOffset_EndOfText_IsAccepted()
	{
		TextOffsetValidation.ValidateOffset("abc", 3, "offset", "caret");
		TextOffsetValidation.ValidateOffset(3, 3, "offset", "caret");
	}

	[TestMethod]
	public void ValidateOffset_NegativeOffset_Throws()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextOffsetValidation.ValidateOffset(3, -1, "caretOffset", "caret"));

		Assert.AreEqual("caretOffset", exception.ParamName);
		StringAssert.Contains(exception.Message, "caret");
	}

	[TestMethod]
	public void ValidateOffset_BeyondEndOfText_Throws()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextOffsetValidation.ValidateOffset("abc", 4, "hoveredOffset", "hovered"));

		Assert.AreEqual("hoveredOffset", exception.ParamName);
	}

	[TestMethod]
	public void ValidateOffset_NullDocumentText_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextOffsetValidation.ValidateOffset(null!, 0, "offset", "caret"));
	}

	[TestMethod]
	public void ValidateRange_RangeInsideTheText_IsAccepted()
	{
		TextOffsetValidation.ValidateRange(10, new TextRange(2, 3), "range", "selection");
	}

	[TestMethod]
	public void ValidateRange_RangeEndingAtTheTextEnd_IsAccepted()
	{
		TextOffsetValidation.ValidateRange(5, new TextRange(2, 3), "range", "selection");
	}

	[TestMethod]
	public void ValidateRange_RangeBeyondTheText_Throws()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextOffsetValidation.ValidateRange(5, new TextRange(4, 2), "wordSpan", "word span"));

		Assert.AreEqual("wordSpan", exception.ParamName);
		StringAssert.Contains(exception.Message, "word span");
	}

	[TestMethod]
	public void ValidateOrderedRange_OrderedOrEmptyRange_IsAccepted()
	{
		TextOffsetValidation.ValidateOrderedRange(2, 5, "startOffset", "selection");
		TextOffsetValidation.ValidateOrderedRange(5, 5, "startOffset", "selection");
	}

	[TestMethod]
	public void ValidateOrderedRange_ReversedRange_Throws()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextOffsetValidation.ValidateOrderedRange(5, 2, "startOffset", "selection"));

		Assert.AreEqual("startOffset", exception.ParamName);
	}
}
