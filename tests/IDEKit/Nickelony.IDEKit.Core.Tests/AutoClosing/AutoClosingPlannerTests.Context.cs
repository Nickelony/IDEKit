using static Nickelony.IDEKit.Core.Tests.AutoClosingPlannerTestHelpers;

namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class AutoClosingPlannerTests
{
	[TestMethod]
	public void TryPlanAction_BeforeCharacterOutsideTheBracketSet_ReturnsFalse()
	{
		// The default bracket set (DefaultBracketAutoCloseBefore) excludes '(', '-' and '@'.
		foreach (char nextCharacter in "(-@")
		{
			bool resolved = Resolve(text: nextCharacter.ToString(), caretOffset: 0, input: "(", action: out _);
			Assert.IsFalse(resolved, $"Expected no auto-closing before '{nextCharacter}'.");
		}
	}

	[TestMethod]
	public void TryPlanAction_BracketBeforeQuoteCharacter_ReturnsInsertAction()
	{
		// The default bracket set includes the quote characters.
		bool resolved = Resolve(text: "\"", caretOffset: 0, input: "(", out AutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	[DataRow("ab", DisplayName = "WordCharacter")]
	[DataRow("a1", DisplayName = "Digit")]
	public void TryPlanAction_OpeningTokenBeforeWordCharacter_ReturnsFalse(string documentText)
	{
		// Auto-closing is suppressed when a letter, digit, or underscore follows the caret.
		bool resolved = Resolve(text: documentText, caretOffset: 1, input: "(", action: out _);
		Assert.IsFalse(resolved);
	}

	[TestMethod]
	[DataRow("a b", 1, DisplayName = "Whitespace")]
	[DataRow("ab", 2, DisplayName = "EndOfText")]
	public void TryPlanAction_OpeningTokenBeforeWhitespaceOrEndOfText_ReturnsInsertAction(string documentText, int caretOffset)
	{
		bool resolved = Resolve(text: documentText, caretOffset, input: "(", out AutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryPlanAction_OpeningTokenBeforePunctuation_ReturnsInsertAction()
	{
		// Punctuation is not a word character, so the default gate allows auto-closing.
		bool resolved = Resolve(text: "x,y", caretOffset: 1, input: "(", out AutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void TryPlanAction_AutoCloseBeforeOption_AllowsListedWordCharacter()
	{
		var options = new AutoClosingOptions { Pairs = [AutoClosingPair.Parentheses], AutoCloseBefore = "b" };

		bool resolved = Resolve(text: "ab", caretOffset: 1, input: "(", out AutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void TryPlanAction_OpeningTokenBeforeExistingClosingText_ReturnsFalse()
	{
		// Typing '(' in front of ')' must not duplicate the closing text.
		bool resolved = Resolve(text: ")", caretOffset: 0, input: "(", action: out _);
		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_OpeningTokenBeforeUnmatchedClosingText_ReturnsFalse()
	{
		// The closing parenthesis later on the caret's line has no opening token after the caret, so
		// inserting a closing text would duplicate it.
		bool resolved = Resolve(text: "ab)c", caretOffset: 2, input: "(", action: out _);
		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_OpeningTokenAfterMatchedPairOnTheLine_ReturnsInsertAction()
	{
		// The depth scan must not treat a balanced pair after the caret as a duplicate closing text:
		// the '(' raises the depth and its ')' lowers it back to zero.
		bool resolved = Resolve(text: "f() g()", caretOffset: 3, input: "(", out AutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void TryPlanAction_OpeningTokenAfterMatchedPairWithLaterUnmatchedClosingText_ReturnsFalse()
	{
		// The scan walks a balanced pair first and then finds a closing text whose depth is zero, so the
		// action must be declined even though the character after the caret would pass the gate.
		bool resolved = Resolve(text: "x (a) )", caretOffset: 1, input: "(", action: out _);
		Assert.IsFalse(resolved);
	}

	[TestMethod]
	[DataRow("ab\ncd)", DisplayName = "Lf")]
	[DataRow("ab\r\ncd)", DisplayName = "CrLf")]
	public void TryPlanAction_UnmatchedClosingTextOnTheNextLine_ReturnsInsertAction(string text)
	{
		// The unmatched-closing scan stops at the caret's line end, so a closing text on the next line
		// does not suppress the insert.
		bool resolved = Resolve(text, caretOffset: 2, input: "(", out AutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryPlanAction_UnmatchedClosingTextOnTheSameLineBeforeCrLf_ReturnsFalse()
	{
		// The scan covers the caret's line up to the terminator, which starts at the CR of a CRLF pair.
		bool resolved = Resolve(text: "ab)\r\ncd", caretOffset: 2, input: "(", action: out _);
		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryPlanAction_CaretBetweenCrLfPair_ScansThePrecedingLineOnly()
	{
		// A position on the LF of a CRLF pair belongs to the preceding line, whose end is the CR: the
		// closing text on the next line must not suppress the insert.
		bool resolved = Resolve(text: "ab\r\ncd)", caretOffset: 3, input: "(", out AutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryPlanAction_EmptyAutoCloseBeforeSet_AdmitsOnlyWhitespaceAndTheEndOfText()
	{
		AutoClosingOptions options = AutoClosingOptions.Default with { AutoCloseBefore = string.Empty };

		// A configured set replaces the kind's preset, so an empty set admits no character.
		Assert.IsFalse(Resolve(text: "(a", caretOffset: 1, input: "(", action: out _, options));

		// Whitespace and the end of the text are always admitted.
		bool resolvedAfterWhitespace = Resolve(text: "( a", caretOffset: 1, input: "(", action: out AutoClosingAction afterWhitespace, options);

		Assert.IsTrue(resolvedAfterWhitespace);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, afterWhitespace.Kind);

		bool resolvedAtEnd = Resolve(text: "(", caretOffset: 1, input: "(", action: out AutoClosingAction atEnd, options);

		Assert.IsTrue(resolvedAtEnd);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, atEnd.Kind);
	}
}
