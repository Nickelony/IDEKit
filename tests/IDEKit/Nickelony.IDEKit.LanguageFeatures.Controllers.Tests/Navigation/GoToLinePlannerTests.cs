using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation;
using Nickelony.QuickPick;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Covers how the go to line mode reads its text and clamps it to a document.</summary>
[TestClass]
public sealed class GoToLinePlannerTests
{
	private static GoToLinePlan? Plan(string query, int lineCount = 50, int lineLength = 20) =>
		GoToLinePlanner.Plan(query, lineCount, _ => lineLength);

	[TestMethod]
	public void Plan_ALine_PutsTheCaretAtTheStartOfTheLine() => Assert.AreEqual(new GoToLinePlan(12, Column: 1, HasColumn: false), Plan("12"));

	[TestMethod]
	public void Plan_ALineAndAColumn_NamesBoth() => Assert.AreEqual(new GoToLinePlan(12, Column: 5, HasColumn: true), Plan("12:5"));

	[TestMethod]
	public void Plan_WhitespaceAroundTheNumbers_IsIgnored() => Assert.AreEqual(new GoToLinePlan(12, Column: 5, HasColumn: true), Plan("  12 : 5  "));

	[TestMethod]
	public void Plan_AnEmptyColumn_IsNoColumn()
	{
		Assert.AreEqual(new GoToLinePlan(12, Column: 1, HasColumn: false), Plan("12:"));
		Assert.AreEqual(new GoToLinePlan(12, Column: 1, HasColumn: false), Plan("12: "));
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("   ")]
	[DataRow("abc")]
	[DataRow("12x")]
	[DataRow("1:2:3")]
	[DataRow(":5")]
	[DataRow("12:x")]
	[DataRow("-3")]
	[DataRow("+3")]
	[DataRow("1.5")]
	[DataRow("1 2")]
	public void Plan_TextThatIsNoPosition_ReturnsNull(string query) => Assert.IsNull(Plan(query));

	[TestMethod]
	public void Plan_ALineBeyondTheDocument_ClampsToTheLastLine()
	{
		Assert.AreEqual(50, Plan("999")!.Value.LineNumber);
		Assert.AreEqual(50, Plan("51")!.Value.LineNumber);
		Assert.AreEqual(50, Plan("50")!.Value.LineNumber);
	}

	[TestMethod]
	public void Plan_LineZero_ClampsToTheFirstLine() => Assert.AreEqual(1, Plan("0")!.Value.LineNumber);

	[TestMethod]
	public void Plan_AColumnBeyondTheLine_ClampsToTheColumnAfterTheLastCharacter()
	{
		GoToLinePlan plan = Plan("3:999", lineLength: 7)!.Value;

		Assert.AreEqual(3, plan.LineNumber);
		Assert.AreEqual(8, plan.Column);
		Assert.IsTrue(plan.HasColumn);
	}

	[TestMethod]
	public void Plan_ColumnZero_ClampsToTheFirstColumn() => Assert.AreEqual(1, Plan("3:0")!.Value.Column);

	[TestMethod]
	public void Plan_AColumnOnAnEmptyLine_ClampsToTheOnlyColumn() => Assert.AreEqual(1, Plan("3:9", lineLength: 0)!.Value.Column);

	[TestMethod]
	public void Plan_NumbersTooLargeForAnInteger_ClampToTheEnd()
	{
		GoToLinePlan plan = Plan("99999999999999999999:99999999999999999999", lineLength: 4)!.Value;

		Assert.AreEqual(50, plan.LineNumber);
		Assert.AreEqual(5, plan.Column);
	}

	[TestMethod]
	public void Plan_AColumn_AsksForTheLengthOfTheClampedLineOnly()
	{
		List<int> asked = [];

		GoToLinePlanner.Plan(
			"999:4",
			lineCount: 50,

			getLineLength: line =>
			{
				asked.Add(line);
				return 10;
			}
		);

		GoToLinePlanner.Plan(
			"7",
			lineCount: 50,

			getLineLength: line =>
			{
				asked.Add(line);
				return 10;
			}
		);

		Assert.AreSequenceEqual([50], asked);
	}
}
