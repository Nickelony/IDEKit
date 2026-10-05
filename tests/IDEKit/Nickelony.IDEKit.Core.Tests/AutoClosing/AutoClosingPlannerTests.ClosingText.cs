using static Nickelony.IDEKit.Core.Tests.AutoClosingPlannerTestHelpers;

namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class AutoClosingPlannerTests
{
	[TestMethod]
	[DataRow("a)b", ")", DisplayName = "ClosingParenthesis")]
	[DataRow("[]", "]", DisplayName = "ClosingBracketAtExistingBracket")]
	[DataRow("{}", "}", DisplayName = "ClosingBraceAtExistingBrace")]
	public void TryPlanAction_ClosingTokenBeforeExisting_WithAlwaysOvertype_ReturnsSkipAction(string documentText, string input)
	{
		bool resolved = Resolve(text: documentText, caretOffset: 1, input, out AutoClosingAction action, options: AlwaysTrackedOptions);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual(input, action.ClosingText);
	}

	[TestMethod]
	[DataRow("a)b", ")", DisplayName = "ClosingParenthesis")]
	[DataRow("[]", "]", DisplayName = "ClosingBracketAtExistingBracket")]
	public void TryPlanAction_ClosingTokenBeforeUntrackedExisting_AutoOvertype_ReturnsFalse(string documentText, string input)
	{
		// Nothing is tracked (the callback is null), so the default TrackedOnly tracking mode declines the skip
		// and normal text input applies.
		bool resolved = Resolve(text: documentText, caretOffset: 1, input, action: out _);
		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_ClosingTokenBeforeExisting_WithNeverOvertype_ReturnsFalse()
	{
		// Never turns the overtype path off entirely, so the closing token is normal text input even
		// when the closing text sits at the caret.
		AutoClosingOptions options = AutoClosingOptions.Default with
		{
			ClosingTextSkipTracking = AutoClosingTrackingMode.Never,
		};

		bool resolved = Resolve(text: "a)b", caretOffset: 1, input: ")", action: out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_TrackedClosingText_WithAutoOvertype_ReturnsSkipAction()
	{
		// The callback reports the closing text at the caret as tracked, so the default Auto mode skips it.
		bool resolved = Resolve(
			text: "a)b",
			caretOffset: 1,
			input: ")",
			out AutoClosingAction action,
			isTrackedClosingText: static offset => offset == 1
		);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void TryPlanAction_TrackedAtDifferentOffset_WithAutoOvertype_ReturnsFalse()
	{
		// A tracked closing text elsewhere must not enable the skip at the caret.
		bool resolved = Resolve(text: "a)b", caretOffset: 1, input: ")", action: out _, isTrackedClosingText: static offset => offset == 0);
		Assert.IsFalse(resolved);
	}

	[TestMethod]
	[DataRow("},", DisplayName = "WholeClosingString")]
	[DataRow("}", DisplayName = "LeadingTokenOnly")]
	public void TryPlanAction_TypingClosingTextWithTrailingComma_WithAlwaysOvertype_SkipsWholeClosingString(string input)
	{
		AutoClosingOptions options = CreateOptions(new AutoClosingPair("{", "},")) with { ClosingTextSkipTracking = AutoClosingTrackingMode.Always };

		bool resolved = Resolve(text: "{},", caretOffset: 1, input, out AutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual("},", action.ClosingText);
	}

	[TestMethod]
	public void TryPlanAction_TypingNonLeadingPartOfClosingString_ReturnsFalse()
	{
		AutoClosingOptions options = CreateOptions(new AutoClosingPair("{", "},"));

		bool resolved = Resolve(text: "{},", caretOffset: 1, input: ",", action: out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_MultiCharacterClosingString_PartialMatchAtCaret_DoesNotSkip()
	{
		// Only one of the two closing characters follows the caret, so the skip must not resolve.
		AutoClosingOptions options = CreateOptions(new AutoClosingPair("{", "}}"));

		bool resolved = Resolve(text: "{}", caretOffset: 1, input: "}", action: out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_MultiCharacterQuoteClosingString_WithAlwaysOvertype_SkipsWholeClosingString()
	{
		AutoClosingOptions options = CreateOptions(new AutoClosingPair("\"", "\"\"") { Kind = AutoClosingPairKind.Quote }) with
		{
			ClosingTextSkipTracking = AutoClosingTrackingMode.Always,
		};

		bool resolved = Resolve(text: "\"\"rest", caretOffset: 0, input: "\"", out AutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual("\"\"", action.ClosingText);
	}

	[TestMethod]
	public void TryPlanAction_ClosingTokenBeforeCaret_StillInsertsClosingElement()
	{
		// An existing closing text is skipped only when it starts at the caret; the same token before
		// the caret is unrelated text, so typing the opening token inserts the closing text.
		AutoClosingOptions options = CreateOptions(AutoClosingPair.Brackets);

		bool resolved = Resolve(text: "] b", caretOffset: 1, input: "[", out AutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual("]", action.ClosingText);
	}

	[TestMethod]
	public void TryPlanAction_ClosingTokenOfOtherPair_ReturnsFalse()
	{
		// A closing token is only meaningful to its own pair, and a pair that is not part of the
		// options cannot contribute an action, so typing the closing token of another pair does nothing.
		AutoClosingOptions options = CreateOptions(AutoClosingPair.Brackets);

		bool resolved = Resolve(text: "]b", caretOffset: 1, input: ")", out AutoClosingAction action, options);

		Assert.IsFalse(resolved);
		Assert.AreEqual(AutoClosingActionKind.None, action.Kind);
	}
}
