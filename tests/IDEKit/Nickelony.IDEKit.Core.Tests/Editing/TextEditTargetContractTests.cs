using static Nickelony.IDEKit.Core.Tests.TextEditInputs;

namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextEditTargetContractTests
{
	[TestMethod]
	public void Apply_PreparedBatchInListOrder_ProducesTheEditedText()
	{
		var target = new NaiveTextEditTarget("abcdef");
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[
				CreateEdit(1, 1, "B"),
				CreateEdit(4, 0, "Z")
			]);

		Assert.IsTrue(result.IsValid);

		// The target contract is that a prepared batch applies in list order without sorting, so a
		// target that does exactly that must produce the edited text from the descending operations.
		target.Apply(result.Edits);

		Assert.AreEqual("aBcdZef", target.Text);
	}

	// A minimal ITextEditTarget implementation: it applies the validated operations in list order,
	// exactly as the interface documents, with no sorting or offset bookkeeping of its own.
	private sealed class NaiveTextEditTarget : ITextEditTarget
	{
		public NaiveTextEditTarget(string text) => Text = text;

		public string Text { get; private set; }

		public void Apply(PreparedTextEdits edits)
		{
			foreach (TextEditOperation operation in edits.Operations)
			{
				Text = string.Concat(
					Text.AsSpan(0, operation.StartOffset),
					operation.NewText,
					Text.AsSpan(operation.EndOffset));
			}
		}
	}
}
