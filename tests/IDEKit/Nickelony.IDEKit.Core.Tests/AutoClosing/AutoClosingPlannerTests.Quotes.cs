using static Nickelony.IDEKit.Core.Tests.AutoClosingPlannerTestHelpers;

namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class AutoClosingPlannerTests
{
	[TestMethod]
	public void TryPlanAction_DoubleQuoteBeforeExistingQuote_WithAlwaysOvertype_ReturnsSkipAction()
	{
		bool resolved = Resolve(text: "a\"b", caretOffset: 1, input: "\"", out AutoClosingAction action, options: AlwaysTrackedOptions);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual("\"", action.ClosingText);
	}

	[TestMethod]
	public void TryPlanAction_OpeningDoubleQuote_ReturnsInsertAction()
	{
		bool resolved = Resolve(text: "x = ", caretOffset: 4, input: "\"", out AutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual("\"", action.ClosingText);
	}

	[TestMethod]
	[DataRow("ab", DisplayName = "WordCharacter")]
	[DataRow("12", DisplayName = "Digit")]
	[DataRow("a_", DisplayName = "Underscore")]
	public void TryPlanAction_DoubleQuoteAfterWordCharacterDigitOrUnderscore_ReturnsFalse(string documentText)
	{
		bool resolved = Resolve(text: documentText, caretOffset: 2, input: "\"", action: out _);
		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_DoubleQuoteAfterSeparator_ReturnsInsertAction()
	{
		bool resolved = Resolve(text: "a = ", caretOffset: 4, input: "\"", out AutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryPlanAction_DoubleQuotePrecededBySameToken_ReturnsFalse()
	{
		// The doubling rule lets normal input build runs such as '"""'.
		bool resolved = Resolve(text: "\"", caretOffset: 1, input: "\"", action: out _);
		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_DoubleQuoteAtEndOfPair_ReturnsFalse()
	{
		bool resolved = Resolve(text: "\"\"", caretOffset: 2, input: "\"", action: out _);
		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_OpeningSingleQuote_ReturnsInsertAction()
	{
		bool resolved = Resolve(text: "x = ", caretOffset: 4, input: "'", out AutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual("'", action.ClosingText);
	}

	[TestMethod]
	public void TryPlanAction_SingleQuoteBeforeExistingQuote_WithAlwaysOvertype_ReturnsSkipAction()
	{
		bool resolved = Resolve(text: "a'b", caretOffset: 1, input: "'", out AutoClosingAction action, options: AlwaysTrackedOptions);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual("'", action.ClosingText);
	}

	[TestMethod]
	public void TryPlanAction_SingleQuoteAfterWordCharacter_ReturnsFalse()
	{
		bool resolved = Resolve(text: "ab", caretOffset: 2, input: "'", action: out _);
		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_Backtick_NotInDefaultOptions_ReturnsFalse()
	{
		// The caret sits at the end of the text, so option membership is the only gate left: the test
		// fails if the preset is added to the defaults.
		bool resolved = Resolve(text: "ab", caretOffset: 2, input: "`", action: out _);
		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_OpeningBacktick_WhenOptedIn_ReturnsInsertAction()
	{
		AutoClosingOptions options = CreateOptions(AutoClosingPair.Backticks);

		bool resolved = Resolve(text: "ab", caretOffset: 2, input: "`", out AutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual("`", action.ClosingText);
	}

	[TestMethod]
	public void TryPlanAction_BacktickBeforeExistingBacktick_WhenOptedIn_WithAlwaysOvertype_ReturnsSkipAction()
	{
		AutoClosingOptions options = CreateOptions(AutoClosingPair.Backticks) with { ClosingTextSkipTracking = AutoClosingTrackingMode.Always };

		bool resolved = Resolve(text: "a`b", caretOffset: 1, input: "`", out AutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual("`", action.ClosingText);
	}

	[TestMethod]
	public void TryPlanAction_QuoteKindPair_SuppressesAfterWordCharacter()
	{
		AutoClosingOptions options = CreateOptions(
			new AutoClosingPair("*", "*") { Kind = AutoClosingPairKind.Quote, SuppressAfterWordCharacter = true }
		);

		bool resolved = Resolve(text: "ab", caretOffset: 2, input: "*", action: out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_DoubleQuoteAfterWordCharacter_WithCustomIdentifierPolicy_ReturnsInsertAction()
	{
		AutoClosingOptions options = AutoClosingOptions.Default with
		{
			IdentifierPolicy = IdentifierCharacterPolicy.Create(static character => char.IsDigit(character)),
		};

		// 'b' is a word character under the default policy but not under the custom one.
		bool resolved = Resolve(text: "ab", caretOffset: 2, input: "\"", out AutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryPlanAction_QuotePrecededByCustomEscapeCharacter_IsNotSkipped()
	{
		AutoClosingOptions options = AutoClosingOptions.Default with
		{
			ClosingTextSkipTracking = AutoClosingTrackingMode.Always,
			EscapeCharacter = '#',
		};

		// '#' escapes the quote before it, so the existing closing text must not be skipped.
		bool resolved = Resolve(text: "a#\"", caretOffset: 2, input: "\"", action: out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_QuotePrecededByFormerEscapeCharacter_WithEscapeDisabled_IsSkipped()
	{
		AutoClosingOptions options = AutoClosingOptions.Default with
		{
			ClosingTextSkipTracking = AutoClosingTrackingMode.Always,
			EscapeCharacter = null,
		};

		bool resolved = Resolve(text: "a#\"", caretOffset: 2, input: "\"", out AutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.SkipExistingClosingText, action.Kind);
	}
}
