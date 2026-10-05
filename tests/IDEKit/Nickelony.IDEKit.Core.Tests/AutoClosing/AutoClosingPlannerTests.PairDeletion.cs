using static Nickelony.IDEKit.Core.Tests.AutoClosingPlannerTestHelpers;

namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class AutoClosingPlannerTests
{
	[TestMethod]
	public void TryPlanPairDeletion_LoadedPair_WithAlwaysDelete_ReturnsThePair()
	{
		bool resolved = AutoClosingPlanner.TryPlanPairDeletion(
			new AutoClosingDeletionRequest(
				Snapshot: new StringTextSnapshot("()"),
				CaretOffset: 1,
				Options: AlwaysTrackedOptions,
				IsTrackedClosingText: null
			),

			out AutoClosingPair? pair
		);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingPair.Parentheses, pair);
	}

	[TestMethod]
	public void TryPlanPairDeletion_LoadedPair_WithAutoDelete_ReturnsFalse()
	{
		// Nothing is tracked, so the default TrackedOnly pair-deletion tracking mode leaves the loaded pair to the editor.
		bool resolved = AutoClosingPlanner.TryPlanPairDeletion(
			new AutoClosingDeletionRequest(
				Snapshot: new StringTextSnapshot("()"),
				CaretOffset: 1,
				Options: DefaultOptions,
				IsTrackedClosingText: null
			),

			out AutoClosingPair? pair
		);

		Assert.IsFalse(resolved);
		Assert.IsNull(pair);
	}

	[TestMethod]
	public void TryPlanPairDeletion_TrackedClosingText_WithAutoDelete_ReturnsThePair()
	{
		bool resolved = AutoClosingPlanner.TryPlanPairDeletion(
			new AutoClosingDeletionRequest(
				Snapshot: new StringTextSnapshot("()"),
				CaretOffset: 1,
				Options: DefaultOptions,
				IsTrackedClosingText: static offset => offset == 1
			),

			out AutoClosingPair? pair
		);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingPair.Parentheses, pair);
	}

	[TestMethod]
	public void TryPlanPairDeletion_MultiCharacterClosingText_ReturnsThePair()
	{
		AutoClosingOptions options = CreateOptions(new AutoClosingPair("(", "},")) with { PairDeletionTracking = AutoClosingTrackingMode.Always };

		bool resolved = AutoClosingPlanner.TryPlanPairDeletion(
			new AutoClosingDeletionRequest(Snapshot: new StringTextSnapshot("(},"), CaretOffset: 1, options, IsTrackedClosingText: null),
			out AutoClosingPair? pair
		);

		Assert.IsTrue(resolved);
		Assert.IsNotNull(pair);
		Assert.AreEqual("(", pair.Open);
		Assert.AreEqual("},", pair.Close);
	}

	[TestMethod]
	public void TryPlanPairDeletion_NotBetweenAPair_ReturnsFalse()
	{
		bool resolved = AutoClosingPlanner.TryPlanPairDeletion(
			new AutoClosingDeletionRequest(
				Snapshot: new StringTextSnapshot("ab"),
				CaretOffset: 1,
				Options: AlwaysTrackedOptions,
				IsTrackedClosingText: null
			),

			out _
		);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_MultiCharacterClosingTextTyped_WhenEscaped_IsNotSkipped()
	{
		AutoClosingOptions options = CreateOptions(new AutoClosingPair("\"", "\"\"") { Kind = AutoClosingPairKind.Quote }) with
		{
			ClosingTextSkipTracking = AutoClosingTrackingMode.Always,
		};

		// The caret sits after one escape character (the default backslash), so the quote run at the
		// caret belongs to an escape sequence and must not be skipped.
		bool resolved = Resolve(text: "a\\\"\"", caretOffset: 2, input: "\"\"", action: out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_MultiCharacterClosingTextTyped_WhenEvenlyEscaped_IsSkipped()
	{
		AutoClosingOptions options = CreateOptions(new AutoClosingPair("\"", "\"\"") { Kind = AutoClosingPairKind.Quote }) with
		{
			ClosingTextSkipTracking = AutoClosingTrackingMode.Always,
		};

		// Two escape characters before the caret form an escaped backslash, so the quote run is not
		// escaped and the skip applies (escape parity).
		bool resolved = Resolve(text: "a\\\\\"\"", caretOffset: 3, input: "\"\"", out AutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.SkipExistingClosingText, action.Kind);
	}

	[TestMethod]
	public void TryPlanAction_AsymmetricQuoteClosingTyped_WhenEscaped_IsNotSkipped()
	{
		AutoClosingOptions options = CreateOptions(new AutoClosingPair("\u00AB", "\u00BB") { Kind = AutoClosingPairKind.Quote }) with
		{
			ClosingTextSkipTracking = AutoClosingTrackingMode.Always,
		};

		// The closing quote at the caret is preceded by the escape character, so it is not skipped.
		bool resolved = Resolve(text: "a\\\u00BB", caretOffset: 2, input: "\u00BB", action: out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_QuoteTypedAfterDefaultEscapeCharacter_InsertsInsteadOfSkipping()
	{
		AutoClosingOptions options = AutoClosingOptions.Default with { ClosingTextSkipTracking = AutoClosingTrackingMode.Always };

		// One backslash (the default escape character) precedes the caret, so the quote is escaped:
		// it is neither skipped nor treated as existing closing text, and normal insertion applies
		// (the caret sits at the end of the text, where the character-after-the-caret gate admits it).
		bool resolved = Resolve(text: "a\\", caretOffset: 2, input: "\"", out AutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual("\"", action.ClosingText);
	}

	[TestMethod]
	public void TryPlanPairDeletion_LoadedPair_WithNeverDelete_ReturnsFalse()
	{
		AutoClosingOptions options = AutoClosingOptions.Default with { PairDeletionTracking = AutoClosingTrackingMode.Never };

		// Even with a tracked closing text, the Never pair-deletion tracking mode leaves Backspace to the editor.
		bool resolved = AutoClosingPlanner.TryPlanPairDeletion(
			new AutoClosingDeletionRequest(
				Snapshot: new StringTextSnapshot("()"),
				CaretOffset: 1,
				options,
				IsTrackedClosingText: static offset => offset == 1
			),

			out AutoClosingPair? pair
		);

		Assert.IsFalse(resolved);
		Assert.IsNull(pair);
	}

	[TestMethod]
	public void TryPlanPairDeletion_QuotePairOpeningTokenEscaped_ReturnsFalse()
	{
		AutoClosingOptions options = AutoClosingOptions.Default with { PairDeletionTracking = AutoClosingTrackingMode.Always };

		// The opening quote is preceded by the escape character, so the pair belongs to an escape
		// sequence and Backspace must leave it to the editor.
		bool resolved = AutoClosingPlanner.TryPlanPairDeletion(
			new AutoClosingDeletionRequest(Snapshot: new StringTextSnapshot("\\\"\""), CaretOffset: 2, options, IsTrackedClosingText: null),
			out AutoClosingPair? pair
		);

		Assert.IsFalse(resolved);
		Assert.IsNull(pair);
	}

	[TestMethod]
	public void TryPlanPairDeletion_QuotePairEvenlyEscaped_ReturnsThePair()
	{
		AutoClosingOptions options = AutoClosingOptions.Default with { PairDeletionTracking = AutoClosingTrackingMode.Always };

		// Two escape characters form an escaped backslash, so the pair is not escaped (escape parity)
		// and Backspace removes it.
		bool resolved = AutoClosingPlanner.TryPlanPairDeletion(
			new AutoClosingDeletionRequest(Snapshot: new StringTextSnapshot("\\\\\"\""), CaretOffset: 3, options, IsTrackedClosingText: null),
			out AutoClosingPair? pair
		);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingPair.DoubleQuotes, pair);
	}
}
