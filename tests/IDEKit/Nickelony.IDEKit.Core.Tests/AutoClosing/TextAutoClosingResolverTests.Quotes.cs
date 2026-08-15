namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class TextAutoClosingResolverTests
{
	[TestMethod]
	public void TryResolveAction_DoubleQuoteBeforeExistingQuote_WithAlwaysOvertype_ReturnsSkipAction()
	{
		bool resolved = Resolve("a\"b", 1, "\"", out TextAutoClosingAction action, s_alwaysOptions);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual("\"", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_OpeningDoubleQuote_ReturnsInsertAction()
	{
		bool resolved = Resolve("x = ", 4, "\"", out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual("\"", action.ClosingText);
	}

	[TestMethod]
	[DataRow("ab", DisplayName = "WordCharacter")]
	[DataRow("12", DisplayName = "Digit")]
	[DataRow("a_", DisplayName = "Underscore")]
	public void TryResolveAction_DoubleQuoteAfterWordCharacterDigitOrUnderscore_ReturnsFalse(string documentText)
	{
		bool resolved = Resolve(documentText, 2, "\"", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_DoubleQuoteAfterSeparator_ReturnsInsertAction()
	{
		bool resolved = Resolve("a = ", 4, "\"", out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryResolveAction_DoubleQuotePrecededBySameToken_ReturnsFalse()
	{
		// The doubling rule lets normal input build runs such as '"""'.
		bool resolved = Resolve("\"", 1, "\"", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_DoubleQuoteAtEndOfPair_ReturnsFalse()
	{
		bool resolved = Resolve("\"\"", 2, "\"", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_OpeningSingleQuote_ReturnsInsertAction()
	{
		bool resolved = Resolve("x = ", 4, "'", out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual("'", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_SingleQuoteBeforeExistingQuote_WithAlwaysOvertype_ReturnsSkipAction()
	{
		bool resolved = Resolve("a'b", 1, "'", out TextAutoClosingAction action, s_alwaysOptions);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual("'", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_SingleQuoteAfterWordCharacter_ReturnsFalse()
	{
		bool resolved = Resolve("ab", 2, "'", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_Backtick_NotInDefaultOptions_ReturnsFalse()
	{
		// The caret sits at the end of the text, so option membership is the only gate left: the test
		// fails if the preset is added to the defaults.
		bool resolved = Resolve("ab", 2, "`", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_OpeningBacktick_WhenOptedIn_ReturnsInsertAction()
	{
		TextAutoClosingOptions options = CreateOptions(TextAutoClosingPair.Backticks);

		bool resolved = Resolve("ab", 2, "`", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual("`", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_BacktickBeforeExistingBacktick_WhenOptedIn_WithAlwaysOvertype_ReturnsSkipAction()
	{
		TextAutoClosingOptions options = CreateOptions(TextAutoClosingPair.Backticks) with
		{
			ClosingTextSkipProvenance = TextAutoClosingProvenance.Always
		};

		bool resolved = Resolve("a`b", 1, "`", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual("`", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_QuoteKindPair_SuppressesAfterWordCharacter()
	{
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair("*", "*")
		{
			Kind = TextAutoClosingPairKind.Quote,
			SuppressAfterWordCharacter = true
		});

		bool resolved = Resolve("ab", 2, "*", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_DoubleQuoteAfterWordCharacter_WithCustomIdentifierPolicy_ReturnsInsertAction()
	{
		TextAutoClosingOptions options = TextAutoClosingOptions.Default with
		{
			IdentifierPolicy = IdentifierCharacterPolicy.Create(static character => char.IsDigit(character))
		};

		// 'b' is a word character under the default policy but not under the custom one.
		bool resolved = Resolve("ab", 2, "\"", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryResolveAction_QuotePrecededByCustomEscapeCharacter_IsNotSkipped()
	{
		TextAutoClosingOptions options = TextAutoClosingOptions.Default with
		{
			ClosingTextSkipProvenance = TextAutoClosingProvenance.Always,
			EscapeCharacter = '#'
		};

		// '#' escapes the quote before it, so the existing closing text must not be skipped.
		bool resolved = Resolve("a#\"", 2, "\"", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_QuotePrecededByFormerEscapeCharacter_WithEscapeDisabled_IsSkipped()
	{
		TextAutoClosingOptions options = TextAutoClosingOptions.Default with
		{
			ClosingTextSkipProvenance = TextAutoClosingProvenance.Always,
			EscapeCharacter = null
		};

		bool resolved = Resolve("a#\"", 2, "\"", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, action.Kind);
	}
}
