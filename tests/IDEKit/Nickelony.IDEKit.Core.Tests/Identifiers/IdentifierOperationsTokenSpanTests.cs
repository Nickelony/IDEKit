namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class IdentifierOperationsTokenSpanTests
{
	// Delimiter policy: delimiters and line terminators end a token.
	private static readonly IdentifierCharacterPolicy s_delimiterPolicy = IdentifierCharacterPolicy.Create(
		static c => c is not (',' or '=' or ';' or '+' or '-' or '*' or '/' or '(' or ')' or '\r' or '\n'));

	// Structural-delimiter policy: whitespace, structural delimiters, and ':' end a token,
	// while double quotes remain part of the token.
	private static readonly IdentifierCharacterPolicy s_structuralDelimiterPolicy = IdentifierCharacterPolicy.Create(
		static c => c is not (' ' or ',' or '{' or '}' or '[' or ']' or ':' or '\t' or '\n' or '\r'));

	// A narrower start rule: digits and underscores may continue a token but not start one.
	private static readonly IdentifierCharacterPolicy s_continuationPolicy = IdentifierCharacterPolicy.Create(
		static c => char.IsLetterOrDigit(c) || c == '_',
		static c => char.IsLetter(c) || c == '_');

	[TestMethod]
	public void FindTokenSpan_DelimiterPolicy_KeyAtLineStart()
	{
		var snapshot = new StringTextSnapshot("Legend= 42");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 0, s_delimiterPolicy);

		Assert.AreEqual(new TextRange(0, 6), range);
	}

	[TestMethod]
	public void FindTokenSpan_DelimiterPolicy_FirstArgument_IncludesLeadingWhitespace()
	{
		var snapshot = new StringTextSnapshot("Legend= 42");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 8, s_delimiterPolicy);

		Assert.IsNotNull(range);
		Assert.AreEqual(new TextRange(7, 3), range);
		Assert.AreEqual(" 42", snapshot.GetText(range.Value.Offset, range.Value.Length));
	}

	[TestMethod]
	public void FindTokenSpan_DelimiterPolicy_SecondArgument_IncludesLeadingWhitespace()
	{
		var snapshot = new StringTextSnapshot("Customize= CUST_BAR, 5");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 22, s_delimiterPolicy);

		Assert.AreEqual(new TextRange(20, 2), range);
	}

	[TestMethod]
	public void FindTokenSpan_DelimiterPolicy_ConstantValue()
	{
		var snapshot = new StringTextSnapshot("Customize= CUST_BAR, 5");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 12, s_delimiterPolicy);

		Assert.AreEqual(new TextRange(10, 9), range);
	}

	[TestMethod]
	public void FindTokenSpan_DelimiterPolicy_HexValueIncludesDollarSign()
	{
		var snapshot = new StringTextSnapshot("Legend= $1A2F");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 8, s_delimiterPolicy);

		Assert.IsNotNull(range);
		Assert.AreEqual(new TextRange(7, 6), range);
		Assert.AreEqual("$1A2F", snapshot.GetText(range.Value.Offset, range.Value.Length).Trim());
	}

	[TestMethod]
	public void FindTokenSpan_DelimiterPolicy_SemicolonIsForwardDelimiter()
	{
		var snapshot = new StringTextSnapshot("Legend= \"hello;world\"");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 9, s_delimiterPolicy);

		Assert.IsNotNull(range);
		Assert.AreEqual(new TextRange(7, 7), range);
		Assert.AreEqual("\"hello", snapshot.GetText(range.Value.Offset, range.Value.Length).Trim());
	}

	[TestMethod]
	public void FindTokenSpan_DelimiterPolicy_WhitespaceOnlySpansWhitespace()
	{
		var snapshot = new StringTextSnapshot("   ");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 1, s_delimiterPolicy);

		Assert.AreEqual(new TextRange(0, 3), range);
	}

	[TestMethod]
	public void FindTokenSpan_DelimiterPolicy_DoesNotCrossLineBoundary()
	{
		var snapshot = new StringTextSnapshot("Line1\nLegend= 42");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 7, s_delimiterPolicy);

		Assert.AreEqual(new TextRange(6, 6), range);
	}

	[TestMethod]
	public void FindTokenSpan_Default_InsideIdentifier()
	{
		var snapshot = new StringTextSnapshot("local targetValue = 1");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 9, IdentifierCharacterPolicy.Default);

		Assert.AreEqual(new TextRange(6, 11), range);
	}

	[TestMethod]
	public void FindTokenSpan_Default_StepsBackToWordBeforeBoundary()
	{
		var snapshot = new StringTextSnapshot("player.");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, snapshot.TextLength, IdentifierCharacterPolicy.Default);

		Assert.AreEqual(new TextRange(0, 6), range);
	}

	[TestMethod]
	public void FindTokenSpan_Default_MiddleBoundaryPrefersWordBeforeDot()
	{
		var snapshot = new StringTextSnapshot("player.a");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 6, IdentifierCharacterPolicy.Default);

		Assert.AreEqual(new TextRange(0, 6), range);
	}

	[TestMethod]
	public void FindTokenSpan_Default_CaretAtEndOfIdentifier()
	{
		var snapshot = new StringTextSnapshot("return targetValue");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, snapshot.TextLength, IdentifierCharacterPolicy.Default);

		Assert.AreEqual(new TextRange(7, 11), range);
	}

	[TestMethod]
	public void FindTokenSpan_Default_OffsetPastEndClampsToLastWord()
	{
		var snapshot = new StringTextSnapshot("return targetValue");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 999, IdentifierCharacterPolicy.Default);

		Assert.AreEqual(new TextRange(7, 11), range);
	}

	[TestMethod]
	public void FindTokenSpan_Default_KeyValueSectionName()
	{
		var snapshot = new StringTextSnapshot("LEVEL: Foo");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 2, IdentifierCharacterPolicy.Default);

		Assert.AreEqual(new TextRange(0, 5), range);
	}

	[TestMethod]
	public void FindTokenSpan_Default_KeyValueValueName()
	{
		var snapshot = new StringTextSnapshot("LEVEL: Foo");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 8, IdentifierCharacterPolicy.Default);

		Assert.AreEqual(new TextRange(7, 3), range);
	}

	[TestMethod]
	public void FindTokenSpan_Default_LeadingWhitespaceReturnsNull()
	{
		var snapshot = new StringTextSnapshot("   foo");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 0, IdentifierCharacterPolicy.Default);

		Assert.IsNull(range);
	}

	[TestMethod]
	public void FindTokenSpan_Default_EmptyDocumentReturnsNull()
	{
		var snapshot = new StringTextSnapshot(string.Empty);

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 0, IdentifierCharacterPolicy.Default);

		Assert.IsNull(range);
	}

	[TestMethod]
	public void FindTokenSpan_Default_SingleWord()
	{
		var snapshot = new StringTextSnapshot("abc");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 1, IdentifierCharacterPolicy.Default);

		Assert.AreEqual(new TextRange(0, 3), range);
	}

	[TestMethod]
	public void FindTokenSpan_StructuralDelimiterPolicy_WordBeingTypedEndsAtCaret()
	{
		var snapshot = new StringTextSnapshot("alpha beta");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 5, s_structuralDelimiterPolicy, IdentifierSpanMode.EndingAtOffset);

		Assert.AreEqual(new TextRange(0, 5), range);
	}

	[TestMethod]
	public void FindTokenSpan_StructuralDelimiterPolicy_WordAfterSpaceAtEnd()
	{
		var snapshot = new StringTextSnapshot("alpha beta");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, snapshot.TextLength, s_structuralDelimiterPolicy, IdentifierSpanMode.EndingAtOffset);

		Assert.AreEqual(new TextRange(6, 4), range);
	}

	[TestMethod]
	public void FindTokenSpan_StructuralDelimiterPolicy_AtDocumentStartReturnsNull()
	{
		var snapshot = new StringTextSnapshot("alpha beta");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 0, s_structuralDelimiterPolicy, IdentifierSpanMode.EndingAtOffset);

		Assert.IsNull(range);
	}

	[TestMethod]
	public void FindTokenSpan_StructuralDelimiterPolicy_FreshQuoteReturnsQuoteOnly()
	{
		var snapshot = new StringTextSnapshot("{\"");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, snapshot.TextLength, s_structuralDelimiterPolicy, IdentifierSpanMode.EndingAtOffset);

		Assert.AreEqual(new TextRange(1, 1), range);
	}

	[TestMethod]
	public void FindTokenSpan_StructuralDelimiterPolicy_QuoteIncludedInsideWord()
	{
		var snapshot = new StringTextSnapshot("{\"abc");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 4, s_structuralDelimiterPolicy, IdentifierSpanMode.EndingAtOffset);

		Assert.AreEqual(new TextRange(1, 3), range);
	}

	[TestMethod]
	public void FindTokenSpan_StructuralDelimiterPolicy_StringValueIncludingQuote()
	{
		var snapshot = new StringTextSnapshot("{\"key\": \"va");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, snapshot.TextLength, s_structuralDelimiterPolicy, IdentifierSpanMode.EndingAtOffset);

		Assert.AreEqual(new TextRange(8, 3), range);
	}

	[TestMethod]
	public void FindTokenSpan_ContinuationCharacterInsideToken_ResolvesContainingToken()
	{
		var snapshot = new StringTextSnapshot("ab1");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 2, s_continuationPolicy);

		Assert.AreEqual(new TextRange(0, 3), range);
	}

	[TestMethod]
	public void FindTokenSpan_OffsetAfterContinuationCharacter_ResolvesContainingToken()
	{
		var snapshot = new StringTextSnapshot(" ab1");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, snapshot.TextLength, s_continuationPolicy);

		Assert.AreEqual(new TextRange(1, 3), range);
	}

	[TestMethod]
	public void FindTokenSpan_LeadingContinuationCharacter_ResolvesContainingToken()
	{
		var snapshot = new StringTextSnapshot("1ab");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 0, s_continuationPolicy);

		Assert.AreEqual(new TextRange(0, 3), range);
	}

	[TestMethod]
	public void FindTokenSpan_ProbeAfterContinuationCharacter_PrefersPrecedingToken()
	{
		var snapshot = new StringTextSnapshot("ab1 2");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 3, s_continuationPolicy);

		Assert.AreEqual(new TextRange(0, 3), range);
	}

	[TestMethod]
	public void FindTokenSpan_ProbeOnSeparatorWithoutStartCharacter_ReturnsNull()
	{
		var snapshot = new StringTextSnapshot("1ab 2");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, 3, s_continuationPolicy);

		Assert.IsNull(range);
	}

	[TestMethod]
	public void FindTokenSpan_EndingAtOffset_ContinuationCharacterAtOffset_ResolvesFullToken()
	{
		// The caret-adjacent character cannot start a token, but it is a continuation character.
		var snapshot = new StringTextSnapshot("value = ab1");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, snapshot.TextLength, s_continuationPolicy, IdentifierSpanMode.EndingAtOffset);

		Assert.AreEqual(new TextRange(8, 3), range);
	}

	[TestMethod]
	public void FindTokenSpan_EndingAtOffset_AllContinuationCharacters_ResolvesFullToken()
	{
		var snapshot = new StringTextSnapshot("42");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, snapshot.TextLength, s_continuationPolicy, IdentifierSpanMode.EndingAtOffset);

		Assert.AreEqual(new TextRange(0, 2), range);
	}

	[TestMethod]
	public void FindTokenSpan_EndingAtOffset_OffsetAfterWhitespace_ReturnsNull()
	{
		// The word being typed must be adjacent to the caret.
		var snapshot = new StringTextSnapshot("ab1 ");

		TextRange? range = IdentifierOperations.FindTokenSpan(snapshot, snapshot.TextLength, s_continuationPolicy, IdentifierSpanMode.EndingAtOffset);

		Assert.IsNull(range);
	}
}
