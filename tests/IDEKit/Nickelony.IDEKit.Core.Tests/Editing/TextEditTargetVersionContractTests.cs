using static Nickelony.IDEKit.Core.Tests.TextEditInputs;

namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the <see cref="IVersionedTextEditTarget"/> contract with a reference implementation of the
/// documented capture-compare-apply workflow: the batch is published only while the captured stamp
/// still matches, so a stale batch is rejected without touching the content.
/// </summary>
[TestClass]
public sealed class TextEditTargetVersionContractTests
{
	[TestMethod]
	public void TryApply_MatchingStamp_AppliesTheBatchAndAdvancesTheStamp()
	{
		var target = new StampedTextEditTarget("abcdef");
		TextEditPreparationResult prepared = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 1, "B"), CreateEdit(4, 0, "Z")]);

		long capturedStamp = target.Version;

		Assert.IsTrue(prepared.IsValid);
		Assert.IsTrue(target.TryApply(prepared.Edits, capturedStamp));
		Assert.AreEqual("aBcdZef", target.Text);

		// The applied batch is itself a change, so a batch prepared against the old content is stale.
		Assert.AreNotEqual(capturedStamp, target.Version);
	}

	[TestMethod]
	public void TryApply_StaleStamp_RejectsTheBatchWithoutApplyingIt()
	{
		var target = new StampedTextEditTarget("abcdef");
		long capturedStamp = target.Version;

		// A direct edit between the capture and the apply invalidates the prepared batch.
		target.Apply(TextEditKernel.Prepare([new TextEditOperation(0, 0, "X", 0)]).Edits);

		TextEditPreparationResult prepared = TextEditKernel.Prepare(
			new StringTextSnapshot("Xabcdef"),
			[CreateEdit(1, 1, "Y")]);

		Assert.IsFalse(target.TryApply(prepared.Edits, capturedStamp));
		Assert.AreEqual("Xabcdef", target.Text);
	}

	[TestMethod]
	public void TryApply_ReplayingAGivenStamp_AppliesOnlyOnce()
	{
		var target = new StampedTextEditTarget("abcdef");
		long capturedStamp = target.Version;
		PreparedTextEdits batch = TextEditKernel.Prepare(new StringTextSnapshot("abcdef"), [CreateEdit(1, 1, "B")]).Edits;

		Assert.IsTrue(target.TryApply(batch, capturedStamp));
		Assert.AreEqual("aBcdef", target.Text);

		// The first apply advanced the stamp, so replaying the same captured stamp is stale.
		Assert.IsFalse(target.TryApply(batch, capturedStamp));
		Assert.AreEqual("aBcdef", target.Text);
	}

	[TestMethod]
	public void TryApply_NullEdits_Throws()
	{
		var target = new StampedTextEditTarget("abcdef");

		Assert.ThrowsExactly<ArgumentNullException>(() => target.TryApply(null!, target.Version));
	}

	// A reference implementation of the documented workflow: the content is a plain string, the stamp
	// is an edit counter, and TryApply compares the stamp before it publishes anything, so a stale
	// batch never reaches the content.
	private sealed class StampedTextEditTarget : IVersionedTextEditTarget
	{
		public StampedTextEditTarget(string text) => Text = text;

		public string Text { get; private set; }

		public long Version { get; private set; }

		public void Apply(PreparedTextEdits edits)
		{
			ArgumentNullException.ThrowIfNull(edits);

			foreach (TextEditOperation operation in edits.Operations)
			{
				Text = string.Concat(
					Text.AsSpan(0, operation.StartOffset),
					operation.NewText,
					Text.AsSpan(operation.EndOffset));
			}

			Version++;
		}

		public bool TryApply(PreparedTextEdits edits, long expectedVersion)
		{
			ArgumentNullException.ThrowIfNull(edits);

			if (Version != expectedVersion)
				return false;

			Apply(edits);

			return true;
		}
	}
}
