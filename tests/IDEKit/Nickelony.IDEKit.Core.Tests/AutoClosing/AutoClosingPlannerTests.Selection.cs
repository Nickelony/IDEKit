using static Nickelony.IDEKit.Core.Tests.AutoClosingPlannerTestHelpers;

namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class AutoClosingPlannerTests
{
	[TestMethod]
	public void TryPlanAction_IsWrappingSelection_WithWrappingPair_ReturnsInsertAction()
	{
		bool resolved = Resolve(text: "abcd", caretOffset: 3, input: "(", out AutoClosingAction action, wrappingSelection: true);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void TryPlanAction_IsWrappingSelection_WithWrapDisabledPair_ReturnsFalse()
	{
		AutoClosingOptions options = CreateOptions(new AutoClosingPair("(", ")") { WrapSelection = false });

		bool resolved = Resolve(text: "abcd", caretOffset: 3, input: "(", action: out _, options, wrappingSelection: true);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_IsWrappingSelection_BypassesWordCharacterSuppression()
	{
		// Wrapping bypasses the collapsed-caret gates: the quote is inserted even after a word character.
		bool resolved = Resolve(text: "ab", caretOffset: 2, input: "\"", out AutoClosingAction action, wrappingSelection: true);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryPlanAction_IsWrappingSelection_ClosingTokenTyped_ReturnsFalse()
	{
		// A wrapping request never resolves a skip or a closing-token action: the closing token replaces
		// the selection through normal text editing.
		bool resolved = Resolve(text: "abcd", caretOffset: 3, input: ")", action: out _, wrappingSelection: true);
		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_IsWrappingSelection_ClosingTokenOverExistingClosingText_ReturnsFalse()
	{
		// The same token over an existing closing text is a skip in the collapsed-caret path, which a
		// wrapping request never resolves either.
		AutoClosingOptions options = AutoClosingOptions.Default with
		{
			ClosingTextSkipTracking = AutoClosingTrackingMode.Always,
		};

		bool resolved = Resolve(text: ")", caretOffset: 0, input: ")", action: out _, options, wrappingSelection: true);

		Assert.IsFalse(resolved);
	}
}
