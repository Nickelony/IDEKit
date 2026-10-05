using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Verifies the commit range of an item that was answered before the typed word grew.</summary>
public sealed partial class CompletionCommitPlannerTests
{
	[TestMethod]
	public void Plan_ItemRangeEndsBeforeTheCaret_ReplacesThroughTheCaret()
	{
		const string text = "string.re";

		// The item answered "string." with an empty range at the member access, and "re" was typed after it.
		var edit = new CompletionTextEdit(new TextRange(7, 0));

		CompletionCommitPlan plan = Plan(
			text,
			completionSegment: new TextRange(7, 2),
			insertText: "rep",
			primaryEdit: edit,
			isOverstrikeMode: false,
			caretOffset: 9
		);

		Assert.AreEqual(new TextRange(7, 2), plan.Operations[0].Range);
		Assert.AreEqual("string.rep", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_ItemRangeEndsBeforeTheCaretInOverstrikeMode_ReplacesThroughTheCaret()
	{
		const string text = "string.re";

		var edit = new CompletionTextEdit(new TextRange(7, 0), replaceRange: new TextRange(7, 1));

		CompletionCommitPlan plan = Plan(
			text,
			completionSegment: new TextRange(7, 2),
			insertText: "rep",
			primaryEdit: edit,
			isOverstrikeMode: true,
			caretOffset: 9
		);

		Assert.AreEqual(new TextRange(7, 2), plan.Operations[0].Range);
		Assert.AreEqual("string.rep", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_CaretBeforeTheSegmentEnd_ExtendsOnlyToTheCaret()
	{
		const string text = "string.rep";

		var edit = new CompletionTextEdit(new TextRange(7, 0));

		// The word runs on past the caret, as a whole-word segment does, and the text after the caret stays.
		CompletionCommitPlan plan = Plan(
			text,
			completionSegment: new TextRange(7, 3),
			insertText: "rev",
			primaryEdit: edit,
			isOverstrikeMode: false,
			caretOffset: 9
		);

		Assert.AreEqual(new TextRange(7, 2), plan.Operations[0].Range);
		Assert.AreEqual("string.revp", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_WholeWordSegmentWithAnInsertReplaceEditInInsertMode_KeepsTheInsertRange()
	{
		const string text = "foobar";

		// The caret is after "fo", and the segment spans the whole word "foobar".
		var edit = new CompletionTextEdit(new TextRange(0, 2), replaceRange: new TextRange(0, 6));

		CompletionCommitPlan plan = Plan(
			text,
			completionSegment: new TextRange(0, 6),
			insertText: "foo",
			primaryEdit: edit,
			isOverstrikeMode: false,
			caretOffset: 2
		);

		Assert.AreEqual(new TextRange(0, 2), plan.Operations[0].Range);
		Assert.AreEqual("fooobar", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_WholeWordSegmentWithAnInsertReplaceEditInReplaceMode_KeepsTheReplaceRange()
	{
		const string text = "foobar";

		var edit = new CompletionTextEdit(new TextRange(0, 2), replaceRange: new TextRange(0, 6));

		CompletionCommitPlan plan = Plan(
			text,
			completionSegment: new TextRange(0, 6),
			insertText: "foo",
			primaryEdit: edit,
			isOverstrikeMode: true,
			caretOffset: 2
		);

		Assert.AreEqual(new TextRange(0, 6), plan.Operations[0].Range);
		Assert.AreEqual("foo", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_ItemRangeReachesTheCaret_IsUsedAsIs()
	{
		const string text = "string.re";

		var edit = new CompletionTextEdit(new TextRange(7, 2));

		CompletionCommitPlan plan = Plan(
			text,
			completionSegment: new TextRange(7, 2),
			insertText: "rep",
			primaryEdit: edit,
			isOverstrikeMode: false,
			caretOffset: 9
		);

		Assert.AreEqual(new TextRange(7, 2), plan.Operations[0].Range);
	}

	[TestMethod]
	public void Plan_ItemRangePassesTheCaret_IsUsedAsIs()
	{
		const string text = "string.replace";

		// The replace range covers the rest of the word after the caret, which the segment does not reach.
		var edit = new CompletionTextEdit(new TextRange(7, 0), replaceRange: new TextRange(7, 7));

		CompletionCommitPlan plan = Plan(
			text,
			completionSegment: new TextRange(7, 2),
			insertText: "rep",
			primaryEdit: edit,
			isOverstrikeMode: true,
			caretOffset: 9
		);

		Assert.AreEqual(new TextRange(7, 7), plan.Operations[0].Range);
		Assert.AreEqual("string.rep", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_WithoutACaretOffset_UsesTheItemRangeAsIs()
	{
		const string text = "string.re";

		var edit = new CompletionTextEdit(new TextRange(7, 0));

		CompletionCommitPlan plan = Plan(text, completionSegment: new TextRange(7, 2), insertText: "rep", primaryEdit: edit, isOverstrikeMode: false);

		Assert.AreEqual(new TextRange(7, 0), plan.Operations[0].Range);
	}

	[TestMethod]
	public void Plan_CompletionSegmentStartsAfterTheItemRange_IsNotUsedToExtendIt()
	{
		const string text = "ab cd";

		var edit = new CompletionTextEdit(new TextRange(0, 1));

		CompletionCommitPlan plan = Plan(
			text,
			completionSegment: new TextRange(3, 2),
			insertText: "x",
			primaryEdit: edit,
			isOverstrikeMode: false,
			caretOffset: 5
		);

		Assert.AreEqual(new TextRange(0, 1), plan.Operations[0].Range);
		Assert.AreEqual("xb cd", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_CaretBeyondTheSnapshot_IsNotUsedToExtendTheItemRange()
	{
		const string text = "string.";

		var edit = new CompletionTextEdit(new TextRange(7, 0));

		CompletionCommitPlan plan = Plan(
			text,
			completionSegment: new TextRange(7, 5),
			insertText: "rep",
			primaryEdit: edit,
			isOverstrikeMode: false,
			caretOffset: 12
		);

		Assert.AreEqual(new TextRange(7, 0), plan.Operations[0].Range);
	}

	[TestMethod]
	public void ResolvePrimaryRange_ItemRangeEndsBeforeTheCaret_ReturnsTheRangeThroughTheCaret()
	{
		var request = new CompletionCommitPlanRequest(
			Snapshot: new StringTextSnapshot("string.re"),
			CompletionSegment: new TextRange(7, 2),
			InsertText: "rep",
			CaretOffsetInInsertText: null,
			AdditionalTextEdits: [],
			PrimaryEdit: new CompletionTextEdit(new TextRange(7, 0)),
			IsOverstrikeMode: false,
			CaretOffset: 9
		);

		Assert.AreEqual(new TextRange(7, 2), CompletionCommitPlanner.ResolvePrimaryRange(request));
	}
}
