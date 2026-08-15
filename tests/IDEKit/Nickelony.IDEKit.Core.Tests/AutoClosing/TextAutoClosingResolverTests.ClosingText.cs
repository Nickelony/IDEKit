namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class TextAutoClosingResolverTests
{
	[TestMethod]
	[DataRow("a)b", ")", DisplayName = "ClosingParenthesis")]
	[DataRow("[]", "]", DisplayName = "ClosingBracketAtExistingBracket")]
	[DataRow("{}", "}", DisplayName = "ClosingBraceAtExistingBrace")]
	public void TryResolveAction_ClosingTokenBeforeExisting_WithAlwaysOvertype_ReturnsSkipAction(string documentText, string input)
	{
		bool resolved = Resolve(documentText, 1, input, out TextAutoClosingAction action, s_alwaysOptions);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual(input, action.ClosingText);
	}

	[TestMethod]
	[DataRow("a)b", ")", DisplayName = "ClosingParenthesis")]
	[DataRow("[]", "]", DisplayName = "ClosingBracketAtExistingBracket")]
	public void TryResolveAction_ClosingTokenBeforeUntrackedExisting_AutoOvertype_ReturnsFalse(string documentText, string input)
	{
		// Nothing is tracked (the callback is null), so the default Auto provenance declines the skip
		// and normal text input applies.
		bool resolved = Resolve(documentText, 1, input, out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_ClosingTokenBeforeExisting_WithNeverOvertype_ReturnsFalse()
	{
		// Never turns the overtype path off entirely, so the closing token is normal text input even
		// when the closing text sits at the caret.
		var options = TextAutoClosingOptions.Default with { ClosingTextSkipProvenance = TextAutoClosingProvenance.Never };

		bool resolved = Resolve("a)b", 1, ")", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_TrackedClosingText_WithAutoOvertype_ReturnsSkipAction()
	{
		// The callback reports the closing text at the caret as tracked, so the default Auto mode skips it.
		bool resolved = Resolve(
			"a)b",
			1,
			")",
			out TextAutoClosingAction action,
			isTrackedClosingText: static offset => offset == 1);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_TrackedAtDifferentOffset_WithAutoOvertype_ReturnsFalse()
	{
		// A tracked closing text elsewhere must not enable the skip at the caret.
		bool resolved = Resolve(
			"a)b",
			1,
			")",
			out _,
			isTrackedClosingText: static offset => offset == 0);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	[DataRow("},", DisplayName = "WholeClosingString")]
	[DataRow("}", DisplayName = "LeadingTokenOnly")]
	public void TryResolveAction_TypingClosingTextWithTrailingComma_WithAlwaysOvertype_SkipsWholeClosingString(string input)
	{
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair("{", "},")) with
		{
			ClosingTextSkipProvenance = TextAutoClosingProvenance.Always
		};

		bool resolved = Resolve("{},", 1, input, out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual("},", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_TypingNonLeadingPartOfClosingString_ReturnsFalse()
	{
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair("{", "},"));

		bool resolved = Resolve("{},", 1, ",", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_MultiCharacterClosingString_PartialMatchAtCaret_DoesNotSkip()
	{
		// Only one of the two closing characters follows the caret, so the skip must not resolve.
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair("{", "}}"));

		bool resolved = Resolve("{}", 1, "}", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_MultiCharacterQuoteClosingString_WithAlwaysOvertype_SkipsWholeClosingString()
	{
		TextAutoClosingOptions options = CreateOptions(
			new TextAutoClosingPair("\"", "\"\"") { Kind = TextAutoClosingPairKind.Quote }) with
		{
			ClosingTextSkipProvenance = TextAutoClosingProvenance.Always
		};

		bool resolved = Resolve("\"\"rest", 0, "\"", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual("\"\"", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_ClosingTokenBeforeCaret_StillInsertsClosingElement()
	{
		// An existing closing text is skipped only when it starts at the caret; the same token before
		// the caret is unrelated text, so typing the opening token inserts the closing text.
		TextAutoClosingOptions options = CreateOptions(TextAutoClosingPair.Brackets);

		bool resolved = Resolve("] b", 1, "[", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual("]", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_ClosingTokenOfOtherPair_ReturnsFalse()
	{
		// A closing token is only meaningful to its own pair, and a pair that is not part of the
		// options cannot contribute an action, so typing the closing token of another pair does nothing.
		TextAutoClosingOptions options = CreateOptions(TextAutoClosingPair.Brackets);

		bool resolved = Resolve("]b", 1, ")", out TextAutoClosingAction action, options);

		Assert.IsFalse(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.None, action.Kind);
	}
}
