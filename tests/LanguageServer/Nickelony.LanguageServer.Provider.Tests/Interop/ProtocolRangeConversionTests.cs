namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class ProtocolRangeConversionTests
{
	[TestMethod]
	public void TryGetTextPosition_ReturnsFalseForNegativeProtocolCoordinates()
	{
		bool resolved = ProtocolRangeConversion.TryGetTextPosition(
			new ProtocolPosition(Line: -1, Character: 0),
			out TextPosition position);

		Assert.IsFalse(resolved);
		Assert.AreEqual(default, position);

		resolved = ProtocolRangeConversion.TryGetTextPosition(
			new ProtocolPosition(Line: 0, Character: -1),
			out position);

		Assert.IsFalse(resolved);
		Assert.AreEqual(default, position);
	}

	[TestMethod]
	public void TryGetTextPositionRange_ReturnsFalseWhenRangeContainsNegativeProtocolCoordinate()
	{
		bool resolved = ProtocolRangeConversion.TryGetTextPositionRange(
			new ProtocolRangePayload(
				new ProtocolPosition(Line: -1, Character: 0),
				new ProtocolPosition(Line: 0, Character: 1)),
			out TextPositionRange range);

		Assert.IsFalse(resolved);
		Assert.AreEqual(default, range);
	}

	[TestMethod]
	public void TryGetTextPositionRange_CopiesProtocolCoordinatesWithoutChangingTheBasis()
	{
		bool resolved = ProtocolRangeConversion.TryGetTextPositionRange(
			new ProtocolRangePayload(
				new ProtocolPosition(Line: 2, Character: 4),
				new ProtocolPosition(Line: 2, Character: 10)),
			out TextPositionRange range);

		Assert.IsTrue(resolved);
		Assert.AreEqual(new TextPositionRange(new TextPosition(2, 4), new TextPosition(2, 10)), range);
	}

	[TestMethod]
	public void TryGetTextPositionRange_InvertedRange_ReturnsFalse()
	{
		bool resolvedOnSameLine = ProtocolRangeConversion.TryGetTextPositionRange(
			new ProtocolRangePayload(
				new ProtocolPosition(Line: 2, Character: 10),
				new ProtocolPosition(Line: 2, Character: 4)),
			out TextPositionRange sameLineRange);

		Assert.IsFalse(resolvedOnSameLine);
		Assert.AreEqual(default, sameLineRange);

		bool resolvedAcrossLines = ProtocolRangeConversion.TryGetTextPositionRange(
			new ProtocolRangePayload(
				new ProtocolPosition(Line: 3, Character: 0),
				new ProtocolPosition(Line: 1, Character: 9)),
			out TextPositionRange crossLineRange);

		Assert.IsFalse(resolvedAcrossLines);
		Assert.AreEqual(default, crossLineRange);
	}
}
