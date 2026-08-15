namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class TextAutoClosingResolverTests
{
	[TestMethod]
	public void TryResolveAction_IsWrappingSelection_WithWrappingPair_ReturnsInsertAction()
	{
		bool resolved = Resolve("abcd", 3, "(", out TextAutoClosingAction action, wrappingSelection: true);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_IsWrappingSelection_WithWrapDisabledPair_ReturnsFalse()
	{
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair("(", ")") { WrapSelection = false });

		bool resolved = Resolve("abcd", 3, "(", out _, options, wrappingSelection: true);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_IsWrappingSelection_BypassesWordCharacterSuppression()
	{
		// Wrapping bypasses the collapsed-caret gates: the quote is inserted even after a word character.
		bool resolved = Resolve("ab", 2, "\"", out TextAutoClosingAction action, wrappingSelection: true);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryResolveAction_IsWrappingSelection_ClosingTokenTyped_ReturnsFalse()
	{
		// A wrapping request never resolves a skip or a closing-token action: the closing token replaces
		// the selection through normal text editing.
		bool resolved = Resolve("abcd", 3, ")", out _, wrappingSelection: true);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_IsWrappingSelection_ClosingTokenOverExistingClosingText_ReturnsFalse()
	{
		// The same token over an existing closing text is a skip in the collapsed-caret path, which a
		// wrapping request never resolves either.
		TextAutoClosingOptions options = TextAutoClosingOptions.Default with
		{
			ClosingTextSkipProvenance = TextAutoClosingProvenance.Always
		};

		bool resolved = Resolve(")", 0, ")", out _, options, wrappingSelection: true);

		Assert.IsFalse(resolved);
	}
}
