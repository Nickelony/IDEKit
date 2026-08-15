namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class IdentifierCharacterPolicyTests
{
	[TestMethod]
	public void Default_AcceptsLettersDigitsAndUnderscore()
	{
		Assert.IsTrue(IdentifierCharacterPolicy.Default.IsPartCharacter('a'));
		Assert.IsTrue(IdentifierCharacterPolicy.Default.IsPartCharacter('Z'));
		Assert.IsTrue(IdentifierCharacterPolicy.Default.IsPartCharacter('4'));
		Assert.IsTrue(IdentifierCharacterPolicy.Default.IsPartCharacter('_'));
	}

	[TestMethod]
	public void Default_RejectsPunctuationWhitespaceAndQuotes()
	{
		Assert.IsFalse(IdentifierCharacterPolicy.Default.IsPartCharacter('.'));
		Assert.IsFalse(IdentifierCharacterPolicy.Default.IsPartCharacter(':'));
		Assert.IsFalse(IdentifierCharacterPolicy.Default.IsPartCharacter(' '));
		Assert.IsFalse(IdentifierCharacterPolicy.Default.IsPartCharacter('"'));
		Assert.IsFalse(IdentifierCharacterPolicy.Default.IsPartCharacter('\''));
	}

	[TestMethod]
	public void Default_AcceptsLetterAndRejectsPunctuationAsStart()
	{
		Assert.IsTrue(IdentifierCharacterPolicy.Default.IsStartCharacter('x'));
		Assert.IsFalse(IdentifierCharacterPolicy.Default.IsStartCharacter('.'));
	}

	[TestMethod]
	public void Default_AcceptsDigitAndRejectsSymbolAsStart()
	{
		// The default start rule is C-like and accepts digits; '$' is outside the default set.
		Assert.IsTrue(IdentifierCharacterPolicy.Default.IsStartCharacter('4'));
		Assert.IsFalse(IdentifierCharacterPolicy.Default.IsStartCharacter('$'));
	}

	[TestMethod]
	public void Create_WithCustomPredicate_AppliesPredicate()
	{
		IdentifierCharacterPolicy policy = IdentifierCharacterPolicy.Create(static c => c is 'a' or 'b');

		Assert.IsTrue(policy.IsPartCharacter('a'));
		Assert.IsTrue(policy.IsPartCharacter('b'));
		Assert.IsFalse(policy.IsPartCharacter('c'));
	}

	[TestMethod]
	public void Create_IsStartCharacterDefaultsToPartCharacter()
	{
		IdentifierCharacterPolicy policy = IdentifierCharacterPolicy.Create(static c => c == 'a');

		Assert.IsTrue(policy.IsStartCharacter('a'));
		Assert.IsFalse(policy.IsStartCharacter('b'));
	}

	[TestMethod]
	public void Create_WithSeparateStartPredicate_UsesIt()
	{
		IdentifierCharacterPolicy policy = IdentifierCharacterPolicy.Create(
			static c => char.IsLetterOrDigit(c),
			static c => char.IsLetter(c));

		Assert.IsTrue(policy.IsPartCharacter('4'));
		Assert.IsTrue(policy.IsStartCharacter('x'));
		Assert.IsFalse(policy.IsStartCharacter('4'));
	}
}
