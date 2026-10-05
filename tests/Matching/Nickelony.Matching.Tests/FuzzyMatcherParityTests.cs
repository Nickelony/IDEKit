namespace Nickelony.Matching.Tests;

/// <summary>
/// Compares the scores of the matcher with a transcription of the fuzzy scorer of VS Code, so a change to the scoring cannot drift
/// from the reference unnoticed.
/// </summary>
[TestClass]
public sealed class FuzzyMatcherParityTests
{
	[TestMethod]
	[DataRow("uie", "IUiDispatcher")]
	[DataRow("hp", "HTMLParser")]
	[DataRow("hp", "HtmlParser")]
	[DataRow("xhr", "XMLHttpRequest")]
	[DataRow("xhr", "XmlHttpRequest")]
	[DataRow("ioe", "IOException")]
	[DataRow("tlc", "Toggle line comment")]
	[DataRow("fmtdoc", "Format document")]
	[DataRow("doc fmt", "Format document")]
	[DataRow("\"mat doc\"", "Format document")]
	[DataRow("*fmt", "Format document")]
	public void Score_CamelCaseAndPieces_EqualTheReferenceScore(string query, string label)
	{
		int expected = FuzzyScorerTestHelpers
			.ScoreItem(new FuzzyScorerTestHelpers.Item(label, Description: null, Path: null, Index: 0), FuzzyScorerTestHelpers.Prepare(query))
			.Score;

		Assert.AreEqual(expected, new FuzzyMatcher(query).Score(label));
	}
}
