namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Verifies the editor-neutral auto-closing resolution over text snapshots: the gate pipeline, the
/// provenance callback, the selection-wrap path, and the pair-deletion query.
/// </summary>
[TestClass]
public sealed partial class TextAutoClosingResolverTests
{
	[TestMethod]
	[DataRow("(", ")", DisplayName = "Parenthesis")]
	[DataRow("{", "}", DisplayName = "Brace")]
	[DataRow("[", "]", DisplayName = "Bracket")]
	public void TryResolveAction_OpeningToken_ReturnsInsertAction(string input, string closingText)
	{
		// The caret sits at the end of the text, where the character-after-the-caret gate allows auto-closing.
		bool resolved = Resolve("ab", 2, input, out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(closingText, action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_OpeningAngleBracket_WhenOptedIn_ReturnsInsertAction()
	{
		TextAutoClosingOptions options = CreateOptions(TextAutoClosingPair.AngleBrackets);

		bool resolved = Resolve("ab", 2, "<", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(">", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_OpeningAngleBracket_NotInDefaultOptions_ReturnsFalse()
	{
		// The caret sits at the end of the text, so option membership is the only gate left: the test
		// fails if the preset is added to the defaults.
		bool resolved = Resolve("ab", 2, "<", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_OutOfRangeCaretOffset_IsClampedToDocumentBounds()
	{
		const string text = ")a";
		(int Offset, string Input, bool Resolved, TextAutoClosingActionKind Kind, string? ClosingText)[] cases =
		[
			// Offsets below the document clamp to offset 0, where the closing parenthesis is at the caret.
			(int.MinValue, ")", true, TextAutoClosingActionKind.SkipExistingClosingText, ")"),
			(-1, ")", true, TextAutoClosingActionKind.SkipExistingClosingText, ")"),

			// Offsets beyond the document clamp to its end, where nothing follows the caret.
			(text.Length, ")", false, TextAutoClosingActionKind.None, null),
			(int.MaxValue, ")", false, TextAutoClosingActionKind.None, null),

			// An opening parenthesis inserts the closing string at the end boundary. At the start
			// boundary the closing parenthesis already at the caret suppresses the duplicate pair.
			(int.MinValue, "(", false, TextAutoClosingActionKind.None, null),
			(int.MaxValue, "(", true, TextAutoClosingActionKind.InsertClosingText, ")"),
		];

		foreach ((int offset, string input, bool expectedResolved, TextAutoClosingActionKind expectedKind, string? expectedClosingText) in cases)
		{
			// The mode is Always because the skip rows describe resolution shapes, not provenance.
			bool resolved = Resolve(text, offset, input, out TextAutoClosingAction action, s_alwaysOptions);

			Assert.AreEqual(expectedResolved, resolved, $"Resolution mismatch for '{input}' at offset {offset}.");
			Assert.AreEqual(expectedKind, action.Kind, $"Action kind mismatch for '{input}' at offset {offset}.");
			Assert.AreEqual(expectedClosingText, action.ClosingText, $"Closing text mismatch for '{input}' at offset {offset}.");
		}
	}

	[TestMethod]
	public void TryResolveAction_ClosingBraceWithoutExisting_ReturnsFalse()
	{
		bool resolved = Resolve("ab", 1, "}", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_BracketKindPair_DoesNotSuppressAfterWordCharacter()
	{
		// Suppression after a word character is a quote rule; an explicit Bracket kind ignores the flag.
		TextAutoClosingOptions options = CreateOptions(
			new TextAutoClosingPair("*", "*") { SuppressAfterWordCharacter = true });

		bool resolved = Resolve("ab", 2, "*", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryResolveAction_SymmetricBracketPair_TokenAfterSameToken_IsNotDoubled()
	{
		// The token-run rule is not quote-only: a bracket-like pair whose opening token is also its
		// closing text is not tripled either, so typing '|' after '|' is left to normal text input.
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair("|", "|")) with
		{
			AutoCloseUnconditionally = true
		};

		bool resolved = Resolve("|", 1, "|", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_SymmetricBracketPair_ClosingTextAtTheCaret_IsNotDuplicated()
	{
		// The character-after-the-caret gate admits the token when the host lists it, but the duplicated
		// closing text is still not inserted in front of itself.
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair("|", "|")) with
		{
			AutoCloseBefore = "|"
		};

		bool resolved = Resolve("||", 1, "|", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_AsymmetricBracketPair_TokenAfterSameToken_StillInsertsClosingText()
	{
		// The opening token of an asymmetric pair legitimately repeats: the inner '(' of '(())' must
		// still insert its closing text.
		TextAutoClosingOptions options = CreateOptions(TextAutoClosingPair.Parentheses) with
		{
			AutoCloseUnconditionally = true
		};

		bool resolved = Resolve("(", 1, "(", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_MultiCharacterOpeningToken_MatchesWholeInput()
	{
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair("<<", ">>"));

		bool resolved = Resolve("ab", 2, "<<", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(">>", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_EmptyClosingTextPair_IsIgnored()
	{
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair("(", string.Empty));

		bool resolved = Resolve("ab", 1, "(", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_EmptyOpeningTextPair_IsIgnored()
	{
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair(string.Empty, ")"));

		bool resolved = Resolve("ab", 1, "(", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_SameOpeningTokenOnTwoPairs_FirstPairWins()
	{
		TextAutoClosingOptions options = CreateOptions(
			new TextAutoClosingPair("(", "first"),
			new TextAutoClosingPair("(", "second"));

		bool resolved = Resolve("ab", 2, "(", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual("first", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_EmptyInput_ReturnsFalse()
	{
		bool resolved = Resolve("ab", 1, string.Empty, out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_NoMatch_ReturnsNoneKind()
	{
		bool resolved = Resolve("ab", 1, "x", out TextAutoClosingAction action);

		Assert.IsFalse(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.None, action.Kind);
		Assert.IsNull(action.ClosingText);
	}
}
