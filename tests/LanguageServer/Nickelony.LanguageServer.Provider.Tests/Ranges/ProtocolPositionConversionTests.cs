using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class ProtocolPositionConversionTests
{
	[TestMethod]
	public void ToProtocolPosition_KeepsNonNegativeCoordinates() =>
		Assert.AreEqual(new PositionPayload(3, 7), ProtocolPositionConversion.ToProtocolPosition(new TextPosition(3, 7)));

	[TestMethod]
	[DataRow(-1, 3, 0, 3)]
	[DataRow(2, -5, 2, 0)]
	[DataRow(-4, -4, 0, 0)]
	public void ToProtocolPosition_ClampsNegativeCoordinatesToZero(int line, int character, int expectedLine, int expectedCharacter)
	{
		Assert.AreEqual(
			new PositionPayload(expectedLine, expectedCharacter),
			ProtocolPositionConversion.ToProtocolPosition(new TextPosition(line, character))
		);
	}

	[TestMethod]
	public void TryGetTextPosition_CopiesNonNegativeCoordinatesWithoutChangingTheBasis()
	{
		bool resolved = ProtocolPositionConversion.TryGetTextPosition(new PositionPayload(Line: 2, Character: 0), out TextPosition position);

		Assert.IsTrue(resolved);
		Assert.AreEqual(new TextPosition(2, 0), position);
	}

	[TestMethod]
	[DataRow(-1, 0)]
	[DataRow(0, -1)]
	public void TryGetTextPosition_ReturnsFalseForNegativeProtocolCoordinates(int line, int character)
	{
		bool resolved = ProtocolPositionConversion.TryGetTextPosition(new PositionPayload(line, character), out TextPosition position);

		Assert.IsFalse(resolved);
		Assert.AreEqual(default, position);
	}
}
