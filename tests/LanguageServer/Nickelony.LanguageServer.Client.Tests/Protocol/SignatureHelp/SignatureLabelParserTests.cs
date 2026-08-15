using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class SignatureLabelParserTests
{
	private static JsonElement ParseElement(string json)
		=> JsonDocument.Parse(json).RootElement;

	[TestMethod]
	public void TryExtractParameterLabel_OffsetPair_ReturnsTheLabelSubstring()
	{
		bool succeeded = SignatureLabelParser.TryExtractParameterLabel("fn(first, second)", ParseElement("[3, 8]"), out string? parameterLabel);

		Assert.IsTrue(succeeded);
		Assert.AreEqual("first", parameterLabel);
	}

	[TestMethod]
	public void TryExtractParameterLabel_OffsetsBeyondTheLabel_ClampsToTheLabelBounds()
	{
		bool succeeded = SignatureLabelParser.TryExtractParameterLabel("abc", ParseElement("[1, 99]"), out string? parameterLabel);

		Assert.IsTrue(succeeded);
		Assert.AreEqual("bc", parameterLabel);
	}

	[TestMethod]
	public void TryExtractParameterLabel_NegativeStart_ClampsToZero()
	{
		bool succeeded = SignatureLabelParser.TryExtractParameterLabel("abc", ParseElement("[-5, 2]"), out string? parameterLabel);

		Assert.IsTrue(succeeded);
		Assert.AreEqual("ab", parameterLabel);
	}

	[TestMethod]
	public void TryExtractParameterLabel_InvertedOrEmptyRange_ReturnsFalse()
	{
		Assert.IsFalse(SignatureLabelParser.TryExtractParameterLabel("fn(abc)", ParseElement("[5, 2]"), out _));
		Assert.IsFalse(SignatureLabelParser.TryExtractParameterLabel("fn(abc)", ParseElement("[2, 2]"), out _));
	}

	[TestMethod]
	public void TryExtractParameterLabel_NonArrayParameterLabel_ReturnsFalse()
	{
		Assert.IsFalse(SignatureLabelParser.TryExtractParameterLabel("fn(abc)", ParseElement("\"abc\""), out _));
		Assert.IsFalse(SignatureLabelParser.TryExtractParameterLabel("fn(abc)", ParseElement("42"), out _));
	}

	[TestMethod]
	public void TryExtractParameterLabel_MalformedOffsetArray_ReturnsFalse()
	{
		Assert.IsFalse(SignatureLabelParser.TryExtractParameterLabel("fn(abc)", ParseElement("[2]"), out _));
		Assert.IsFalse(SignatureLabelParser.TryExtractParameterLabel("fn(abc)", ParseElement("[]"), out _));
		Assert.IsFalse(SignatureLabelParser.TryExtractParameterLabel("fn(abc)", ParseElement("[\"a\", \"b\"]"), out _));
		Assert.IsFalse(SignatureLabelParser.TryExtractParameterLabel("fn(abc)", ParseElement("[0, 1, 2]"), out _));
	}

	[TestMethod]
	public void TryExtractParameterLabel_EmptySignatureLabel_ReturnsFalse()
	{
		Assert.IsFalse(SignatureLabelParser.TryExtractParameterLabel(string.Empty, ParseElement("[0, 1]"), out _));
		Assert.IsFalse(SignatureLabelParser.TryExtractParameterLabel("abc", default, out _));
	}
}
