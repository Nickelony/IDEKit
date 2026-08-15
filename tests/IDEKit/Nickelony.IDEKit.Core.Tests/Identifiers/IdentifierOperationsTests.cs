namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class IdentifierOperationsTests
{
	[TestMethod]
	public void GetWordEndingAt_ReturnsWordEndingAtOffset()
	{
		const string text = "if CONST_";

		string prefix = IdentifierOperations.GetWordEndingAt(text, text.Length);

		Assert.AreEqual("CONST_", prefix);
	}

	[TestMethod]
	public void GetWordEndingAt_DefaultStopsAtDollarSign()
	{
		const string text = "let $el";

		string prefix = IdentifierOperations.GetWordEndingAt(text, text.Length);

		Assert.AreEqual("el", prefix);
	}

	[TestMethod]
	public void GetWordEndingAt_CustomPolicyIncludesDollarSign()
	{
		const string text = "let $el";
		var policy = IdentifierCharacterPolicy.Create(static c => char.IsLetterOrDigit(c) || c is '_' or '$');

		string prefix = IdentifierOperations.GetWordEndingAt(text, text.Length, policy);

		Assert.AreEqual("$el", prefix);
	}

	[TestMethod]
	public void GetWordEndingAt_CustomPolicyIncludesPrime()
	{
		const string text = "map'";
		var policy = IdentifierCharacterPolicy.Create(static c => char.IsLetterOrDigit(c) || c is '_' or '\'');

		string prefix = IdentifierOperations.GetWordEndingAt(text, text.Length, policy);

		Assert.AreEqual("map'", prefix);
	}

	[TestMethod]
	public void GetWordEndingAt_PastEnd_ClampsToLastWord()
	{
		Assert.AreEqual("Beta", IdentifierOperations.GetWordEndingAt("Beta", 999));
	}

	[TestMethod]
	public void GetWordEndingAt_AtStart_ReturnsEmpty()
	{
		Assert.AreEqual(string.Empty, IdentifierOperations.GetWordEndingAt("Beta", 0));
	}

	[TestMethod]
	public void GetWordEndingAt_NegativeOffset_ReturnsEmpty()
	{
		Assert.AreEqual(string.Empty, IdentifierOperations.GetWordEndingAt("Beta", -1));
	}

	[TestMethod]
	public void GetWordEndingAt_NonAsciiLetters_ArePartOfTheWord()
	{
		Assert.AreEqual("caf\u00E9", IdentifierOperations.GetWordEndingAt("caf\u00E9", 4));
	}

	[TestMethod]
	public void GetWordEndingAt_AstralCharacter_SplitsAtTheSurrogatePair()
	{
		// char.IsLetterOrDigit works on UTF-16 code units, so the default rule ends the run at the
		// surrogate pair; use a custom predicate for code-point-aware rules.
		Assert.AreEqual("b", IdentifierOperations.GetWordEndingAt("a\U0001F600b", 4));
	}

	[TestMethod]
	public void GetWordEndingAt_EmptyValue_ReturnsEmpty()
	{
		Assert.AreEqual(string.Empty, IdentifierOperations.GetWordEndingAt(string.Empty, 0));
	}

	[TestMethod]
	public void GetWordEndingAt_AgreesWithEndingAtOffsetSpanForEveryInRangeOffset()
	{
		// Both primitives share one boundary walk, so the string prefix must match the span the
		// snapshot API reports for the word being typed.
		const string text = "local target.value = CONST_ + el";
		var snapshot = new StringTextSnapshot(text);

		for (int offset = 0; offset <= text.Length; offset++)
		{
			string prefix = IdentifierOperations.GetWordEndingAt(text, offset);
			TextRange? span = IdentifierOperations.FindTokenSpan(
				snapshot, offset, IdentifierCharacterPolicy.Default, IdentifierSpanMode.EndingAtOffset);

			if (span is null)
			{
				Assert.AreEqual(string.Empty, prefix, $"Offset {offset}.");
				continue;
			}

			Assert.AreEqual(offset, span.Value.EndOffset, $"Offset {offset}.");
			Assert.AreEqual(text.Substring(span.Value.Offset, span.Value.Length), prefix, $"Offset {offset}.");
		}
	}
}
