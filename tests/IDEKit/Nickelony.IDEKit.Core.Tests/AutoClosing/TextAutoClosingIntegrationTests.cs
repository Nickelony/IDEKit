namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the auto-closing behaviors added for the multi-event opener matching, the escaped-pair
/// deletion guard, the wrapping skip gate, the unconditional strategy, and the scan opt-out.
/// </summary>
[TestClass]
public sealed class TextAutoClosingIntegrationTests
{
	[TestMethod]
	public void MultiCharacterOpener_CompletesAcrossInputEvents()
	{
		// The opening token is recognized by completion: after "/" the next "*" completes "/*".
		TextAutoClosingOptions options = Options(new TextAutoClosingPair("/*", "*/"));

		bool resolved = Resolve("x /", 3, "*", options, out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual("*/", action.ClosingText);
	}

	[TestMethod]
	public void MultiCharacterOpener_PartialOrMisplacedInput_ResolvesNothing()
	{
		TextAutoClosingOptions options = Options(new TextAutoClosingPair("/*", "*/"));

		Assert.IsFalse(Resolve("x ", 2, "/", options, out _));
		Assert.IsFalse(Resolve("x y", 3, "*", options, out _));
	}

	[TestMethod]
	public void PairDeletion_EscapedQuotePair_IsNotRemoved()
	{
		var options = new TextAutoClosingOptions
		{
			Pairs = [TextAutoClosingPair.DoubleQuotes],
			PairDeletionProvenance = TextAutoClosingProvenance.Always
		};

		// The opening quote is escaped, so the pair is not a deletion candidate.
		Assert.IsFalse(TextAutoClosingResolver.TryResolvePairDeletion(
			new TextAutoClosingDeletionRequest(
				new StringTextSnapshot("\\\"\""), 2, options, null),
			out _));

		// The unescaped control case still resolves.
		Assert.IsTrue(TextAutoClosingResolver.TryResolvePairDeletion(
			new TextAutoClosingDeletionRequest(
				new StringTextSnapshot("\"\""), 1, options, null),
			out TextAutoClosingPair? pair));
		Assert.AreEqual("\"", pair!.Close);
	}

	[TestMethod]
	public void WrappingRequest_NeverResolvesSkip()
	{
		var options = new TextAutoClosingOptions
		{
			Pairs = [TextAutoClosingPair.Parentheses],
			ClosingTextSkipProvenance = TextAutoClosingProvenance.Always
		};
		var snapshot = new StringTextSnapshot(")");

		var wrapping = new TextAutoClosingRequest(snapshot, 0, ")", options, IsWrappingSelection: true);

		Assert.IsFalse(TextAutoClosingResolver.TryResolveAction(wrapping, out _));

		var collapsed = new TextAutoClosingRequest(snapshot, 0, ")", options);

		Assert.IsTrue(TextAutoClosingResolver.TryResolveAction(collapsed, out TextAutoClosingAction action));
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, action.Kind);
	}

	[TestMethod]
	public void AutoCloseUnconditionally_SkipsTheBeforeGateAndWordSuppression()
	{
		Assert.IsFalse(Resolve("ab", 1, "(", Options(TextAutoClosingPair.Parentheses), out _));

		TextAutoClosingOptions unconditional = Options(TextAutoClosingPair.Parentheses) with
		{
			AutoCloseUnconditionally = true
		};

		Assert.IsTrue(Resolve("ab", 1, "(", unconditional, out TextAutoClosingAction bracketAction));
		Assert.AreEqual(")", bracketAction.ClosingText);

		// Quote presets suppress auto-closing after a word character; the flag disables that too.
		Assert.IsFalse(Resolve("ab", 2, "\"", Options(TextAutoClosingPair.DoubleQuotes), out _));

		TextAutoClosingOptions unconditionalQuotes = Options(TextAutoClosingPair.DoubleQuotes) with
		{
			AutoCloseUnconditionally = true
		};

		Assert.IsTrue(Resolve("ab", 2, "\"", unconditionalQuotes, out TextAutoClosingAction quoteAction));
		Assert.AreEqual("\"", quoteAction.ClosingText);
	}

	[TestMethod]
	public void CheckUnmatchedClosingText_False_AllowsInsertInFrontOfRawCloser()
	{
		// The raw-text scan sees the ')' inside the string and suppresses the default pair.
		Assert.IsFalse(Resolve("m = \")\";", 4, "(", Options(TextAutoClosingPair.Parentheses), out _));

		TextAutoClosingOptions optedOut = Options(
			new TextAutoClosingPair("(", ")") { CheckUnmatchedClosingText = false });

		Assert.IsTrue(Resolve("m = \")\";", 4, "(", optedOut, out TextAutoClosingAction action));
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void ActionValidation_RejectsInsertOrSkipWithoutClosingText()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new TextAutoClosingAction(
			TextAutoClosingActionKind.InsertClosingText, null));
		Assert.ThrowsExactly<ArgumentException>(() => new TextAutoClosingAction(
			TextAutoClosingActionKind.SkipExistingClosingText, null));

		var none = new TextAutoClosingAction(TextAutoClosingActionKind.None, null);

		Assert.AreEqual(TextAutoClosingActionKind.None, none.Kind);
	}

	[TestMethod]
	public void AutoCloseUnconditionally_SkipsTheUnmatchedCloserScan()
	{
		// The default pair declines in front of raw closing text on the caret's line.
		Assert.IsFalse(Resolve("ab)c", 2, "(", Options(TextAutoClosingPair.Parentheses), out _));

		TextAutoClosingOptions unconditional = Options(TextAutoClosingPair.Parentheses) with
		{
			AutoCloseUnconditionally = true
		};

		// The unconditional strategy skips the scan, so the insert resolves.
		Assert.IsTrue(Resolve("ab)c", 2, "(", unconditional, out TextAutoClosingAction action));
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void AutoCloseUnconditionally_DoublingAndEscapeStillApply()
	{
		TextAutoClosingOptions unconditionalQuotes = Options(TextAutoClosingPair.DoubleQuotes) with
		{
			AutoCloseUnconditionally = true
		};

		// The doubled-token rule survives the unconditional strategy: typing a quote directly after
		// the same token stays normal text input.
		Assert.IsFalse(Resolve("\"", 1, "\"", unconditionalQuotes, out _));

		// An escaped quote likewise resolves nothing.
		Assert.IsFalse(Resolve("\\\"", 2, "\"", unconditionalQuotes, out _));
	}

	[TestMethod]
	public void PairDeletion_OutOfRangeCaretAndEmptyToken_FollowTheContract()
	{
		var options = new TextAutoClosingOptions
		{
			Pairs = [TextAutoClosingPair.Parentheses],
			PairDeletionProvenance = TextAutoClosingProvenance.Always
		};

		// Offsets outside the snapshot are clamped, so an extreme caret probes a boundary position
		// and resolves no pair instead of throwing.
		Assert.IsFalse(TextAutoClosingResolver.TryResolvePairDeletion(
			new TextAutoClosingDeletionRequest(
				new StringTextSnapshot("()"), int.MinValue, options, null),
			out _));
		Assert.IsFalse(TextAutoClosingResolver.TryResolvePairDeletion(
			new TextAutoClosingDeletionRequest(
				new StringTextSnapshot("()"), 99, options, null),
			out _));

		// A pair with an empty closing token can never match and is skipped.
		var emptyTokenOptions = new TextAutoClosingOptions
		{
			Pairs = [new TextAutoClosingPair("(", string.Empty)],
			PairDeletionProvenance = TextAutoClosingProvenance.Always
		};

		Assert.IsFalse(TextAutoClosingResolver.TryResolvePairDeletion(
			new TextAutoClosingDeletionRequest(
				new StringTextSnapshot("("), 1, emptyTokenOptions, null),
			out _));
	}

	private static TextAutoClosingOptions Options(params TextAutoClosingPair[] pairs) => new() { Pairs = pairs };

	private static bool Resolve(
		string text,
		int caretOffset,
		string input,
		TextAutoClosingOptions options,
		out TextAutoClosingAction action)
		=> TextAutoClosingResolver.TryResolveAction(
			new TextAutoClosingRequest(new StringTextSnapshot(text), caretOffset, input, options),
			out action);
}
