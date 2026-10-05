using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Verifies the completion commit plan without an editor: which range the primary insertion replaces, which additional edits survive,
/// and where the caret lands. The tests apply the plan to a string, so a wrong order or shift shows up as wrong text, not only as wrong numbers.
/// </summary>
[TestClass]
public sealed partial class CompletionCommitPlannerTests
{
	[TestMethod]
	public void Plan_WithoutAdditionalEdits_ReplacesTheCompletionSegment()
	{
		CompletionCommitPlan plan = Plan("pr(", new TextRange(0, 2), "print");

		Assert.HasCount(1, plan.Operations);
		Assert.AreEqual(new TextRange(0, 2), plan.Operations[0].Range);
		Assert.AreEqual("print", plan.Operations[0].NewText);
		Assert.AreEqual(0, plan.Operations[0].EditIndex);
		Assert.IsNull(plan.CaretOffset);
		Assert.AreEqual("print(", Apply("pr(", plan));
	}

	[TestMethod]
	public void Plan_WithoutAdditionalEdits_KeepsAPrimaryThatChangesNothing()
	{
		CompletionCommitPlan plan = Plan("ab", new TextRange(2, 0), "");

		// The commit replaces the segment unconditionally, so the plan keeps the no-op replacement.
		Assert.HasCount(1, plan.Operations);
		Assert.IsTrue(plan.Operations[0].IsNoOp);
	}

	[TestMethod]
	public void Plan_PrimaryEditInInsertMode_ReplacesTheInsertRange()
	{
		const string text = "abcdef";

		var edit = new CompletionTextEdit(new TextRange(1, 1), replaceRange: new TextRange(1, 3));

		CompletionCommitPlan plan = Plan(text, completionSegment: new TextRange(0, 0), insertText: "X", primaryEdit: edit, isOverstrikeMode: false);

		Assert.AreEqual(new TextRange(1, 1), plan.Operations[0].Range);
		Assert.AreEqual("aXcdef", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_PrimaryEditInOverstrikeMode_ReplacesTheReplacementRange()
	{
		const string text = "abcdef";

		var edit = new CompletionTextEdit(new TextRange(1, 1), replaceRange: new TextRange(1, 3));

		CompletionCommitPlan plan = Plan(text, completionSegment: new TextRange(0, 0), insertText: "X", primaryEdit: edit, isOverstrikeMode: true);

		Assert.AreEqual(new TextRange(1, 3), plan.Operations[0].Range);
		Assert.AreEqual("aXef", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_PrimaryEditWithoutReplaceRangeInOverstrikeMode_ReplacesTheInsertRange()
	{
		const string text = "abcdef";

		var edit = new CompletionTextEdit(new TextRange(1, 1));

		CompletionCommitPlan plan = Plan(text, completionSegment: new TextRange(0, 0), insertText: "X", primaryEdit: edit, isOverstrikeMode: true);

		Assert.AreEqual(new TextRange(1, 1), plan.Operations[0].Range);
	}

	[TestMethod]
	public void Plan_PrimaryEditBeyondTheSnapshot_FallsBackToTheCompletionSegment()
	{
		const string text = "abcdef";

		var edit = new CompletionTextEdit(new TextRange(40, 2));

		CompletionCommitPlan plan = Plan(text, completionSegment: new TextRange(2, 1), insertText: "X", primaryEdit: edit, isOverstrikeMode: false);

		// The edit was produced against a longer document, so the live completion segment decides.
		Assert.AreEqual(new TextRange(2, 1), plan.Operations[0].Range);
		Assert.AreEqual("abXdef", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_CaretOffsetInInsertText_PlacesTheCaretFromTheInsertionStart()
	{
		const string text = "ab";

		CompletionCommitPlan plan = PlanWithCaret(text, completionSegment: new TextRange(2, 0), insertText: "foo()", caretOffsetInInsertText: 4);

		Assert.AreEqual(6, plan.CaretOffset);
		Assert.AreEqual("abfoo(", TextBeforeCaret(text, plan));
	}

	[TestMethod]
	public void Plan_CaretOffsetBeyondTheDocument_IsClampedToTheLengthAfterTheCommit()
	{
		const string text = "ab";

		CompletionCommitPlan plan = PlanWithCaret(text, completionSegment: new TextRange(2, 0), insertText: "x", caretOffsetInInsertText: 40);

		// The offset was derived against an earlier document state; the caret never leaves the committed document.
		Assert.AreEqual(3, plan.CaretOffset);
	}

	[TestMethod]
	public void Plan_CaretOffsetBeyondAShrunkDocument_IsClampedToTheLengthAfterTheCommit()
	{
		const string text = "abcdef";

		CompletionCommitPlan plan = PlanWithCaret(text, completionSegment: new TextRange(1, 4), insertText: "X", caretOffsetInInsertText: 40);

		// The commit removes four characters and inserts one, so the document ends at offset 3.
		Assert.AreEqual(3, plan.CaretOffset);
	}

	[TestMethod]
	public void Plan_CaretOffsetBeforeTheDocument_IsClampedToZero()
	{
		const string text = "ab";

		CompletionCommitPlan plan = PlanWithCaret(text, completionSegment: new TextRange(0, 0), insertText: "x", caretOffsetInInsertText: -5);

		Assert.AreEqual(0, plan.CaretOffset);
	}

	[TestMethod]
	public void Plan_HugeCaretOffset_DoesNotOverflowTheClamp()
	{
		const string text = "ab";

		CompletionCommitPlan plan = PlanWithCaret(
			text,
			completionSegment: new TextRange(2, 0),
			insertText: "x",
			caretOffsetInInsertText: int.MaxValue
		);

		Assert.AreEqual(3, plan.CaretOffset);
	}

	[TestMethod]
	public void Plan_AdditionalEditBeforeThePrimary_ShiftsTheCaretByItsLength()
	{
		const string text = "\nfoo.b";

		// The import goes in at the top of the file while the member is completed further down.
		var import = new CompletionTextEdit(new TextRange(0, 0), newText: "import x;");

		CompletionCommitPlan plan = PlanWithEdits(
			text,
			completionSegment: new TextRange(5, 1),
			insertText: "bar()",
			caretOffsetInInsertText: 4,
			additionalEdits: [import]
		);

		Assert.HasCount(2, plan.Operations);
		Assert.AreEqual("import x;\nfoo.bar()", Apply(text, plan));

		// The caret sits after the opening bracket of the committed member, wherever the import pushed it.
		Assert.AreEqual("import x;\nfoo.bar(", TextBeforeCaret(text, plan));
	}

	[TestMethod]
	public void Plan_AdditionalEditAfterThePrimary_LeavesTheCaretAlone()
	{
		const string text = "foo.b\n";

		var trailing = new CompletionTextEdit(new TextRange(6, 0), newText: "// done");

		CompletionCommitPlan plan = PlanWithEdits(
			text,
			completionSegment: new TextRange(4, 1),
			insertText: "bar()",
			caretOffsetInInsertText: 4,
			additionalEdits: [trailing]
		);

		Assert.AreEqual("foo.bar()\n// done", Apply(text, plan));
		Assert.AreEqual("foo.bar(", TextBeforeCaret(text, plan));
	}

	[TestMethod]
	public void Plan_AdditionalInsertionAtThePrimaryStart_ShiftsTheCaretByItsLength()
	{
		const string text = "b";

		var prefix = new CompletionTextEdit(new TextRange(0, 0), newText: "this.");

		CompletionCommitPlan plan = PlanWithEdits(
			text,
			completionSegment: new TextRange(0, 1),
			insertText: "bar()",
			caretOffsetInInsertText: 4,
			additionalEdits: [prefix]
		);

		Assert.AreEqual("this.bar()", Apply(text, plan));
		Assert.AreEqual("this.bar(", TextBeforeCaret(text, plan));
	}

	[TestMethod]
	public void Plan_AdditionalEditInsideThePrimaryReplacement_IsDropped()
	{
		const string text = "abcdef";

		var inside = new CompletionTextEdit(new TextRange(3, 0), newText: "!");

		CompletionCommitPlan plan = PlanWithEdits(
			text,
			completionSegment: new TextRange(1, 4),
			insertText: "X",
			caretOffsetInInsertText: null,
			additionalEdits: [inside]
		);

		// An insertion strictly inside a replacement conflicts with it, and the primary wins.
		Assert.HasCount(1, plan.Operations);
		Assert.AreEqual("aXf", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_AdditionalEditEndingAtThePrimaryStart_IsApplied()
	{
		const string text = "abpr";

		var touching = new CompletionTextEdit(new TextRange(0, 2), newText: "AB");

		CompletionCommitPlan plan = PlanWithEdits(
			text,
			completionSegment: new TextRange(2, 2),
			insertText: "print",
			caretOffsetInInsertText: null,
			additionalEdits: [touching]
		);

		// A range that only touches the primary range does not overlap it.
		Assert.AreEqual("ABprint", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_AdditionalReplacementOverlappingThePrimary_IsDropped()
	{
		const string text = "prx";

		var overlapping = new CompletionTextEdit(new TextRange(1, 2), newText: "X");

		CompletionCommitPlan plan = PlanWithEdits(
			text,
			completionSegment: new TextRange(0, 2),
			insertText: "print",
			caretOffsetInInsertText: null,
			additionalEdits: [overlapping]
		);

		Assert.HasCount(1, plan.Operations);
		Assert.AreEqual("printx", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_AdditionalReplacementEnclosingAPrimaryInsertion_IsDropped()
	{
		const string text = "prxyz";

		var enclosing = new CompletionTextEdit(new TextRange(0, 3), newText: "AB");

		CompletionCommitPlan plan = PlanWithEdits(
			text,
			completionSegment: new TextRange(2, 0),
			insertText: "print",
			caretOffsetInInsertText: null,
			additionalEdits: [enclosing]
		);

		// The primary is the batch's first entry, so the conflict is resolved in its favor and only the secondary edit is dropped.
		Assert.HasCount(1, plan.Operations);
		Assert.AreEqual("prprintxyz", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_OverlappingAdditionalReplacements_KeepTheFirstEntry()
	{
		const string text = "prwxyz";

		var first = new CompletionTextEdit(new TextRange(2, 2), newText: "A");
		var second = new CompletionTextEdit(new TextRange(3, 2), newText: "B");

		CompletionCommitPlan plan = PlanWithEdits(
			text,
			completionSegment: new TextRange(0, 2),
			insertText: "print",
			caretOffsetInInsertText: null,
			additionalEdits: [first, second]
		);

		Assert.HasCount(2, plan.Operations);
		Assert.AreEqual("printAyz", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_AdditionalEditWithAReplaceRange_ReplacesTheReplacementRange()
	{
		const string text = "abcdef";

		var wide = new CompletionTextEdit(new TextRange(3, 0), replaceRange: new TextRange(3, 2), newText: "Z");

		CompletionCommitPlan plan = PlanWithEdits(
			text,
			completionSegment: new TextRange(0, 1),
			insertText: "X",
			caretOffsetInInsertText: null,
			additionalEdits: [wide]
		);

		// An additional edit contributes its effective replacement range, not only its insert range.
		Assert.AreEqual("XbcZf", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_PrimaryInsertionAndAdditionalReplacementAtTheSameOffset_AppliesBoth()
	{
		const string text = "prx";

		var replacement = new CompletionTextEdit(new TextRange(2, 1), newText: "X");

		CompletionCommitPlan plan = PlanWithEdits(
			text,
			completionSegment: new TextRange(2, 0),
			insertText: "print",
			caretOffsetInInsertText: null,
			additionalEdits: [replacement]
		);

		// The replacement is ordered before an insertion that shares its start offset, so the inserted text lands at the insertion point and the
		// replacement replaces the character that followed it.
		Assert.AreEqual("prprintX", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_PrimaryInsertionAndAdditionalInsertionAtTheSameOffset_PutsThePrimaryLeftmost()
	{
		const string text = "pr";

		var insertion = new CompletionTextEdit(new TextRange(2, 0), newText: "X");

		CompletionCommitPlan plan = PlanWithEdits(
			text,
			completionSegment: new TextRange(2, 0),
			insertText: "print",
			caretOffsetInInsertText: null,
			additionalEdits: [insertion]
		);

		// Two insertions at one offset apply the higher edit index first, so the primary (edit index zero) applies last and its text lands leftmost.
		Assert.AreEqual("prprintX", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_AdditionalInsertionAtThePrimaryInsertionPoint_DoesNotShiftTheCaret()
	{
		const string text = "pr";

		var insertion = new CompletionTextEdit(new TextRange(2, 0), newText: "X");

		CompletionCommitPlan plan = PlanWithEdits(
			text,
			completionSegment: new TextRange(2, 0),
			insertText: "print",
			caretOffsetInInsertText: 5,
			additionalEdits: [insertion]
		);

		// The same-offset insertion is ordered before the primary insertion, so the caret stays at the end of the primary text, not after the
		// secondary one.
		Assert.AreEqual("prprint", TextBeforeCaret(text, plan));
	}

	[TestMethod]
	public void Plan_AdditionalEditWithoutText_IsSkipped()
	{
		const string text = "abc";

		var withoutText = new CompletionTextEdit(new TextRange(0, 1));
		var valid = new CompletionTextEdit(new TextRange(3, 0), newText: "!");

		CompletionCommitPlan plan = PlanWithEdits(
			text,
			completionSegment: new TextRange(1, 1),
			insertText: "X",
			caretOffsetInInsertText: null,
			additionalEdits: [withoutText, valid]
		);

		// An entry without explicit text cannot be applied, and it must not cost the commit its other edits.
		Assert.AreEqual("aXc!", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_AdditionalEditBeyondTheSnapshot_IsSkipped()
	{
		const string text = "abc";

		var stale = new CompletionTextEdit(new TextRange(40, 1), newText: "!");

		CompletionCommitPlan plan = PlanWithEdits(
			text,
			completionSegment: new TextRange(1, 1),
			insertText: "X",
			caretOffsetInInsertText: null,
			additionalEdits: [stale]
		);

		Assert.HasCount(1, plan.Operations);
		Assert.AreEqual("aXc", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_PrimaryThatChangesNothingBesideAdditionalEdits_IsDroppedWithoutShiftingTheCaret()
	{
		const string text = "abc";

		var near = new CompletionTextEdit(new TextRange(2, 0), newText: "?");
		var far = new CompletionTextEdit(new TextRange(3, 0), newText: "!");

		CompletionCommitPlan plan = PlanWithEdits(
			text,
			completionSegment: new TextRange(1, 0),
			insertText: "",
			caretOffsetInInsertText: 2,
			additionalEdits: [near, far]
		);

		// The kernel drops the no-op primary, and an edit after the primary start never shifts the caret, so it counts from the primary start.
		Assert.HasCount(2, plan.Operations);
		Assert.AreEqual(3, plan.CaretOffset);
		Assert.AreEqual("ab?c!", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_OperationsAreOrderedFromTheHighestOffsetDown()
	{
		const string text = "0123456789";

		var low = new CompletionTextEdit(new TextRange(0, 1), newText: "a");
		var high = new CompletionTextEdit(new TextRange(8, 1), newText: "b");

		CompletionCommitPlan plan = PlanWithEdits(
			text,
			completionSegment: new TextRange(4, 1),
			insertText: "X",
			caretOffsetInInsertText: null,
			additionalEdits: [low, high]
		);

		Assert.AreSequenceEqual([8, 4, 0], [.. plan.Operations.Select(static operation => operation.Range.Offset)]);
		Assert.AreEqual("a123X567b9", Apply(text, plan));
	}

	[TestMethod]
	public void Plan_NullMembersOfTheRequest_ThrowArgumentNullException()
	{
		var valid = new CompletionCommitPlanRequest(
			Snapshot: new StringTextSnapshot("ab"),
			CompletionSegment: default,
			InsertText: "x",
			CaretOffsetInInsertText: null,
			AdditionalTextEdits: [],
			PrimaryEdit: null,
			IsOverstrikeMode: false
		);

		var edit = new CompletionTextEdit(new TextRange(0, 0), newText: "y");

		Assert.ThrowsExactly<ArgumentNullException>(() => CompletionCommitPlanner.Plan(valid with { Snapshot = null! }));

		// Beside an additional edit the kernel would skip an entry without text, so only the guard rejects a missing insertion text.
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			CompletionCommitPlanner.Plan(valid with { InsertText = null!, AdditionalTextEdits = [edit] })
		);

		Assert.ThrowsExactly<ArgumentNullException>(() => CompletionCommitPlanner.Plan(valid with { AdditionalTextEdits = null! }));
	}

	[TestMethod]
	public void ResolvePrimaryRange_MatchesTheRangeThePlanReplaces()
	{
		var edit = new CompletionTextEdit(new TextRange(1, 1), replaceRange: new TextRange(1, 3));
		var stale = new CompletionTextEdit(new TextRange(40, 1));

		var request = new CompletionCommitPlanRequest(
			Snapshot: new StringTextSnapshot("abcdef"),
			CompletionSegment: new TextRange(0, 2),
			InsertText: "X",
			CaretOffsetInInsertText: null,
			AdditionalTextEdits: [],
			PrimaryEdit: null,
			IsOverstrikeMode: false
		);

		CompletionCommitPlanRequest[] requests =
		[
			request,

			request with
			{
				PrimaryEdit = edit,
			},

			request with
			{
				PrimaryEdit = edit,
				IsOverstrikeMode = true,
			},

			request with
			{
				PrimaryEdit = stale,
			},
		];

		Assert.AreSequenceEqual(
			[new TextRange(0, 2), new TextRange(1, 1), new TextRange(1, 3), new TextRange(0, 2)],
			[.. requests.Select(static item => CompletionCommitPlanner.ResolvePrimaryRange(item))]
		);

		foreach (CompletionCommitPlanRequest item in requests)
			Assert.AreEqual(CompletionCommitPlanner.ResolvePrimaryRange(item), CompletionCommitPlanner.Plan(item).Operations[0].Range);
	}

	[TestMethod]
	public void ResolvePrimaryRange_NullSnapshot_ThrowsArgumentNullException()
	{
		var request = new CompletionCommitPlanRequest(
			Snapshot: null!,
			CompletionSegment: default,
			InsertText: "x",
			CaretOffsetInInsertText: null,
			AdditionalTextEdits: [],
			PrimaryEdit: null,
			IsOverstrikeMode: false
		);

		Assert.ThrowsExactly<ArgumentNullException>(() => CompletionCommitPlanner.ResolvePrimaryRange(request));
	}

	private static CompletionCommitPlan Plan(string text, TextRange completionSegment, string insertText) =>
		PlanWithEdits(text, completionSegment, insertText, caretOffsetInInsertText: null, additionalEdits: []);

	private static CompletionCommitPlan PlanWithCaret(string text, TextRange completionSegment, string insertText, int caretOffsetInInsertText) =>
		PlanWithEdits(text, completionSegment, insertText, caretOffsetInInsertText, additionalEdits: []);

	private static CompletionCommitPlan PlanWithEdits(
		string text,
		TextRange completionSegment,
		string insertText,
		int? caretOffsetInInsertText,
		IReadOnlyList<CompletionTextEdit> additionalEdits
	) => Plan(text, completionSegment, insertText, primaryEdit: null, isOverstrikeMode: false, caretOffsetInInsertText, additionalEdits);

	private static CompletionCommitPlan Plan(
		string text,
		TextRange completionSegment,
		string insertText,
		CompletionTextEdit? primaryEdit,
		bool isOverstrikeMode,
		int? caretOffsetInInsertText = null,
		IReadOnlyList<CompletionTextEdit>? additionalEdits = null,
		int? caretOffset = null
	)
	{
		return CompletionCommitPlanner.Plan(
			new CompletionCommitPlanRequest(
				Snapshot: new StringTextSnapshot(text),
				completionSegment,
				insertText,
				caretOffsetInInsertText,
				AdditionalTextEdits: additionalEdits ?? [],
				primaryEdit,
				isOverstrikeMode,
				caretOffset
			)
		);
	}

	// Applies the plan the way a binding does: sequentially, in list order, each operation against its original coordinates.
	private static string Apply(string text, CompletionCommitPlan plan)
	{
		foreach (TextEditOperation operation in plan.Operations)
			text = text.Remove(operation.Range.Offset, operation.Range.Length).Insert(operation.Range.Offset, operation.NewText);

		return text;
	}

	private static string TextBeforeCaret(string text, CompletionCommitPlan plan)
	{
		int caret = plan.CaretOffset ?? throw new InvalidOperationException("The plan carries no caret offset.");
		return Apply(text, plan)[..caret];
	}
}
