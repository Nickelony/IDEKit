using static Nickelony.IDEKit.Core.Tests.AutoClosingPlannerTestHelpers;

namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Verifies the editor-neutral auto-closing resolution over text snapshots: the gate pipeline, the tracking callback, the selection-wrap path,
/// and the pair-deletion query.
/// </summary>
[TestClass]
public sealed partial class AutoClosingPlannerTests
{
	[TestMethod]
	[DataRow("(", ")", DisplayName = "Parenthesis")]
	[DataRow("{", "}", DisplayName = "Brace")]
	[DataRow("[", "]", DisplayName = "Bracket")]
	public void TryPlanAction_OpeningToken_ReturnsInsertAction(string input, string closingText)
	{
		// The caret sits at the end of the text, where the character-after-the-caret gate allows auto-closing.
		bool resolved = Resolve(text: "ab", caretOffset: 2, input, out AutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(closingText, action.ClosingText);
	}

	[TestMethod]
	public void TryPlanAction_OpeningAngleBracket_WhenOptedIn_ReturnsInsertAction()
	{
		AutoClosingOptions options = CreateOptions(AutoClosingPair.AngleBrackets);

		bool resolved = Resolve(text: "ab", caretOffset: 2, input: "<", out AutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(">", action.ClosingText);
	}

	[TestMethod]
	public void TryPlanAction_OpeningAngleBracket_NotInDefaultOptions_ReturnsFalse()
	{
		// The caret sits at the end of the text, so option membership is the only gate left: the test
		// fails if the preset is added to the defaults.
		bool resolved = Resolve(text: "ab", caretOffset: 2, input: "<", action: out _);
		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_OutOfRangeCaretOffset_IsClampedToDocumentBounds()
	{
		const string text = ")a";

		(int Offset, string Input, bool Resolved, AutoClosingActionKind Kind, string? ClosingText)[] cases =
		[
			// Offsets below the document clamp to offset 0, where the closing parenthesis is at the caret.
			(int.MinValue, ")", true, AutoClosingActionKind.SkipExistingClosingText, ")"),
			(-1, ")", true, AutoClosingActionKind.SkipExistingClosingText, ")"),

			// Offsets beyond the document clamp to its end, where nothing follows the caret.
			(text.Length, ")", false, AutoClosingActionKind.None, null),
			(int.MaxValue, ")", false, AutoClosingActionKind.None, null),

			// An opening parenthesis inserts the closing string at the end boundary. At the start
			// boundary the closing parenthesis already at the caret suppresses the duplicate pair.
			(int.MinValue, "(", false, AutoClosingActionKind.None, null),
			(int.MaxValue, "(", true, AutoClosingActionKind.InsertClosingText, ")"),
		];

		foreach ((int offset, string input, bool expectedResolved, AutoClosingActionKind expectedKind, string? expectedClosingText) in cases)
		{
			// The mode is Always because the skip rows describe resolution shapes, not tracking.
			bool resolved = Resolve(text, caretOffset: offset, input, out AutoClosingAction action, options: AlwaysTrackedOptions);

			Assert.AreEqual(expectedResolved, resolved, $"Resolution mismatch for '{input}' at offset {offset}.");
			Assert.AreEqual(expectedKind, action.Kind, $"Action kind mismatch for '{input}' at offset {offset}.");
			Assert.AreEqual(expectedClosingText, action.ClosingText, $"Closing text mismatch for '{input}' at offset {offset}.");
		}
	}

	[TestMethod]
	public void TryPlanAction_ClosingBraceWithoutExisting_ReturnsFalse()
	{
		bool resolved = Resolve(text: "ab", caretOffset: 1, input: "}", action: out _);
		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_BracketKindPair_DoesNotSuppressAfterWordCharacter()
	{
		// Suppression after a word character is a quote rule; an explicit Bracket kind ignores the flag.
		AutoClosingOptions options = CreateOptions(new AutoClosingPair("*", "*") { SuppressAfterWordCharacter = true });

		bool resolved = Resolve(text: "ab", caretOffset: 2, input: "*", out AutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryPlanAction_SymmetricBracketPair_TokenAfterSameToken_IsNotDoubled()
	{
		// The token-run rule is not quote-only: a bracket-like pair whose opening token is also its
		// closing text is not tripled either, so typing '|' after '|' is left to normal text input.
		AutoClosingOptions options = CreateOptions(new AutoClosingPair("|", "|")) with
		{
			AutoCloseUnconditionally = true,
		};

		bool resolved = Resolve(text: "|", caretOffset: 1, input: "|", action: out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_SymmetricBracketPair_ClosingTextAtTheCaret_IsNotDuplicated()
	{
		// The character-after-the-caret gate admits the token when the host lists it, but the duplicated
		// closing text is still not inserted in front of itself.
		AutoClosingOptions options = CreateOptions(new AutoClosingPair("|", "|")) with
		{
			AutoCloseBefore = "|",
		};

		bool resolved = Resolve(text: "||", caretOffset: 1, input: "|", action: out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_AsymmetricBracketPair_TokenAfterSameToken_StillInsertsClosingText()
	{
		// The opening token of an asymmetric pair legitimately repeats: the inner '(' of '(())' must
		// still insert its closing text.
		AutoClosingOptions options = CreateOptions(AutoClosingPair.Parentheses) with
		{
			AutoCloseUnconditionally = true,
		};

		bool resolved = Resolve(text: "(", caretOffset: 1, input: "(", out AutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void TryPlanAction_MultiCharacterOpeningToken_MatchesWholeInput()
	{
		AutoClosingOptions options = CreateOptions(new AutoClosingPair("<<", ">>"));

		bool resolved = Resolve(text: "ab", caretOffset: 2, input: "<<", out AutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(">>", action.ClosingText);
	}

	[TestMethod]
	public void TryPlanAction_EmptyClosingTextPair_IsIgnored()
	{
		AutoClosingOptions options = CreateOptions(new AutoClosingPair("(", string.Empty));

		bool resolved = Resolve(text: "ab", caretOffset: 1, input: "(", action: out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_EmptyOpeningTextPair_IsIgnored()
	{
		AutoClosingOptions options = CreateOptions(new AutoClosingPair(string.Empty, ")"));

		bool resolved = Resolve(text: "ab", caretOffset: 1, input: "(", action: out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_SameOpeningTokenOnTwoPairs_FirstPairWins()
	{
		AutoClosingOptions options = CreateOptions(new AutoClosingPair("(", "first"), new AutoClosingPair("(", "second"));

		bool resolved = Resolve(text: "ab", caretOffset: 2, input: "(", out AutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual("first", action.ClosingText);
	}

	[TestMethod]
	public void TryPlanAction_EmptyInput_ReturnsFalse()
	{
		bool resolved = Resolve(text: "ab", caretOffset: 1, input: string.Empty, action: out _);
		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_NoMatch_ReturnsNoneKind()
	{
		bool resolved = Resolve(text: "ab", caretOffset: 1, input: "x", out AutoClosingAction action);

		Assert.IsFalse(resolved);
		Assert.AreEqual(AutoClosingActionKind.None, action.Kind);
		Assert.IsNull(action.ClosingText);
	}
}
