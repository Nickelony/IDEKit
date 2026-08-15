namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class TextAutoClosingResolverTests
{
	[TestMethod]
	public void TryResolveAction_BeforeCharacterOutsideTheBracketSet_ReturnsFalse()
	{
		// The default bracket set replaced the former "any non-word character" rule, matching the fixed sets
		// mainstream desktop editors use.
		foreach (char nextCharacter in "(-@")
		{
			bool resolved = Resolve(nextCharacter.ToString(), 0, "(", out _);

			Assert.IsFalse(resolved, $"Expected no auto-closing before '{nextCharacter}'.");
		}
	}

	[TestMethod]
	public void TryResolveAction_BracketBeforeQuoteCharacter_ReturnsInsertAction()
	{
		// The default bracket set includes the quote characters.
		bool resolved = Resolve("\"", 0, "(", out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	[DataRow("ab", DisplayName = "WordCharacter")]
	[DataRow("a1", DisplayName = "Digit")]
	public void TryResolveAction_OpeningTokenBeforeWordCharacter_ReturnsFalse(string documentText)
	{
		// Auto-closing is suppressed when a letter, digit, or underscore follows the caret.
		bool resolved = Resolve(documentText, 1, "(", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	[DataRow("a b", 1, DisplayName = "Whitespace")]
	[DataRow("ab", 2, DisplayName = "EndOfText")]
	public void TryResolveAction_OpeningTokenBeforeWhitespaceOrEndOfText_ReturnsInsertAction(string documentText, int caretOffset)
	{
		bool resolved = Resolve(documentText, caretOffset, "(", out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryResolveAction_OpeningTokenBeforePunctuation_ReturnsInsertAction()
	{
		// Punctuation is not a word character, so the default gate allows auto-closing.
		bool resolved = Resolve("x,y", 1, "(", out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_AutoCloseBeforeOption_AllowsListedWordCharacter()
	{
		var options = new TextAutoClosingOptions
		{
			Pairs = [TextAutoClosingPair.Parentheses],
			AutoCloseBefore = "b"
		};

		bool resolved = Resolve("ab", 1, "(", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_OpeningTokenBeforeExistingClosingText_ReturnsFalse()
	{
		// Typing '(' in front of ')' must not duplicate the closing text.
		bool resolved = Resolve(")", 0, "(", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_OpeningTokenBeforeUnmatchedClosingText_ReturnsFalse()
	{
		// The closing parenthesis later on the caret's line has no opening token after the caret, so
		// inserting a closing text would duplicate it.
		bool resolved = Resolve("ab)c", 2, "(", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_OpeningTokenAfterMatchedPairOnTheLine_ReturnsInsertAction()
	{
		// The depth scan must not treat a balanced pair after the caret as a duplicate closing text:
		// the '(' raises the depth and its ')' lowers it back to zero.
		bool resolved = Resolve("f() g()", 3, "(", out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_OpeningTokenAfterMatchedPairWithLaterUnmatchedClosingText_ReturnsFalse()
	{
		// The scan walks a balanced pair first and then finds a closing text whose depth is zero, so the
		// action must be declined even though the character after the caret would pass the gate.
		bool resolved = Resolve("x (a) )", 1, "(", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	[DataRow("ab\ncd)", DisplayName = "Lf")]
	[DataRow("ab\r\ncd)", DisplayName = "CrLf")]
	public void TryResolveAction_UnmatchedClosingTextOnTheNextLine_ReturnsInsertAction(string text)
	{
		// The unmatched-closing scan stops at the caret's line end, so a closing text on the next line
		// does not suppress the insert.
		bool resolved = Resolve(text, 2, "(", out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryResolveAction_UnmatchedClosingTextOnTheSameLineBeforeCrLf_ReturnsFalse()
	{
		// The scan covers the caret's line up to the terminator, which starts at the CR of a CRLF pair.
		bool resolved = Resolve("ab)\r\ncd", 2, "(", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_CaretBetweenCrLfPair_ScansThePrecedingLineOnly()
	{
		// A position on the LF of a CRLF pair belongs to the preceding line, whose end is the CR: the
		// closing text on the next line must not suppress the insert.
		bool resolved = Resolve("ab\r\ncd)", 3, "(", out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryResolveAction_EmptyAutoCloseBeforeSet_AdmitsOnlyWhitespaceAndTheEndOfText()
	{
		TextAutoClosingOptions options = TextAutoClosingOptions.Default with { AutoCloseBefore = string.Empty };

		// A configured set replaces the kind's preset, so an empty set admits no character.
		Assert.IsFalse(Resolve("(a", 1, "(", out _, options));

		// Whitespace and the end of the text are always admitted.
		bool resolvedAfterWhitespace = Resolve("( a", 1, "(", out TextAutoClosingAction afterWhitespace, options);
		Assert.IsTrue(resolvedAfterWhitespace);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, afterWhitespace.Kind);

		bool resolvedAtEnd = Resolve("(", 1, "(", out TextAutoClosingAction atEnd, options);
		Assert.IsTrue(resolvedAtEnd);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, atEnd.Kind);
	}
}
