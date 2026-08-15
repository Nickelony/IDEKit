using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

[TestClass]
public sealed class TextCompletionTextEditTests
{
	[TestMethod]
	public void ReplacementRange_FallsBackToInsertRange()
	{
		var edit = new TextCompletionTextEdit(new TextRange(2, 4));

		Assert.AreEqual(new TextRange(2, 4), edit.ReplacementRange);
		Assert.IsNull(edit.ReplaceRange);
	}

	[TestMethod]
	public void ReplacementRange_PrefersExplicitReplaceRange()
	{
		var edit = new TextCompletionTextEdit(new TextRange(2, 4), new TextRange(2, 10));

		Assert.AreEqual(new TextRange(2, 4), edit.InsertRange);
		Assert.AreEqual(new TextRange(2, 10), edit.ReplacementRange);
	}

	[TestMethod]
	public void Equality_SameValues_AreEqual()
	{
		var first = new TextCompletionTextEdit(new TextRange(2, 4), new TextRange(2, 10));
		var second = new TextCompletionTextEdit(new TextRange(2, 4), new TextRange(2, 10));

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void NewText_DefaultsToNull()
	{
		var edit = new TextCompletionTextEdit(new TextRange(2, 4));

		Assert.IsNull(edit.NewText);
	}

	[TestMethod]
	public void NewText_WhitespaceOnlyValue_IsPreserved()
	{
		// The edit text is the commit payload; a whitespace-only replacement therefore survives even
		// though the item's insertion text falls back to the label when it is unset.
		var edit = new TextCompletionTextEdit(new TextRange(2, 4), newText: " ");

		Assert.AreEqual(" ", edit.NewText);
	}

	[TestMethod]
	public void Equality_DifferentNewText_AreNotEqual()
	{
		var first = new TextCompletionTextEdit(new TextRange(2, 4), newText: "a");
		var second = new TextCompletionTextEdit(new TextRange(2, 4), newText: "b");

		Assert.AreNotEqual(first, second);
	}

	[TestMethod]
	public void Constructor_ReplaceRangeNotContainingInsertRange_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new TextCompletionTextEdit(new TextRange(0, 5), new TextRange(0, 3)));

	[TestMethod]
	public void Constructor_ReplaceRangeWithDifferentStart_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new TextCompletionTextEdit(new TextRange(2, 2), new TextRange(3, 4)));

	[TestMethod]
	public void Constructor_ReplaceRangeContainingInsertRange_IsAccepted()
	{
		var edit = new TextCompletionTextEdit(new TextRange(2, 2), new TextRange(2, 6));

		Assert.AreEqual(new TextRange(2, 2), edit.InsertRange);
		Assert.AreEqual(new TextRange(2, 6), edit.ReplacementRange);
	}

	[TestMethod]
	public void TryCreate_InvalidPair_ReturnsFalseWithoutThrowing()
	{
		Assert.IsFalse(TextCompletionTextEdit.TryCreate(new TextRange(0, 5), new TextRange(0, 3), "text", out TextCompletionTextEdit edit));
		Assert.AreEqual(default(TextCompletionTextEdit), edit);
	}

	[TestMethod]
	public void TryCreate_ValidPair_ReturnsTheEdit()
	{
		Assert.IsTrue(TextCompletionTextEdit.TryCreate(new TextRange(0, 3), new TextRange(0, 5), "text", out TextCompletionTextEdit edit));
		Assert.AreEqual(new TextRange(0, 5), edit.ReplacementRange);
		Assert.AreEqual("text", edit.NewText);
	}

	[TestMethod]
	public void Deconstruct_ReportsTheComponents()
	{
		var edit = new TextCompletionTextEdit(new TextRange(1, 2), new TextRange(1, 4), "text");

		(TextRange insertRange, TextRange? replaceRange, string? newText) = edit;

		Assert.AreEqual(new TextRange(1, 2), insertRange);
		Assert.AreEqual(new TextRange(1, 4), replaceRange);
		Assert.AreEqual("text", newText);
	}

	[TestMethod]
	public void WithExpression_ReplacesOnlyTheText()
	{
		// The ranges are constructor-only, so a with expression can update the replacement text
		// without being able to produce an invalid range pair.
		var edit = new TextCompletionTextEdit(new TextRange(2, 4), new TextRange(2, 10), "before");

		TextCompletionTextEdit updated = edit with { NewText = "after" };

		Assert.AreEqual(new TextRange(2, 4), updated.InsertRange);
		Assert.AreEqual(new TextRange(2, 10), updated.ReplaceRange);
		Assert.AreEqual("after", updated.NewText);
	}
}
