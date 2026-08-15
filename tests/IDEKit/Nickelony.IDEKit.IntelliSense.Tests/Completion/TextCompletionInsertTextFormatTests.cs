using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

/// <summary>
/// Pins the LSP numeric contract of the insert-text format vocabulary.
/// </summary>
[TestClass]
public sealed class TextCompletionInsertTextFormatTests
{
	[TestMethod]
	[DataRow(TextCompletionInsertTextFormat.PlainText, 1)]
	[DataRow(TextCompletionInsertTextFormat.Snippet, 2)]
	public void InsertTextFormat_MatchesLspNumericContract(TextCompletionInsertTextFormat format, int expectedValue)
		=> Assert.AreEqual(expectedValue, (int)format);
}
