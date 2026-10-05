namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextOffsetValidationTests
{
	[TestMethod]
	public void ValidateOffset_EndOfText_IsAccepted()
	{
		TextOffsetValidation.ValidateOffset(documentText: "abc", offset: 3, parameterName: "offset", offsetDescription: "caret");
		TextOffsetValidation.ValidateOffset(textLength: 3, offset: 3, parameterName: "offset", offsetDescription: "caret");
	}

	[TestMethod]
	public void ValidateOffset_NegativeOffset_Throws()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			TextOffsetValidation.ValidateOffset(textLength: 3, offset: -1, parameterName: "caretOffset", offsetDescription: "caret")
		);

		Assert.AreEqual("caretOffset", exception.ParamName);
		Assert.Contains("caret", exception.Message);
	}

	[TestMethod]
	public void ValidateOffset_BeyondEndOfText_Throws()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			TextOffsetValidation.ValidateOffset(documentText: "abc", offset: 4, parameterName: "hoveredOffset", offsetDescription: "hovered")
		);

		Assert.AreEqual("hoveredOffset", exception.ParamName);
	}

	[TestMethod]
	public void ValidateOffset_NegativeTextLength_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			TextOffsetValidation.ValidateOffset(textLength: -1, offset: 0, parameterName: "offset", offsetDescription: "caret")
		);
	}

	[TestMethod]
	public void ValidateOffset_NullDocumentText_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			TextOffsetValidation.ValidateOffset(documentText: null!, offset: 0, parameterName: "offset", offsetDescription: "caret")
		);
	}

	[TestMethod]
	public void ValidateRange_RangeInsideTheText_IsAccepted() =>
		TextOffsetValidation.ValidateRange(textLength: 10, range: new TextRange(2, 3), parameterName: "range", rangeDescription: "selection");

	[TestMethod]
	public void ValidateRange_RangeEndingAtTheTextEnd_IsAccepted() =>
		TextOffsetValidation.ValidateRange(textLength: 5, range: new TextRange(2, 3), parameterName: "range", rangeDescription: "selection");

	[TestMethod]
	public void ValidateRange_NegativeTextLength_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			TextOffsetValidation.ValidateRange(textLength: -1, range: new TextRange(0, 0), parameterName: "range", rangeDescription: "selection")
		);
	}

	[TestMethod]
	public void ValidateRange_RangeBeyondTheText_Throws()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			TextOffsetValidation.ValidateRange(textLength: 5, range: new TextRange(4, 2), parameterName: "wordSpan", rangeDescription: "word span")
		);

		Assert.AreEqual("wordSpan", exception.ParamName);
		Assert.Contains("word span", exception.Message);
	}
}
