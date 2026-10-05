using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

/// <summary>Pins the LSP numeric contract of the insert-text format vocabulary.</summary>
[TestClass]
public sealed class CompletionInsertTextFormatTests
{
	[TestMethod]
	[DataRow(CompletionInsertTextFormat.PlainText, 1)]
	[DataRow(CompletionInsertTextFormat.Snippet, 2)]
	public void InsertTextFormat_MatchesLspNumericContract(CompletionInsertTextFormat format, int expectedValue) =>
		Assert.AreEqual(expectedValue, (int)format);
}
