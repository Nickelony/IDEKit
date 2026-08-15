namespace Nickelony.LanguageServer.Abstractions.Tests;

[TestClass]
public sealed class TextFormattingOptionsTests
{
	[TestMethod]
	[DataRow(1, DisplayName = "Smallest positive width is preserved")]
	[DataRow(8, DisplayName = "Positive width is preserved")]
	public void Constructor_PositiveTabSize_IsPreserved(int tabSize)
	{
		var options = new TextFormattingOptions(tabSize, insertSpaces: true);

		Assert.AreEqual(tabSize, options.TabSize);
		Assert.IsTrue(options.InsertSpaces);
	}

	[TestMethod]
	[DataRow(0, DisplayName = "Zero width uses DefaultTabSize")]
	[DataRow(-6, DisplayName = "Negative width uses DefaultTabSize")]
	[DataRow(int.MinValue, DisplayName = "Int32.MinValue uses DefaultTabSize")]
	public void Constructor_NonPositiveTabSize_UsesDefaultTabSize(int tabSize)
	{
		var options = new TextFormattingOptions(tabSize, insertSpaces: false);

		Assert.AreEqual(TextFormattingOptions.DefaultTabSize, options.TabSize);
		Assert.IsFalse(options.InsertSpaces);
	}
}
