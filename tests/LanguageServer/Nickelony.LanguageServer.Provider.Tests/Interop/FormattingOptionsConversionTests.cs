namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers <see cref="FormattingOptionsConversion"/> directly, including the defaulting rule that
/// <see cref="TextFormattingOptions"/> owns.
/// </summary>
[TestClass]
public sealed class FormattingOptionsConversionTests
{
	[TestMethod]
	[DataRow(2, true)]
	[DataRow(8, false)]
	public void ToPayload_CopiesTabSizeAndInsertSpaces(int tabSize, bool insertSpaces)
	{
		FormattingOptionsPayload payload = FormattingOptionsConversion.ToPayload(
			new TextFormattingOptions(tabSize, insertSpaces));

		Assert.AreEqual(tabSize, payload.TabSize);
		Assert.AreEqual(insertSpaces, payload.InsertSpaces);
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(-1)]
	public void ToPayload_NonPositiveTabSize_UsesTheDefaultTabSize(int tabSize)
	{
		FormattingOptionsPayload payload = FormattingOptionsConversion.ToPayload(
			new TextFormattingOptions(tabSize, insertSpaces: true));

		Assert.AreEqual(TextFormattingOptions.DefaultTabSize, payload.TabSize);
		Assert.IsTrue(payload.InsertSpaces);
	}
}
