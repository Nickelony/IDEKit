namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextLineMapTests
{
	[TestMethod]
	public void Accessors_ClampOutOfRangeLineIndices()
	{
		var lineMap = TextLineMap.Build("one\ntwo");

		Assert.AreEqual(3, lineMap.GetLineLength(-1));
		Assert.AreEqual(0, lineMap.GetLineStartOffset(-1));
		Assert.AreEqual(3, lineMap.GetLineLength(99));
		Assert.AreEqual(4, lineMap.GetLineStartOffset(99));
		Assert.AreEqual("two", lineMap.GetLineText(99));
	}

	[TestMethod]
	public void Build_TreatsCrLfAndCrAsLineTerminators()
	{
		var lineMap = TextLineMap.Build("one\r\ntwo\rthree");

		Assert.AreEqual(3, lineMap.LineCount);
		Assert.AreEqual("one", lineMap.GetLineText(0));
		Assert.AreEqual("two", lineMap.GetLineText(1));
		Assert.AreEqual("three", lineMap.GetLineText(2));
		Assert.AreEqual(9, lineMap.GetLineStartOffset(2));
	}

	[TestMethod]
	public void Accessors_ReportTheSourceTextLength()
	{
		var lineMap = TextLineMap.Build("one\r\ntwo\rthree");
		Assert.AreEqual(14, lineMap.TextLength);
	}

	[TestMethod]
	[DataRow(3, 0, 3, DisplayName = "EndOfFirstLine")]
	[DataRow(7, 1, 3, DisplayName = "EndOfDocument")]
	public void GetPosition_MapsEndOfLineAndEndOfDocument(int offset, int line, int column)
	{
		var lineMap = TextLineMap.Build("one\ntwo");
		Assert.AreEqual(new TextPosition(line, column), lineMap.GetPosition(offset));
	}

	[TestMethod]
	public void Build_NullContent_ProducesSingleEmptyLine()
	{
		var lineMap = TextLineMap.Build(null);

		Assert.AreEqual(1, lineMap.LineCount);
		Assert.AreEqual(0, lineMap.TextLength);
		Assert.AreEqual(string.Empty, lineMap.GetLineText(0));
	}

	[TestMethod]
	public void Build_TrailingTerminator_YieldsFinalEmptyLine()
	{
		var lineMap = TextLineMap.Build("abc\n");

		Assert.AreEqual(2, lineMap.LineCount);
		Assert.AreEqual("abc", lineMap.GetLineText(0));
		Assert.AreEqual(string.Empty, lineMap.GetLineText(1));
		Assert.AreEqual(4, lineMap.GetLineStartOffset(1));
	}

	[TestMethod]
	public void GetPositionAndOffset_LoneCrRoundTripsOutsideTheTerminator()
	{
		var lineMap = TextLineMap.Build("a\rb");

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
		var lineMap = TextLineMap.Build("a\r\nb");

		// Both terminator characters map to the end of the line they terminate, so the position never
		// exceeds the line length; the terminator offsets are not distinguishable by position.
		Assert.AreEqual(new TextPosition(line, column), lineMap.GetPosition(offset));
	}

	[TestMethod]
	public void GetOffset_PastLineEnd_ClampsToTheLineEnd()
	{
		var lineMap = TextLineMap.Build("a\r\nb");

		Assert.AreEqual(1, lineMap.GetOffset(0, 2));
		Assert.AreEqual(1, lineMap.GetOffset(new TextPosition(0, 2)));
	}

	[TestMethod]
	public void Accessors_NegativeIndices_ClampToTheFirstLine()
	{
		var lineMap = TextLineMap.Build("one\ntwo");

		Assert.AreEqual("one", lineMap.GetLineText(-1));
		Assert.AreEqual(0, lineMap.GetOffset(-1, 0));
		Assert.AreEqual(0, lineMap.GetOffset(0, -1));
		Assert.AreEqual(new TextPosition(0, 0), lineMap.GetPosition(-1));
	}

	[TestMethod]
	public void TryGetOffsets_MapsEndpointsAndClamps()
	{
		var lineMap = TextLineMap.Build("one\ntwo");

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
		var lineMap = TextLineMap.Build("one\ntwo");

		Assert.IsTrue(lineMap.TryGetOffsets(new TextPositionRange(new TextPosition(0, 2), new TextPosition(0, 2)), out TextRange range));
		Assert.AreEqual(new TextRange(2, 0), range);
	}

	[TestMethod]
	public void TryGetOffsets_ReversedRange_Fails()
	{
		var lineMap = TextLineMap.Build("one\ntwo");

		Assert.IsFalse(lineMap.TryGetOffsets(new TextPositionRange(new TextPosition(1, 0), new TextPosition(0, 0)), out TextRange range));
		Assert.AreEqual(default, range);
	}

	[TestMethod]
	public void GetPositionRange_Instance_ConvertsBothEndpointsLikeGetPosition()
	{
		var lineMap = TextLineMap.Build("one\r\ntwo\rthree");

		TextPositionRange range = lineMap.GetPositionRange(TextRange.FromBounds(2, 12));

		Assert.AreEqual(new TextPositionRange(lineMap.GetPosition(2), lineMap.GetPosition(12)), range);
		Assert.AreEqual(new TextPositionRange(new TextPosition(0, 2), new TextPosition(2, 3)), range);
	}

	[TestMethod]
	public void GetPositionRange_Static_MatchesTheInstanceLookupForEveryOffsetPair()
	{
		// LF, CRLF and lone CR terminators, a CR before a CRLF, consecutive terminators, and a final terminator.
		const string text = "a\nbc\r\nd\re\r\r\n\n\rf\r\n";

		var lineMap = TextLineMap.Build(text);

		for (int start = 0; start <= text.Length + 2; start++)
		{
			for (int end = start; end <= text.Length + 2; end++)
			{
				var range = TextRange.FromBounds(start, end);
				Assert.AreEqual(lineMap.GetPositionRange(range), TextLineMap.GetPositionRange(text, range), $"offsets {start} to {end}");
			}
		}
	}

	[TestMethod]
	public void GetPositionRange_Static_OffsetBetweenCrAndLf_IsTheEndOfThePrecedingLine()
	{
		TextPositionRange range = TextLineMap.GetPositionRange("one\r\ntwo", TextRange.FromBounds(4, 5));
		Assert.AreEqual(new TextPositionRange(new TextPosition(0, 3), new TextPosition(1, 0)), range);
	}

	[TestMethod]
	public void GetPositionRange_Static_EmptyRangeAndEmptyText_ReturnOneRepeatedPosition()
	{
		Assert.AreEqual(
			new TextPositionRange(new TextPosition(1, 1), new TextPosition(1, 1)),
			TextLineMap.GetPositionRange("ab\ncd", new TextRange(4, 0))
		);

		Assert.AreEqual(default, TextLineMap.GetPositionRange(string.Empty, new TextRange(0, 0)));
	}

	[TestMethod]
	public void GetPositionRange_Static_EndPastTheText_ClampsToTheEndOfTheText()
	{
		TextPositionRange range = TextLineMap.GetPositionRange("ab\ncd", TextRange.FromBounds(1, 99));
		Assert.AreEqual(new TextPositionRange(new TextPosition(0, 1), new TextPosition(1, 2)), range);
	}

	[TestMethod]
	public void GetPositionRange_Static_AllocatesNothing()
	{
		const string text = "one\r\ntwo\rthree\nfour";
		var range = TextRange.FromBounds(3, 17);

		TextLineMap.GetPositionRange(text, range);

		long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

		for (int i = 0; i < 1000; i++)
			TextLineMap.GetPositionRange(text, range);

		Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
	}
}
