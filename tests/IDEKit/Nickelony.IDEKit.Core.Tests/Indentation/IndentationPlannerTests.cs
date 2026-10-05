namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class IndentationPlannerTests
{
	private static string Apply(string text, IReadOnlyList<TextIncrementalEdit> edits)
	{
		var builder = new System.Text.StringBuilder(text);

		foreach (TextIncrementalEdit edit in edits.OrderByDescending(edit => edit.Range.Offset))
			builder.Remove(edit.Range.Offset, edit.Range.Length).Insert(edit.Range.Offset, edit.NewText);

		return builder.ToString();
	}

	private const string Mixed = "function f()\n    if x then\n\t\tprint(\"a\tb\")\n  \t  end\n\n    \n\t\nend\n   x\n";

	[TestMethod]
	[DataRow("\n")]
	[DataRow("\r\n")]
	[DataRow("\r")]
	public void PlanToSpaces_AppliedToTheText_GivesWhatConvertIndentationToSpacesGives(string terminator)
	{
		string text = Mixed.Replace("\n", terminator, StringComparison.Ordinal);

		IReadOnlyList<TextIncrementalEdit> edits = IndentationPlanner.PlanToSpaces(new StringTextSnapshot(text), 4);

		Assert.AreEqual(WhitespaceOperations.ConvertIndentationToSpaces(text, 4), Apply(text, edits));
	}

	[TestMethod]
	[DataRow("\n")]
	[DataRow("\r\n")]
	[DataRow("\r")]
	public void PlanToTabs_AppliedToTheText_GivesWhatConvertIndentationToTabsGives(string terminator)
	{
		string text = Mixed.Replace("\n", terminator, StringComparison.Ordinal);

		IReadOnlyList<TextIncrementalEdit> edits = IndentationPlanner.PlanToTabs(new StringTextSnapshot(text), 4);

		Assert.AreEqual(WhitespaceOperations.ConvertIndentationToTabs(text, 4), Apply(text, edits));
	}

	[TestMethod]
	[DataRow("\n")]
	[DataRow("\r\n")]
	[DataRow("\r")]
	public void PlanResize_AppliedToTheText_GivesWhatResizeIndentationGives(string terminator)
	{
		string text = Mixed.Replace("\n", terminator, StringComparison.Ordinal);

		IReadOnlyList<TextIncrementalEdit> edits = IndentationPlanner.PlanResize(new StringTextSnapshot(text), 2, 3);

		Assert.AreEqual(WhitespaceOperations.ResizeIndentation(text, 2, 3), Apply(text, edits));
	}

	[TestMethod]
	public void Plans_EveryEdit_ReplacesOnlyTheLeadingSpacesAndTabsOfOneLine()
	{
		var snapshot = new StringTextSnapshot(Mixed);

		foreach (
			IReadOnlyList<TextIncrementalEdit> edits in new[]
			{
				IndentationPlanner.PlanToSpaces(snapshot, 4),
				IndentationPlanner.PlanToTabs(snapshot, 4),
				IndentationPlanner.PlanResize(snapshot, 2, 3),
			}
		)
		{
			foreach (TextIncrementalEdit edit in edits)
			{
				ITextLine line = snapshot.GetLineByOffset(edit.Range.Offset);

				Assert.AreEqual(line.Offset, edit.Range.Offset);
				Assert.IsLessThanOrEqualTo(line.EndOffset, edit.Range.EndOffset);
				Assert.IsTrue(snapshot.GetText(edit.Range.Offset, edit.Range.Length).All(character => character is ' ' or '\t'));
				Assert.IsTrue(edit.NewText.All(character => character is ' ' or '\t'));
				Assert.AreNotEqual(snapshot.GetText(edit.Range.Offset, edit.Range.Length), edit.NewText);
			}
		}
	}

	[TestMethod]
	public void Plans_AreInLineOrderAndDisjoint()
	{
		IReadOnlyList<TextIncrementalEdit> edits = IndentationPlanner.PlanToSpaces(new StringTextSnapshot(Mixed), 4);

		Assert.IsNotEmpty(edits);

		for (int index = 1; index < edits.Count; index++)
			Assert.IsLessThanOrEqualTo(edits[index].Range.Offset, edits[index - 1].Range.EndOffset);
	}

	[TestMethod]
	public void Plans_OfTextThatNeedsNoChange_AreEmpty()
	{
		var snapshot = new StringTextSnapshot("a\n  b\n\tc\n");

		Assert.IsEmpty(IndentationPlanner.PlanToSpaces(new StringTextSnapshot("a\n  b\n"), 4));
		Assert.IsEmpty(IndentationPlanner.PlanToTabs(new StringTextSnapshot("a\n\tb\n  c\n"), 4));
		Assert.IsEmpty(IndentationPlanner.PlanResize(snapshot, 2, 2));
		Assert.IsEmpty(IndentationPlanner.PlanToSpaces(new StringTextSnapshot(string.Empty), 4));
	}

	[TestMethod]
	public void PlanToSpaces_WhitespaceOnlyLine_IsPlanned()
	{
		Assert.AreEqual(
			new TextIncrementalEdit(new TextRange(2, 1), "    "),
			IndentationPlanner.PlanToSpaces(new StringTextSnapshot("a\n\t\nb"), 4).Single()
		);
	}

	[TestMethod]
	public void Plans_LeaveATabAfterTheFirstCharacter()
	{
		var snapshot = new StringTextSnapshot("\tprint(\"a\tb\")\t-- note");

		IReadOnlyList<TextIncrementalEdit> edits = IndentationPlanner.PlanToSpaces(snapshot, 4);

		Assert.AreEqual(new TextIncrementalEdit(new TextRange(0, 1), "    "), edits.Single());
	}

	[TestMethod]
	public void Plans_RejectBadArguments()
	{
		var snapshot = new StringTextSnapshot("a");

		Assert.ThrowsExactly<ArgumentNullException>(() => IndentationPlanner.PlanToSpaces(null!, 4));
		Assert.ThrowsExactly<ArgumentNullException>(() => IndentationPlanner.PlanToTabs(null!, 4));
		Assert.ThrowsExactly<ArgumentNullException>(() => IndentationPlanner.PlanResize(null!, 2, 4));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => IndentationPlanner.PlanToSpaces(snapshot, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => IndentationPlanner.PlanToTabs(snapshot, 1001));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => IndentationPlanner.PlanResize(snapshot, 0, 4));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => IndentationPlanner.PlanResize(snapshot, 2, 1001));
	}
}
