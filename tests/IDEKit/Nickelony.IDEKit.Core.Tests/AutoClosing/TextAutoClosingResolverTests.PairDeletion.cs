namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class TextAutoClosingResolverTests
{
	[TestMethod]
	public void TryResolvePairDeletion_LoadedPair_WithAlwaysDelete_ReturnsThePair()
	{
		bool resolved = TextAutoClosingResolver.TryResolvePairDeletion(
			new TextAutoClosingDeletionRequest(
				new StringTextSnapshot("()"),
				1,
				s_alwaysOptions,
				IsTrackedClosingText: null),
			out TextAutoClosingPair? pair);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingPair.Parentheses, pair);
	}

	[TestMethod]
	public void TryResolvePairDeletion_LoadedPair_WithAutoDelete_ReturnsFalse()
	{
		// Nothing is tracked, so the default Auto delete mode leaves the loaded pair to the editor.
		bool resolved = TextAutoClosingResolver.TryResolvePairDeletion(
			new TextAutoClosingDeletionRequest(
				new StringTextSnapshot("()"),
				1,
				s_options,
				IsTrackedClosingText: null),
			out TextAutoClosingPair? pair);

		Assert.IsFalse(resolved);
		Assert.IsNull(pair);
	}

	[TestMethod]
	public void TryResolvePairDeletion_TrackedClosingText_WithAutoDelete_ReturnsThePair()
	{
		bool resolved = TextAutoClosingResolver.TryResolvePairDeletion(
			new TextAutoClosingDeletionRequest(
				new StringTextSnapshot("()"),
				1,
				s_options,
				IsTrackedClosingText: static offset => offset == 1),
			out TextAutoClosingPair? pair);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingPair.Parentheses, pair);
	}

	[TestMethod]
	public void TryResolvePairDeletion_MultiCharacterClosingText_ReturnsThePair()
	{
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair("(", "},")) with
		{
			PairDeletionProvenance = TextAutoClosingProvenance.Always
		};

		bool resolved = TextAutoClosingResolver.TryResolvePairDeletion(
			new TextAutoClosingDeletionRequest(
				new StringTextSnapshot("(},"),
				1,
				options,
				IsTrackedClosingText: null),
			out TextAutoClosingPair? pair);

		Assert.IsTrue(resolved);
		Assert.IsNotNull(pair);
		Assert.AreEqual("(", pair.Open);
		Assert.AreEqual("},", pair.Close);
	}

	[TestMethod]
	public void TryResolvePairDeletion_NotBetweenAPair_ReturnsFalse()
	{
		bool resolved = TextAutoClosingResolver.TryResolvePairDeletion(
			new TextAutoClosingDeletionRequest(
				new StringTextSnapshot("ab"),
				1,
				s_alwaysOptions,
				IsTrackedClosingText: null),
			out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_MultiCharacterClosingTextTyped_WhenEscaped_IsNotSkipped()
	{
		TextAutoClosingOptions options = CreateOptions(
			new TextAutoClosingPair("\"", "\"\"") { Kind = TextAutoClosingPairKind.Quote }) with
		{
			ClosingTextSkipProvenance = TextAutoClosingProvenance.Always
		};

		// The caret sits after one escape character (the default backslash), so the quote run at the
		// caret belongs to an escape sequence and must not be skipped.
		bool resolved = Resolve("a\\\"\"", 2, "\"\"", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_MultiCharacterClosingTextTyped_WhenEvenlyEscaped_IsSkipped()
	{
		TextAutoClosingOptions options = CreateOptions(
			new TextAutoClosingPair("\"", "\"\"") { Kind = TextAutoClosingPairKind.Quote }) with
		{
			ClosingTextSkipProvenance = TextAutoClosingProvenance.Always
		};

		// Two escape characters before the caret form an escaped backslash, so the quote run is not
		// escaped and the skip applies (escape parity).
		bool resolved = Resolve("a\\\\\"\"", 3, "\"\"", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, action.Kind);
	}

	[TestMethod]
	public void TryResolveAction_AsymmetricQuoteClosingTyped_WhenEscaped_IsNotSkipped()
	{
		TextAutoClosingOptions options = CreateOptions(
			new TextAutoClosingPair("\u00AB", "\u00BB") { Kind = TextAutoClosingPairKind.Quote }) with
		{
			ClosingTextSkipProvenance = TextAutoClosingProvenance.Always
		};

		// The closing quote at the caret is preceded by the escape character, so it is not skipped.
		bool resolved = Resolve("a\\\u00BB", 2, "\u00BB", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_QuoteTypedAfterDefaultEscapeCharacter_InsertsInsteadOfSkipping()
	{
		TextAutoClosingOptions options = TextAutoClosingOptions.Default with
		{
			ClosingTextSkipProvenance = TextAutoClosingProvenance.Always
		};

		// One backslash (the default escape character) precedes the caret, so the quote is escaped:
		// it is neither skipped nor treated as existing closing text, and normal insertion applies
		// (the caret sits at the end of the text, where the character-after-the-caret gate admits it).
		bool resolved = Resolve("a\\", 2, "\"", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual("\"", action.ClosingText);
	}

	[TestMethod]
	public void TryResolvePairDeletion_LoadedPair_WithNeverDelete_ReturnsFalse()
	{
		TextAutoClosingOptions options = TextAutoClosingOptions.Default with
		{
			PairDeletionProvenance = TextAutoClosingProvenance.Never
		};

		// Even with a tracked closing text, the Never delete mode leaves Backspace to the editor.
		bool resolved = TextAutoClosingResolver.TryResolvePairDeletion(
			new TextAutoClosingDeletionRequest(
				new StringTextSnapshot("()"),
				1,
				options,
				IsTrackedClosingText: static offset => offset == 1),
			out TextAutoClosingPair? pair);

		Assert.IsFalse(resolved);
		Assert.IsNull(pair);
	}

	[TestMethod]
	public void TryResolvePairDeletion_QuotePairOpeningTokenEscaped_ReturnsFalse()
	{
		TextAutoClosingOptions options = TextAutoClosingOptions.Default with
		{
			PairDeletionProvenance = TextAutoClosingProvenance.Always
		};

		// The opening quote is preceded by the escape character, so the pair belongs to an escape
		// sequence and Backspace must leave it to the editor.
		bool resolved = TextAutoClosingResolver.TryResolvePairDeletion(
			new TextAutoClosingDeletionRequest(
				new StringTextSnapshot("\\\"\""),
				2,
				options,
				IsTrackedClosingText: null),
			out TextAutoClosingPair? pair);

		Assert.IsFalse(resolved);
		Assert.IsNull(pair);
	}

	[TestMethod]
	public void TryResolvePairDeletion_QuotePairEvenlyEscaped_ReturnsThePair()
	{
		TextAutoClosingOptions options = TextAutoClosingOptions.Default with
		{
			PairDeletionProvenance = TextAutoClosingProvenance.Always
		};

		// Two escape characters form an escaped backslash, so the pair is not escaped (escape parity)
		// and Backspace removes it.
		bool resolved = TextAutoClosingResolver.TryResolvePairDeletion(
			new TextAutoClosingDeletionRequest(
				new StringTextSnapshot("\\\\\"\""),
				3,
				options,
				IsTrackedClosingText: null),
			out TextAutoClosingPair? pair);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingPair.DoubleQuotes, pair);
	}
}
