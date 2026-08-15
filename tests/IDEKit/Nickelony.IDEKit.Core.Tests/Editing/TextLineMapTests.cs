namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextLineMapTests
{
	[TestMethod]
	public void Accessors_ClampOutOfRangeLineIndices()
	{
		TextLineMap lineMap = TextLineMap.Build("one\ntwo");

		Assert.AreEqual(3, lineMap.GetLineLength(-1));
		Assert.AreEqual(0, lineMap.GetLineStartOffset(-1));
		Assert.AreEqual(3, lineMap.GetLineLength(99));
		Assert.AreEqual(4, lineMap.GetLineStartOffset(99));
		Assert.AreEqual("two", lineMap.GetLineText(99));
	}

	[TestMethod]
	public void Build_TreatsCrLfAndCrAsLineTerminators()
	{
		TextLineMap lineMap = TextLineMap.Build("one\r\ntwo\rthree");

		Assert.AreEqual(3, lineMap.LineCount);
		Assert.AreEqual("one", lineMap.GetLineText(0));
		Assert.AreEqual("two", lineMap.GetLineText(1));
		Assert.AreEqual("three", lineMap.GetLineText(2));
		Assert.AreEqual(9, lineMap.GetLineStartOffset(2));
	}

	[TestMethod]
	public void Accessors_ReportTheSourceTextLength()
	{
		TextLineMap lineMap = TextLineMap.Build("one\r\ntwo\rthree");

		Assert.AreEqual(14, lineMap.TextLength);
	}

	[TestMethod]
	[DataRow(3, 0, 3, DisplayName = "EndOfFirstLine")]
	[DataRow(7, 1, 3, DisplayName = "EndOfDocument")]
	public void GetPosition_MapsEndOfLineAndEndOfDocument(int offset, int line, int column)
	{
		TextLineMap lineMap = TextLineMap.Build("one\ntwo");

		Assert.AreEqual(new TextPosition(line, column), lineMap.GetPosition(offset));
	}

	[TestMethod]
	public void Build_NullContent_ProducesSingleEmptyLine()
	{
		TextLineMap lineMap = TextLineMap.Build(null);

		Assert.AreEqual(1, lineMap.LineCount);
		Assert.AreEqual(0, lineMap.TextLength);
		Assert.AreEqual(string.Empty, lineMap.GetLineText(0));
	}

	[TestMethod]
	public void Build_TrailingTerminator_YieldsFinalEmptyLine()
	{
		TextLineMap lineMap = TextLineMap.Build("abc\n");

		Assert.AreEqual(2, lineMap.LineCount);
		Assert.AreEqual("abc", lineMap.GetLineText(0));
		Assert.AreEqual(string.Empty, lineMap.GetLineText(1));
		Assert.AreEqual(4, lineMap.GetLineStartOffset(1));
	}

	[TestMethod]
	public void GetPositionAndOffset_LoneCrRoundTripsOutsideTheTerminator()
	{
		TextLineMap lineMap = TextLineMap.Build("a\rb");

		Assert.AreEqual(new TextPosition(0, 1), lineMap.GetPosition(1));
		Assert.AreEqual(new TextPosition(1, 0), lineMap.GetPosition(2));
		Assert.AreEqual(2, lineMap.GetOffset(new TextPosition(1, 0)));
	}

	[TestMethod]
	[DataRow(1, 0, 1, DisplayName = "CarriageReturn")]
	[DataRow(2, 0, 1, DisplayName = "LineFeed")]
	[DataRow(3, 1, 0, DisplayName = "FirstCharacterOfNextLine")]
	[DataRow(4, 1, 1, DisplayName = "SecondCharacterOfNextLine")]
	public void GetPosition_OffsetInsideCrLfTerminator_ClampsToLineEnd(int offset, int line, int column)
	{
		TextLineMap lineMap = TextLineMap.Build("a\r\nb");

		// Both terminator characters map to the end of the line they terminate, so the position never
		// exceeds the line length; the terminator offsets are not distinguishable by position.
		Assert.AreEqual(new TextPosition(line, column), lineMap.GetPosition(offset));
	}

	[TestMethod]
	public void GetOffset_PastLineEnd_ClampsToTheLineEnd()
	{
		TextLineMap lineMap = TextLineMap.Build("a\r\nb");

		Assert.AreEqual(1, lineMap.GetOffset(0, 2));
		Assert.AreEqual(1, lineMap.GetOffset(new TextPosition(0, 2)));
	}

	[TestMethod]
	public void Accessors_NegativeIndices_ClampToTheFirstLine()
	{
		TextLineMap lineMap = TextLineMap.Build("one\ntwo");

		Assert.AreEqual("one", lineMap.GetLineText(-1));
		Assert.AreEqual(0, lineMap.GetOffset(-1, 0));
		Assert.AreEqual(0, lineMap.GetOffset(0, -1));
		Assert.AreEqual(new TextPosition(0, 0), lineMap.GetPosition(-1));
	}

	[TestMethod]
	public void TryGetOffsets_MapsEndpointsAndClamps()
	{
		TextLineMap lineMap = TextLineMap.Build("one\ntwo");

		Assert.IsTrue(lineMap.TryGetOffsets(new TextPositionRange(new TextPosition(0, 1), new TextPosition(1, 2)), out TextRange range));
		Assert.AreEqual(new TextRange(1, 5), range);

		// Stale coordinates are clamped like GetOffset(TextPosition), so the conversion still
		// produces a representable range.
		Assert.IsTrue(lineMap.TryGetOffsets(new TextPositionRange(new TextPosition(99, 99), new TextPosition(99, 99)), out TextRange clamped));
		Assert.AreEqual(new TextRange(7, 0), clamped);
	}

	[TestMethod]
	public void TryGetOffsets_EmptyRange_SucceedsWithZeroLength()
	{
		TextLineMap lineMap = TextLineMap.Build("one\ntwo");

		Assert.IsTrue(lineMap.TryGetOffsets(new TextPositionRange(new TextPosition(0, 2), new TextPosition(0, 2)), out TextRange range));
		Assert.AreEqual(new TextRange(2, 0), range);
	}

	[TestMethod]
	public void TryGetOffsets_ReversedRange_Fails()
	{
		TextLineMap lineMap = TextLineMap.Build("one\ntwo");

		Assert.IsFalse(lineMap.TryGetOffsets(new TextPositionRange(new TextPosition(1, 0), new TextPosition(0, 0)), out TextRange range));
		Assert.AreEqual(default(TextRange), range);
	}
}
