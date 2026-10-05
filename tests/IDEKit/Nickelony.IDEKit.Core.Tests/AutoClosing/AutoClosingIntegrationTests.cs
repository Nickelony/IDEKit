namespace Nickelony.IDEKit.Core.Tests;

/// <summary>Pins auto-closing scenarios that cross the resolver's gates.</summary>
[TestClass]
public sealed class AutoClosingIntegrationTests
{
	[TestMethod]
	public void MultiCharacterOpener_CompletesAcrossInputEvents()
	{
		// The opening token is recognized by completion: after "/" the next "*" completes "/*".
		AutoClosingOptions options = Options(new AutoClosingPair("/*", "*/"));

		bool resolved = Resolve(text: "x /", caretOffset: 3, input: "*", options, out AutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual("*/", action.ClosingText);
	}

	[TestMethod]
	public void MultiCharacterOpener_PartialOrMisplacedInput_ResolvesNothing()
	{
		AutoClosingOptions options = Options(new AutoClosingPair("/*", "*/"));

		Assert.IsFalse(Resolve(text: "x ", caretOffset: 2, input: "/", options, action: out _));
		Assert.IsFalse(Resolve(text: "x y", caretOffset: 3, input: "*", options, action: out _));
	}

	[TestMethod]
	public void PairDeletion_EscapedQuotePair_IsNotRemoved()
	{
		var options = new AutoClosingOptions { Pairs = [AutoClosingPair.DoubleQuotes], PairDeletionTracking = AutoClosingTrackingMode.Always };

		// The opening quote is escaped, so the pair is not a deletion candidate.
		Assert.IsFalse(
			AutoClosingPlanner.TryPlanPairDeletion(
				new AutoClosingDeletionRequest(Snapshot: new StringTextSnapshot("\\\"\""), CaretOffset: 2, options, IsTrackedClosingText: null),
				out _
			)
		);

		// The unescaped control case still resolves.
		Assert.IsTrue(
			AutoClosingPlanner.TryPlanPairDeletion(
				new AutoClosingDeletionRequest(Snapshot: new StringTextSnapshot("\"\""), CaretOffset: 1, options, IsTrackedClosingText: null),
				out AutoClosingPair? pair
			)
		);

		Assert.AreEqual("\"", pair!.Close);
	}

	[TestMethod]
	public void WrappingRequest_NeverResolvesSkip()
	{
		var options = new AutoClosingOptions { Pairs = [AutoClosingPair.Parentheses], ClosingTextSkipTracking = AutoClosingTrackingMode.Always };

		var snapshot = new StringTextSnapshot(")");

		var wrapping = new AutoClosingRequest(snapshot, CaretOffset: 0, InputText: ")", options, IsWrappingSelection: true);

		Assert.IsFalse(AutoClosingPlanner.TryPlanAction(wrapping, out _));

		var collapsed = new AutoClosingRequest(snapshot, CaretOffset: 0, InputText: ")", options);

		Assert.IsTrue(AutoClosingPlanner.TryPlanAction(collapsed, out AutoClosingAction action));
		Assert.AreEqual(AutoClosingActionKind.SkipExistingClosingText, action.Kind);
	}

	[TestMethod]
	public void AutoCloseUnconditionally_SkipsTheBeforeGateAndWordSuppression()
	{
		Assert.IsFalse(Resolve(text: "ab", caretOffset: 1, input: "(", options: Options(AutoClosingPair.Parentheses), action: out _));

		AutoClosingOptions unconditional = Options(AutoClosingPair.Parentheses) with { AutoCloseUnconditionally = true };

		Assert.IsTrue(Resolve(text: "ab", caretOffset: 1, input: "(", options: unconditional, action: out AutoClosingAction bracketAction));
		Assert.AreEqual(")", bracketAction.ClosingText);

		// Quote presets suppress auto-closing after a word character; the flag disables that too.
		Assert.IsFalse(Resolve(text: "ab", caretOffset: 2, input: "\"", options: Options(AutoClosingPair.DoubleQuotes), action: out _));

		AutoClosingOptions unconditionalQuotes = Options(AutoClosingPair.DoubleQuotes) with { AutoCloseUnconditionally = true };

		Assert.IsTrue(Resolve(text: "ab", caretOffset: 2, input: "\"", options: unconditionalQuotes, action: out AutoClosingAction quoteAction));
		Assert.AreEqual("\"", quoteAction.ClosingText);
	}

	[TestMethod]
	public void CheckUnmatchedClosingText_False_AllowsInsertInFrontOfRawCloser()
	{
		// The raw-text scan sees the ')' inside the string and suppresses the default pair.
		Assert.IsFalse(Resolve(text: "m = \")\";", caretOffset: 4, input: "(", options: Options(AutoClosingPair.Parentheses), action: out _));

		AutoClosingOptions optedOut = Options(new AutoClosingPair("(", ")") { CheckUnmatchedClosingText = false });

		Assert.IsTrue(Resolve(text: "m = \")\";", caretOffset: 4, input: "(", options: optedOut, out AutoClosingAction action));
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void ActionValidation_RejectsInsertOrSkipWithoutClosingText()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new AutoClosingAction(AutoClosingActionKind.InsertClosingText, null));
		Assert.ThrowsExactly<ArgumentException>(() => new AutoClosingAction(AutoClosingActionKind.SkipExistingClosingText, null));

		var none = new AutoClosingAction(AutoClosingActionKind.None, null);

		Assert.AreEqual(AutoClosingActionKind.None, none.Kind);
	}

	[TestMethod]
	public void AutoCloseUnconditionally_SkipsTheUnmatchedCloserScan()
	{
		// The default pair declines in front of raw closing text on the caret's line.
		Assert.IsFalse(Resolve(text: "ab)c", caretOffset: 2, input: "(", options: Options(AutoClosingPair.Parentheses), action: out _));

		AutoClosingOptions unconditional = Options(AutoClosingPair.Parentheses) with { AutoCloseUnconditionally = true };

		// The unconditional strategy skips the scan, so the insert resolves.
		Assert.IsTrue(Resolve(text: "ab)c", caretOffset: 2, input: "(", options: unconditional, out AutoClosingAction action));
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void AutoCloseUnconditionally_DoublingAndEscapeStillApply()
	{
		AutoClosingOptions unconditionalQuotes = Options(AutoClosingPair.DoubleQuotes) with { AutoCloseUnconditionally = true };

		// The doubled-token rule survives the unconditional strategy: typing a quote directly after
		// the same token stays normal text input.
		Assert.IsFalse(Resolve(text: "\"", caretOffset: 1, input: "\"", options: unconditionalQuotes, action: out _));

		// An escaped quote likewise resolves nothing.
		Assert.IsFalse(Resolve(text: "\\\"", caretOffset: 2, input: "\"", options: unconditionalQuotes, action: out _));
	}

	[TestMethod]
	public void PairDeletion_OutOfRangeCaretAndEmptyToken_FollowTheContract()
	{
		var options = new AutoClosingOptions { Pairs = [AutoClosingPair.Parentheses], PairDeletionTracking = AutoClosingTrackingMode.Always };

		// Offsets outside the snapshot are clamped, so an extreme caret probes a boundary position
		// and resolves no pair instead of throwing.
		Assert.IsFalse(
			AutoClosingPlanner.TryPlanPairDeletion(
				new AutoClosingDeletionRequest(
					Snapshot: new StringTextSnapshot("()"),
					CaretOffset: int.MinValue,
					options,
					IsTrackedClosingText: null
				),

				out _
			)
		);

		Assert.IsFalse(
			AutoClosingPlanner.TryPlanPairDeletion(
				new AutoClosingDeletionRequest(Snapshot: new StringTextSnapshot("()"), CaretOffset: 99, options, IsTrackedClosingText: null),
				out _
			)
		);

		// A pair with an empty closing token can never match and is skipped.
		var emptyTokenOptions = new AutoClosingOptions
		{
			Pairs = [new AutoClosingPair("(", string.Empty)],
			PairDeletionTracking = AutoClosingTrackingMode.Always,
		};

		Assert.IsFalse(
			AutoClosingPlanner.TryPlanPairDeletion(
				new AutoClosingDeletionRequest(
					Snapshot: new StringTextSnapshot("("),
					CaretOffset: 1,
					Options: emptyTokenOptions,
					IsTrackedClosingText: null
				),

				out _
			)
		);
	}

	private static AutoClosingOptions Options(params AutoClosingPair[] pairs) => new() { Pairs = pairs };

	private static bool Resolve(string text, int caretOffset, string input, AutoClosingOptions options, out AutoClosingAction action)
	{
		return AutoClosingPlanner.TryPlanAction(
			new AutoClosingRequest(Snapshot: new StringTextSnapshot(text), caretOffset, InputText: input, options),
			out action
		);
	}
}
