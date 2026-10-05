using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Formatting;
using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.IDEKit.LanguageFeatures.Formatting;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class FormattingPlannerTests
{
	private const string DocumentId = "formatting-test.lua";

	private static WorkspaceEdit CreateEdit(params TextEdit[] edits) => new([new TextDocumentEdit(DocumentId, edits)]);

	private static TextEdit Edit(int startLine, int startCharacter, int endLine, int endCharacter, string newText) =>
		new(new TextPositionRange(new TextPosition(startLine, startCharacter), new TextPosition(endLine, endCharacter)), newText);

	[TestMethod]
	public void TryPlan_Edits_BecomeOneBatchInApplicationOrder()
	{
		WorkspaceEdit response = CreateEdit(
			Edit(startLine: 0, startCharacter: 11, endLine: 0, endCharacter: 14, newText: string.Empty),
			Edit(startLine: 1, startCharacter: 11, endLine: 1, endCharacter: 13, newText: string.Empty)
		);

		bool planned = FormattingPlanner.TryPlan(
			documentText: "local a = 1   \nlocal b = 2  \nlocal c = 3",
			DocumentId,
			response,
			out PreparedTextEdits edits,
			failure: out _
		);

		Assert.IsTrue(planned);
		Assert.HasCount(2, edits.Operations);
		Assert.IsGreaterThan(edits.Operations[1].Range.Offset, edits.Operations[0].Range.Offset);
	}

	[TestMethod]
	public void TryPlan_DeclinedAnswer_HasNothingToApply()
	{
		Assert.IsFalse(FormattingPlanner.TryPlan(documentText: "print(1)", DocumentId, response: null, edits: out _, out FormattingOutcome failure));
		Assert.AreEqual(FormattingOutcome.NothingToApply, failure);
	}

	[TestMethod]
	public void TryPlan_AnswerWithoutEditsOfTheDocument_HasNothingToApply()
	{
		WorkspaceEdit noEdits = new([new TextDocumentEdit(DocumentId, [])]);

		WorkspaceEdit otherDocument = new([
			new TextDocumentEdit("other.lua", [Edit(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 5, newText: "x")]),
		]);

		Assert.IsFalse(
			FormattingPlanner.TryPlan(documentText: "print(1)", DocumentId, response: noEdits, edits: out _, failure: out FormattingOutcome first)
		);

		Assert.IsFalse(
			FormattingPlanner.TryPlan(
				documentText: "print(1)",
				DocumentId,
				response: otherDocument,
				edits: out _,
				failure: out FormattingOutcome second
			)
		);

		Assert.AreEqual(FormattingOutcome.NothingToApply, first);
		Assert.AreEqual(FormattingOutcome.NothingToApply, second);
	}

	[TestMethod]
	public void TryPlan_EditsThatChangeNothing_HaveNothingToApply()
	{
		Assert.IsFalse(
			FormattingPlanner.TryPlan(
				documentText: "print(1)",
				DocumentId,
				response: CreateEdit(Edit(startLine: 0, startCharacter: 2, endLine: 0, endCharacter: 2, newText: string.Empty)),
				edits: out _,
				out FormattingOutcome failure
			)
		);

		Assert.AreEqual(FormattingOutcome.NothingToApply, failure);
	}

	[TestMethod]
	public void TryPlan_ConflictingEdits_AreRejectedAsAWholeBatch()
	{
		WorkspaceEdit response = CreateEdit(
			Edit(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 5, newText: "PRINT"),
			Edit(startLine: 0, startCharacter: 3, endLine: 0, endCharacter: 7, newText: "x")
		);

		Assert.IsFalse(FormattingPlanner.TryPlan(documentText: "print(1)", DocumentId, response, edits: out _, out FormattingOutcome failure));
		Assert.AreEqual(FormattingOutcome.Rejected, failure);
	}

	[TestMethod]
	public void TryPlan_ReversedRange_IsRejected()
	{
		Assert.IsFalse(
			FormattingPlanner.TryPlan(
				documentText: "print(1)",
				DocumentId,
				response: CreateEdit(Edit(startLine: 0, startCharacter: 5, endLine: 0, endCharacter: 1, newText: "x")),
				edits: out _,
				out FormattingOutcome failure
			)
		);

		Assert.AreEqual(FormattingOutcome.Rejected, failure);
	}

	[TestMethod]
	public void TryPlan_NullArguments_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			FormattingPlanner.TryPlan(documentText: null!, DocumentId, response: null, edits: out _, failure: out _)
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			FormattingPlanner.TryPlan(documentText: "text", documentId: null!, response: null, edits: out _, failure: out _)
		);
	}
}
