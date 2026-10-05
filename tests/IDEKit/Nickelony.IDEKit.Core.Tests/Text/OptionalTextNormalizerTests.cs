namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class OptionalTextNormalizerTests
{
	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow(" ")]
	[DataRow("\t")]
	[DataRow("\r\n")]
	[DataRow(" \t\r\n ")]
	[DataRow(" ")]
	public void Normalize_BlankText_ReturnsNull(string? text) => Assert.IsNull(OptionalTextNormalizer.Normalize(text));

	[TestMethod]
	[DataRow("text", "text")]
	[DataRow("  text", "text")]
	[DataRow("text  ", "text")]
	[DataRow("\t text \r\n", "text")]
	[DataRow("two words", "two words")]
	[DataRow("  inner  spaces  ", "inner  spaces")]
	public void Normalize_Text_ReturnsTheTrimmedValue(string text, string expected) =>
		Assert.AreEqual(expected, OptionalTextNormalizer.Normalize(text));
}
