using Nickelony.IDEKit.Core.Media;

namespace Nickelony.IDEKit.TextMate.Tests;

/// <summary>Pins the color spellings of TextMate theme data that <see cref="TextMateColorParser"/> recognizes.</summary>
[TestClass]
public sealed class TextMateColorParserTests
{
	[DataRow("#123456", 0xFF, 0x12, 0x34, 0x56, DisplayName = "SixDigits")]
	[DataRow("#123", 0xFF, 0x11, 0x22, 0x33, DisplayName = "ThreeDigits")]
	[DataRow("#12345678", 0x78, 0x12, 0x34, 0x56, DisplayName = "EightDigitsAlphaLast")]
	[DataRow("#1234", 0x44, 0x11, 0x22, 0x33, DisplayName = "FourDigitsAlphaLast")]
	[DataRow("#abcdef", 0xFF, 0xAB, 0xCD, 0xEF, DisplayName = "LowercaseDigits")]
	[DataRow("#AbCdEf12", 0x12, 0xAB, 0xCD, 0xEF, DisplayName = "MixedCaseDigits")]
	[DataRow("  #FF0000  ", 0xFF, 0xFF, 0x00, 0x00, DisplayName = "SurroundingWhitespace")]
	[DataRow("#000", 0xFF, 0x00, 0x00, 0x00, DisplayName = "Black")]
	[DataRow("#FFFFFFFF", 0xFF, 0xFF, 0xFF, 0xFF, DisplayName = "OpaqueWhite")]
	[TestMethod]
	public void TryParse_RecognizedSpelling_ReturnsTheColor(string value, int alpha, int red, int green, int blue)
	{
		bool parsed = TextMateColorParser.TryParse(value, out ArgbColor color);

		Assert.IsTrue(parsed);
		Assert.AreEqual(new ArgbColor(A: (byte)alpha, R: (byte)red, G: (byte)green, B: (byte)blue), color);
	}

	[DataRow(null, DisplayName = "Null")]
	[DataRow("", DisplayName = "Empty")]
	[DataRow("   ", DisplayName = "Whitespace")]
	[DataRow("#", DisplayName = "HashOnly")]
	[DataRow("#1", DisplayName = "OneDigit")]
	[DataRow("#12", DisplayName = "TwoDigits")]
	[DataRow("#12345", DisplayName = "FiveDigits")]
	[DataRow("#1234567", DisplayName = "SevenDigits")]
	[DataRow("#123456789", DisplayName = "NineDigits")]
	[DataRow("123456", DisplayName = "MissingHash")]
	[DataRow("#GGGGGG", DisplayName = "NonHexSixDigits")]
	[DataRow("#12 456", DisplayName = "InnerWhitespace")]
	[DataRow("red", DisplayName = "ColorName")]
	[DataRow("sc#1,0,0", DisplayName = "ScRgb")]
	[TestMethod]
	public void TryParse_UnrecognizedValue_ReturnsFalse(string? value)
	{
		bool parsed = TextMateColorParser.TryParse(value, out ArgbColor color);

		Assert.IsFalse(parsed);
		Assert.AreEqual(default, color);
	}
}
