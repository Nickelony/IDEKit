using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class ProtocolRangeConversionTests
{
	[TestMethod]
	public void TryGetTextPositionRange_ReturnsFalseWhenRangeContainsNegativeProtocolCoordinate()
	{
		bool resolved = ProtocolRangeConversion.TryGetTextPositionRange(
			new RangePayload(new PositionPayload(Line: -1, Character: 0), new PositionPayload(Line: 0, Character: 1)),
			out TextPositionRange range
		);

		Assert.IsFalse(resolved);
		Assert.AreEqual(default, range);
	}

	[TestMethod]
	public void TryGetTextPositionRange_CopiesProtocolCoordinatesWithoutChangingTheBasis()
	{
		bool resolved = ProtocolRangeConversion.TryGetTextPositionRange(
			new RangePayload(new PositionPayload(Line: 2, Character: 4), new PositionPayload(Line: 2, Character: 10)),
			out TextPositionRange range
		);

		Assert.IsTrue(resolved);
		Assert.AreEqual(new TextPositionRange(new TextPosition(2, 4), new TextPosition(2, 10)), range);
	}

	[TestMethod]
	public void TryGetTextPositionRange_InvertedRange_ReturnsFalse()
	{
		bool resolvedOnSameLine = ProtocolRangeConversion.TryGetTextPositionRange(
			new RangePayload(new PositionPayload(Line: 2, Character: 10), new PositionPayload(Line: 2, Character: 4)),
			out TextPositionRange sameLineRange
		);

		Assert.IsFalse(resolvedOnSameLine);
		Assert.AreEqual(default, sameLineRange);

		bool resolvedAcrossLines = ProtocolRangeConversion.TryGetTextPositionRange(
			new RangePayload(new PositionPayload(Line: 3, Character: 0), new PositionPayload(Line: 1, Character: 9)),
			out TextPositionRange crossLineRange
		);

		Assert.IsFalse(resolvedAcrossLines);
		Assert.AreEqual(default, crossLineRange);
	}

	[TestMethod]
	public void ToProtocolRange_CopiesTheCoordinatesWithoutChangingTheBasis()
	{
		RangePayload range = ProtocolRangeConversion.ToProtocolRange(new TextPositionRange(new TextPosition(2, 4), new TextPosition(3, 10)));
		Assert.AreEqual(new RangePayload(new PositionPayload(Line: 2, Character: 4), new PositionPayload(Line: 3, Character: 10)), range);
	}

	[TestMethod]
	public void ToProtocolRange_ClampsNegativeCoordinatesToZero()
	{
		RangePayload range = ProtocolRangeConversion.ToProtocolRange(new TextPositionRange(new TextPosition(-1, 4), new TextPosition(2, -3)));
		Assert.AreEqual(new RangePayload(new PositionPayload(Line: 0, Character: 4), new PositionPayload(Line: 2, Character: 0)), range);
	}

	private static RangePayload Range(int startLine, int startCharacter, int endLine, int endCharacter) =>
		new(new PositionPayload(startLine, startCharacter), new PositionPayload(endLine, endCharacter));

	[TestMethod]
	[DataRow(0, 6, 0, 7, 6, 1)]
	[DataRow(1, 6, 1, 7, 18, 1)]
	[DataRow(0, 3, 1, 2, 3, 11)]
	public void TryGetOffsetRange_ConvertsARangeInsideTheDocument(
		int startLine,
		int startCharacter,
		int endLine,
		int endCharacter,
		int offset,
		int length
	)
	{
		var lineMap = TextLineMap.Build("local a = 1\nlocal b = 2");

		bool resolved = ProtocolRangeConversion.TryGetOffsetRange(
			Range(startLine, startCharacter, endLine, endCharacter),
			lineMap,
			out TextRange range
		);

		Assert.IsTrue(resolved);
		Assert.AreEqual(new TextRange(offset, length), range);
	}

	[TestMethod]
	public void TryGetOffsetRange_AnEmptyRange_IsAZeroLengthRangeAtItsOffset()
	{
		var lineMap = TextLineMap.Build("local a = 1\nlocal b = 2");

		Assert.IsTrue(
			ProtocolRangeConversion.TryGetOffsetRange(
				Range(startLine: 1, startCharacter: 3, endLine: 1, endCharacter: 3),
				lineMap,
				out TextRange range
			)
		);

		Assert.AreEqual(new TextRange(15, 0), range);
	}

	[TestMethod]
	public void TryGetOffsetRange_TheEndOfALineStaysBeforeItsTerminator()
	{
		var lineMap = TextLineMap.Build("ab\r\ncd");

		Assert.IsTrue(
			ProtocolRangeConversion.TryGetOffsetRange(
				Range(startLine: 0, startCharacter: 2, endLine: 0, endCharacter: 2),
				lineMap,
				out TextRange empty
			)
		);

		Assert.AreEqual(new TextRange(2, 0), empty);

		// A range that ends on the next line covers the whole CRLF terminator.
		Assert.IsTrue(
			ProtocolRangeConversion.TryGetOffsetRange(
				Range(startLine: 0, startCharacter: 2, endLine: 1, endCharacter: 0),
				lineMap,
				out TextRange acrossLines
			)
		);

		Assert.AreEqual(new TextRange(2, 2), acrossLines);
	}

	[TestMethod]
	public void TryGetOffsetRange_ACharacterPastTheEndOfALine_ClampsToTheLineEnd()
	{
		var lineMap = TextLineMap.Build("ab\r\ncd");

		Assert.IsTrue(
			ProtocolRangeConversion.TryGetOffsetRange(
				Range(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 99),
				lineMap,
				out TextRange range
			)
		);

		Assert.AreEqual(new TextRange(0, 2), range);
	}

	[TestMethod]
	public void TryGetOffsetRange_TheEndOfTheDocument_IsAValidOffset()
	{
		var lineMap = TextLineMap.Build("ab\r\ncd");

		Assert.IsTrue(
			ProtocolRangeConversion.TryGetOffsetRange(
				Range(startLine: 1, startCharacter: 0, endLine: 1, endCharacter: 2),
				lineMap,
				out TextRange range
			)
		);

		Assert.AreEqual(new TextRange(4, 2), range);

		Assert.IsTrue(
			ProtocolRangeConversion.TryGetOffsetRange(
				Range(startLine: 1, startCharacter: 2, endLine: 1, endCharacter: 2),
				lineMap,
				out TextRange empty
			)
		);

		Assert.AreEqual(new TextRange(6, 0), empty);
	}

	[TestMethod]
	public void TryGetOffsetRange_ALinePastTheLastLine_ClampsToTheLastLine()
	{
		var lineMap = TextLineMap.Build("ab\r\ncd");

		Assert.IsTrue(
			ProtocolRangeConversion.TryGetOffsetRange(
				Range(startLine: 0, startCharacter: 0, endLine: 9, endCharacter: 99),
				lineMap,
				out TextRange wholeDocument
			)
		);

		Assert.AreEqual(new TextRange(0, 6), wholeDocument);

		Assert.IsTrue(
			ProtocolRangeConversion.TryGetOffsetRange(
				Range(startLine: 9, startCharacter: 0, endLine: 9, endCharacter: 99),
				lineMap,
				out TextRange lastLine
			)
		);

		Assert.AreEqual(new TextRange(4, 2), lastLine);
	}

	[TestMethod]
	public void TryGetOffsetRange_InAnEmptyDocument_MapsEveryRangeToOffsetZero()
	{
		Assert.IsTrue(
			ProtocolRangeConversion.TryGetOffsetRange(
				Range(startLine: 3, startCharacter: 4, endLine: 5, endCharacter: 6),
				TextLineMap.Build(string.Empty),
				out TextRange range
			)
		);

		Assert.AreEqual(new TextRange(0, 0), range);
	}

	[TestMethod]
	public void TryGetOffsetRange_ARangeThatClampsIntoAnInvertedOne_ReturnsFalse()
	{
		var lineMap = TextLineMap.Build("ab\r\ncd");

		// The raw end follows the raw start, but both lines clamp to the last line and the end character then precedes the start character.
		bool resolved = ProtocolRangeConversion.TryGetOffsetRange(
			Range(startLine: 5, startCharacter: 5, endLine: 7, endCharacter: 1),
			lineMap,
			out TextRange range
		);

		Assert.IsFalse(resolved);
		Assert.AreEqual(default, range);
	}

	[TestMethod]
	public void TryGetOffsetRange_ANegativeCoordinate_ReturnsFalse()
	{
		var lineMap = TextLineMap.Build("ab\r\ncd");

		Assert.IsFalse(
			ProtocolRangeConversion.TryGetOffsetRange(
				Range(startLine: -1, startCharacter: 0, endLine: 0, endCharacter: 1),
				lineMap,
				out TextRange negativeLine
			)
		);

		Assert.IsFalse(
			ProtocolRangeConversion.TryGetOffsetRange(
				Range(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: -1),
				lineMap,
				out TextRange negativeCharacter
			)
		);

		Assert.AreEqual(default, negativeLine);
		Assert.AreEqual(default, negativeCharacter);
	}

	[TestMethod]
	public void TryGetOffsetRange_AnInvertedRange_ReturnsFalse()
	{
		var lineMap = TextLineMap.Build("ab\r\ncd");

		Assert.IsFalse(
			ProtocolRangeConversion.TryGetOffsetRange(
				Range(startLine: 0, startCharacter: 2, endLine: 0, endCharacter: 1),
				lineMap,
				out TextRange sameLine
			)
		);

		Assert.IsFalse(
			ProtocolRangeConversion.TryGetOffsetRange(
				Range(startLine: 1, startCharacter: 0, endLine: 0, endCharacter: 2),
				lineMap,
				out TextRange acrossLines
			)
		);

		Assert.AreEqual(default, sameLine);
		Assert.AreEqual(default, acrossLines);
	}

	[TestMethod]
	public void TryGetOffsetRange_WithoutARange_ReturnsFalse()
	{
		Assert.IsFalse(ProtocolRangeConversion.TryGetOffsetRange(null, TextLineMap.Build("ab"), out TextRange range));
		Assert.AreEqual(default, range);
	}

	[TestMethod]
	public void TryGetOffsetRange_CountsACharacterAsOneUtf16CodeUnit()
	{
		// The emoji is a surrogate pair: two code units, so the character after it sits at character 3.
		var lineMap = TextLineMap.Build("a\uD83D\uDE00b");

		Assert.IsTrue(
			ProtocolRangeConversion.TryGetOffsetRange(
				Range(startLine: 0, startCharacter: 1, endLine: 0, endCharacter: 3),
				lineMap,
				out TextRange pair
			)
		);

		Assert.AreEqual(new TextRange(1, 2), pair);

		Assert.IsTrue(
			ProtocolRangeConversion.TryGetOffsetRange(
				Range(startLine: 0, startCharacter: 3, endLine: 0, endCharacter: 4),
				lineMap,
				out TextRange afterPair
			)
		);

		Assert.AreEqual(new TextRange(3, 1), afterPair);

		// A server may address the middle of a pair; the conversion maps it code unit for code unit and never snaps it.
		Assert.IsTrue(
			ProtocolRangeConversion.TryGetOffsetRange(
				Range(startLine: 0, startCharacter: 2, endLine: 0, endCharacter: 3),
				lineMap,
				out TextRange insidePair
			)
		);

		Assert.AreEqual(new TextRange(2, 1), insidePair);
	}
}
