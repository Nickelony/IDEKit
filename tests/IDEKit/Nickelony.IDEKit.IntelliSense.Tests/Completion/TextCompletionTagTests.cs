using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

/// <summary>
/// Pins the LSP numeric contract of the completion tag vocabulary.
/// </summary>
[TestClass]
public sealed class TextCompletionTagTests
{
	[TestMethod]
	[DataRow(TextCompletionTag.Deprecated, 1)]
	public void Tag_MatchesLspNumericContract(TextCompletionTag tag, int expectedValue)
		=> Assert.AreEqual(expectedValue, (int)tag);
}
