namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class LineTerminatorPlannerTests
{
	private static string Apply(string text, IReadOnlyList<TextIncrementalEdit> edits)
	{
		var builder = new System.Text.StringBuilder(text);

		foreach (TextIncrementalEdit edit in edits.OrderByDescending(edit => edit.Range.Offset))
			builder.Remove(edit.Range.Offset, edit.Range.Length).Insert(edit.Range.Offset, edit.NewText);

		return builder.ToString();
	}

	private static IReadOnlyList<TextIncrementalEdit> Plan(string text, LineTerminatorStyle style) =>
		LineTerminatorPlanner.PlanConversion(new StringTextSnapshot(text), style);

	[TestMethod]
	[DataRow("a\nb\r\nc\rd\n", LineTerminatorStyle.Lf, "a\nb\nc\nd\n")]
	[DataRow("a\nb\r\nc\rd\n", LineTerminatorStyle.CrLf, "a\r\nb\r\nc\r\nd\r\n")]
	[DataRow("a\nb\r\nc\rd\n", LineTerminatorStyle.Cr, "a\rb\rc\rd\r")]
	[DataRow("a\r\nb\r\n", LineTerminatorStyle.Lf, "a\nb\n")]
	[DataRow("\n\n\n", LineTerminatorStyle.CrLf, "\r\n\r\n\r\n")]
	[DataRow("\r\n", LineTerminatorStyle.Cr, "\r")]
	[DataRow("a\n", LineTerminatorStyle.CrLf, "a\r\n")]
	[DataRow("a\nb", LineTerminatorStyle.CrLf, "a\r\nb")]
	public void PlanConversion_AppliedToTheText_GivesEveryTerminatorTheStyle(string text, LineTerminatorStyle style, string expected) =>
		Assert.AreEqual(expected, Apply(text, Plan(text, style)));

	[TestMethod]
	public void PlanConversion_HasOneEditPerTerminatorOfAnotherStyle_AndEachEditNamesOnlyTheTerminator()
	{
		const string text = "a\nbb\r\nccc\rd\r\ne";

		IReadOnlyList<TextIncrementalEdit> edits = Plan(text, LineTerminatorStyle.Lf);

		// The LF at 1 stays; the CRLF at 4, the CR at 9 and the CRLF at 11 are replaced.
		Assert.HasCount(3, edits);
		Assert.AreEqual(new TextRange(4, 2), edits[0].Range);
		Assert.AreEqual(new TextRange(9, 1), edits[1].Range);
		Assert.AreEqual(new TextRange(11, 2), edits[2].Range);
		Assert.IsTrue(edits.All(edit => edit.NewText == "\n"));
	}

	[TestMethod]
	[DataRow("", LineTerminatorStyle.Lf)]
	[DataRow("no terminator", LineTerminatorStyle.CrLf)]
	[DataRow("a\nb\n", LineTerminatorStyle.Lf)]
	[DataRow("a\r\nb\r\n", LineTerminatorStyle.CrLf)]
	[DataRow("a\rb\r", LineTerminatorStyle.Cr)]
	public void PlanConversion_OfTextThatHasTheStyleAlready_IsEmpty(string text, LineTerminatorStyle style) => Assert.IsEmpty(Plan(text, style));

	[TestMethod]
	public void PlanConversion_ACrLfIsOneTerminator_NotACrAndAnLf()
	{
		IReadOnlyList<TextIncrementalEdit> edits = Plan("a\r\nb", LineTerminatorStyle.Cr);

		Assert.HasCount(1, edits);
		Assert.AreEqual(new TextRange(1, 2), edits[0].Range);
	}

	[TestMethod]
	[DataRow(LineTerminatorStyle.Mixed)]
	[DataRow(LineTerminatorStyle.None)]
	[DataRow((LineTerminatorStyle)42)]
	public void PlanConversion_StyleThatNamesNoSingleTerminator_Throws(LineTerminatorStyle style) =>
		Assert.ThrowsExactly<ArgumentException>(() => Plan("a\nb", style));

	[TestMethod]
	public void PlanConversion_NullSnapshot_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => LineTerminatorPlanner.PlanConversion(null!, LineTerminatorStyle.Lf));
}
