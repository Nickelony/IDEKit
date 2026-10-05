namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class NavigationLocationPlannerTests
{
	private static NavigationLocationPlan Plan(string text, int caretOffset, TextRange selection, int? preferredDocumentLine = null)
	{
		var snapshot = new StringTextSnapshot(text);

		return NavigationLocationPlanner.Plan(
			snapshot,
			snapshot.LineCount,
			new NavigationLocation(DocumentId: "doc", caretOffset, selection, preferredDocumentLine)
		);
	}

	[TestMethod]
	public void Plan_ANullTextSource_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			NavigationLocationPlanner.Plan<StringTextSnapshot>(
				text: null!,
				lineCount: 1,
				new NavigationLocation(DocumentId: null, CaretOffset: 0, Selection: new TextRange(0, 0), PreferredDocumentLine: null)
			)
		);
	}

	[TestMethod]
	public void Plan_LineCountBelowOne_ThrowsArgumentOutOfRangeException()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			NavigationLocationPlanner.Plan(
				new StringTextSnapshot("a"),
				lineCount: 0,
				new NavigationLocation(DocumentId: null, CaretOffset: 0, Selection: new TextRange(0, 0), PreferredDocumentLine: null)
			)
		);
	}

	[TestMethod]
	public void Plan_OffsetsBeyondTheDocument_AreClamped()
	{
		NavigationLocationPlan plan = Plan("abcd", caretOffset: 99, new TextRange(2, 50));

		Assert.AreEqual(new TextRange(2, 2), plan.Selection);
		Assert.AreEqual(4, plan.CaretOffset);
	}

	[TestMethod]
	public void Plan_NegativeCaret_ClampsToTheDocumentStart()
	{
		NavigationLocationPlan plan = Plan("abcd", caretOffset: -5, new TextRange(0, 0));

		Assert.AreEqual(new TextRange(0, 0), plan.Selection);
		Assert.AreEqual(0, plan.CaretOffset);
	}

	[TestMethod]
	public void Plan_CollapsedSelection_UsesTheRecordedCaret()
	{
		NavigationLocationPlan plan = Plan("abcdef", caretOffset: 4, new TextRange(1, 0));

		Assert.AreEqual(new TextRange(4, 0), plan.Selection);
		Assert.AreEqual(4, plan.CaretOffset);
	}

	[TestMethod]
	public void Plan_CaretAtSelectionStartOrEnd_KeepsTheRecordedCaret()
	{
		Assert.AreEqual(1, Plan("abcdef", caretOffset: 1, new TextRange(1, 3)).CaretOffset);
		Assert.AreEqual(4, Plan("abcdef", caretOffset: 4, new TextRange(1, 3)).CaretOffset);
	}

	[TestMethod]
	public void Plan_CaretInsideSelection_KeepsTheRecordedCaret() =>
		Assert.AreEqual(3, Plan("abcdef", caretOffset: 3, new TextRange(1, 4)).CaretOffset);

	[TestMethod]
	public void Plan_CaretOutsideSelection_ClampsToTheNearestSelectionEdge()
	{
		Assert.AreEqual(2, Plan("abcdef", caretOffset: 0, new TextRange(2, 2)).CaretOffset);
		Assert.AreEqual(4, Plan("abcdef", caretOffset: 6, new TextRange(2, 2)).CaretOffset);
	}

	[TestMethod]
	public void Plan_CaretInsideACrLfPair_SnapsToTheStartOfTheTerminator()
	{
		NavigationLocationPlan plan = Plan("ab\r\ncd", caretOffset: 3, new TextRange(3, 0));

		Assert.AreEqual(2, plan.CaretOffset);
		Assert.AreEqual(new TextRange(2, 0), plan.Selection);
	}

	[TestMethod]
	public void Plan_LoneLineFeedAndLoneCarriageReturn_AreNotSnapped()
	{
		Assert.AreEqual(3, Plan("ab\ncd", caretOffset: 3, new TextRange(3, 0)).CaretOffset);
		Assert.AreEqual(3, Plan("ab\rcd", caretOffset: 3, new TextRange(3, 0)).CaretOffset);
	}

	[TestMethod]
	public void Plan_SelectionBoundariesInsideACrLfPair_SnapOutOfThePair()
	{
		NavigationLocationPlan plan = Plan("ab\r\ncd\r\nef", caretOffset: 4, new TextRange(3, 5));

		// The start snaps from 3 to 2, so the end is 2 + 5 = 7, inside the second pair, and snaps to 6.
		Assert.AreEqual(new TextRange(2, 4), plan.Selection);
		Assert.AreEqual(4, plan.CaretOffset);
	}

	[TestMethod]
	public void Plan_SnappedEndBeforeTheStart_CollapsesTheSelection()
	{
		// The start snaps from 3 to 2, and the end 2 + 1 = 3 is inside the pair and snaps back to 2, so nothing is left selected.
		NavigationLocationPlan plan = Plan("ab\r\ncd", caretOffset: 3, new TextRange(3, 1));
		Assert.AreEqual(0, plan.Selection.Length);
	}

	[TestMethod]
	public void Plan_WithoutPreferredLine_ScrollsToTheLineOfTheSelectionStart() =>
		Assert.IsNull(Plan("a\nb\nc", caretOffset: 2, new TextRange(2, 0)).ScrollLine);

	[TestMethod]
	public void Plan_PreferredLineBeyondTheDocument_ClampsToTheLastLine() =>
		Assert.AreEqual(3, Plan(text: "a\nb\nc", caretOffset: 0, selection: new TextRange(0, 0), preferredDocumentLine: 99).ScrollLine);

	[TestMethod]
	public void Plan_PreferredLineBelowOne_ClampsToTheFirstLine() =>
		Assert.AreEqual(1, Plan(text: "a\nb\nc", caretOffset: 0, selection: new TextRange(0, 0), preferredDocumentLine: -4).ScrollLine);
}
