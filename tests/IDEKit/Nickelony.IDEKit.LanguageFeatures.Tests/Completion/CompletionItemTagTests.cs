using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

/// <summary>Pins the LSP numeric contract of the completion tag vocabulary.</summary>
[TestClass]
public sealed class CompletionItemTagTests
{
	[TestMethod]
	[DataRow(CompletionItemTag.Deprecated, 1)]
	public void Tag_MatchesLspNumericContract(CompletionItemTag tag, int expectedValue) => Assert.AreEqual(expectedValue, (int)tag);
}
