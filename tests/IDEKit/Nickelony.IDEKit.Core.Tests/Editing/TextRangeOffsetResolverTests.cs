namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextRangeOffsetResolverTests
{
	private static readonly Func<char, bool> s_identifierRule = IdentifierCharacterPolicy.Default.IsPartCharacter;

	private static readonly Func<char, bool> s_extendedRule = static character => char.IsLetterOrDigit(character) || character is '_' or '.' or ':' or '\'' or '"';

	[TestMethod]
	public void TryResolveOffsets_ClampsStaleLineIndicesToDocumentBounds()
	{
		TextLineMap lineMap = TextLineMap.Build("\nvalue");

		bool resolved = TextRangeOffsetResolver.TryResolveOffsets(
			lineMap,
			new TextPositionRange(new TextPosition(99, 99), new TextPosition(99, 99)),
			s_identifierRule, out TextRange range);

		Assert.IsTrue(resolved);
		Assert.AreEqual(1, range.Offset);
		Assert.AreEqual(6, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_NonEmptyRange_ReturnsOffsetsUnchanged()
	{
		TextLineMap lineMap = TextLineMap.Build("hello\nworld");

		bool resolved = TextRangeOffsetResolver.TryResolveOffsets(
			lineMap,
			new TextPositionRange(new TextPosition(0, 1), new TextPosition(0, 3)),
			s_identifierRule, out TextRange range);

		Assert.IsTrue(resolved);
		Assert.AreEqual(1, range.Offset);
		Assert.AreEqual(3, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_ReversedRange_PrefersWordAtStartPosition()
	{
		TextLineMap lineMap = TextLineMap.Build("foo bar");

		bool resolved = TextRangeOffsetResolver.TryResolveOffsets(
			lineMap,
			new TextPositionRange(new TextPosition(0, 4), new TextPosition(0, 2)),
			s_identifierRule, out TextRange range);

		Assert.IsTrue(resolved);
		Assert.AreEqual(4, range.Offset);
		Assert.AreEqual(7, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_NegativeCharacter_ClampsToTheLineStart()
	{
		TextLineMap lineMap = TextLineMap.Build("foo bar");

		bool resolved = TextRangeOffsetResolver.TryResolveOffsets(
			lineMap,
			new TextPositionRange(new TextPosition(0, -3), new TextPosition(0, -1)),
			s_identifierRule, out TextRange range);

		Assert.IsTrue(resolved);
		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(3, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_WhitespaceOnlyLine_FallsBackToSingleCharacter()
	{
		TextLineMap lineMap = TextLineMap.Build("  \nfoo");

		bool resolved = TextRangeOffsetResolver.TryResolveOffsets(
			lineMap,
			new TextPositionRange(new TextPosition(0, 2), new TextPosition(0, 0)),
			s_identifierRule, out TextRange range);

		Assert.IsTrue(resolved);
		Assert.AreEqual(1, range.Offset);
		Assert.AreEqual(2, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_WhitespaceAroundContent_TrimsToContent()
	{
		TextLineMap lineMap = TextLineMap.Build("  x  ");

		bool resolved = TextRangeOffsetResolver.TryResolveOffsets(
			lineMap,
			new TextPositionRange(new TextPosition(0, 4), new TextPosition(0, 1)),
			s_identifierRule, out TextRange range);

		Assert.IsTrue(resolved);
		Assert.AreEqual(2, range.Offset);
		Assert.AreEqual(3, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_IdentifierPolicyOverload_MatchesDelegateOverload()
	{
		TextLineMap lineMap = TextLineMap.Build("foo bar");
		var range = new TextPositionRange(new TextPosition(0, 4), new TextPosition(0, 4));

		Assert.IsTrue(TextRangeOffsetResolver.TryResolveOffsets(lineMap, range, s_identifierRule, out TextRange expected));
		Assert.IsTrue(TextRangeOffsetResolver.TryResolveOffsets(lineMap, range, IdentifierCharacterPolicy.Default, out TextRange resolved));
		Assert.AreEqual(expected, resolved);
	}

	[TestMethod]
	public void TryResolveOffsets_NullWordPolicy_Throws()
	{
		TextLineMap lineMap = TextLineMap.Build("foo");

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			TextRangeOffsetResolver.TryResolveOffsets(lineMap, default, (IdentifierCharacterPolicy)null!, out _));
	}

	[TestMethod]
	public void TryResolveOffsets_EmptyMiddleLine_AnchorsToNextNonEmptyLine()
	{
		TextLineMap lineMap = TextLineMap.Build("a\n\nb");

		bool resolved = TextRangeOffsetResolver.TryResolveOffsets(
			lineMap,
			new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 0)),
			s_identifierRule, out TextRange range);

		Assert.IsTrue(resolved);
		Assert.AreEqual(3, range.Offset);
		Assert.AreEqual(4, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_EmptyFinalLine_AnchorsToEndOfPreviousLine()
	{
		TextLineMap lineMap = TextLineMap.Build("abc\n");

		bool resolved = TextRangeOffsetResolver.TryResolveOffsets(
			lineMap,
			new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 0)),
			s_identifierRule, out TextRange range);

		Assert.IsTrue(resolved);
		Assert.AreEqual(2, range.Offset);
		Assert.AreEqual(3, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_AllLinesEmpty_ReturnsFalse()
	{
		TextLineMap lineMap = TextLineMap.Build("\n");

		bool resolved = TextRangeOffsetResolver.TryResolveOffsets(
			lineMap,
			new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 0)),
			s_identifierRule, out TextRange range);

		Assert.IsFalse(resolved);
		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(0, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_CustomWordPredicate_RestrictsWordCharacters()
	{
		TextLineMap lineMap = TextLineMap.Build("foo.bar baz");

		bool resolved = TextRangeOffsetResolver.TryResolveOffsets(
			lineMap,
			new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 0)),
			static character => char.IsLetter(character),
			out TextRange range);

		Assert.IsTrue(resolved);
		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(3, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_EmptyLineBeforeWhitespaceOnlyLine_AnchorsToNextContent()
	{
		// The whitespace-only line is skipped so the anchor lands on the content of the next line.
		TextLineMap lineMap = TextLineMap.Build("a\n\n   \nb");

		bool resolved = TextRangeOffsetResolver.TryResolveOffsets(
			lineMap,
			new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 0)),
			s_identifierRule, out TextRange range);

		Assert.IsTrue(resolved);
		Assert.AreEqual(7, range.Offset);
		Assert.AreEqual(8, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_EmptyLineAfterWhitespaceOnlyLine_AnchorsToLastContent()
	{
		TextLineMap lineMap = TextLineMap.Build("a\n   \n");

		bool resolved = TextRangeOffsetResolver.TryResolveOffsets(
			lineMap,
			new TextPositionRange(new TextPosition(2, 0), new TextPosition(2, 0)),
			s_identifierRule, out TextRange range);

		Assert.IsTrue(resolved);
		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(1, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_ExtendedRule_TreatsDotsAndQuotesAsWordCharacters()
	{
		// A caller that extends the identifier rule with '.', ':', '\'', and '"' resolves a dotted
		// access as one run, while IdentifierCharacterPolicy.Default stops at the dot. The resolver
		// applies the supplied rule, so the divergence is the caller's choice.
		TextLineMap lineMap = TextLineMap.Build("obj.field");
		var snapshot = new StringTextSnapshot("obj.field");

		bool resolved = TextRangeOffsetResolver.TryResolveOffsets(lineMap,
			new TextPositionRange(new TextPosition(0, 2), new TextPosition(0, 2)), s_extendedRule, out TextRange range);
		TextRange? defaultSpan = IdentifierOperations.FindTokenSpan(snapshot, 2, IdentifierCharacterPolicy.Default);

		Assert.IsTrue(resolved);
		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(9, range.EndOffset);
		Assert.IsNotNull(defaultSpan);
		Assert.AreEqual(0, defaultSpan.Value.Offset);
		Assert.AreEqual(3, defaultSpan.Value.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_IdentifierRule_AgreesWithIdentifierOperationsOnWordProbes()
	{
		// The resolver and IdentifierOperations share one boundary walk, so probes inside or right after
		// letters/digits/underscore tokens resolve the same run through both APIs.
		TextLineMap lineMap = TextLineMap.Build("player target name");
		var snapshot = new StringTextSnapshot("player target name");

		foreach (int probe in (int[])[0, 2, 5, 6, 8, 18])
		{
			bool resolved = TextRangeOffsetResolver.TryResolveOffsets(lineMap,
			new TextPositionRange(new TextPosition(0, probe), new TextPosition(0, probe)), s_identifierRule, out TextRange range);
			TextRange? span = IdentifierOperations.FindTokenSpan(snapshot, probe, IdentifierCharacterPolicy.Default);

			Assert.IsTrue(resolved);
			Assert.IsTrue(span.HasValue);
			Assert.AreEqual(span.Value.Offset, range.Offset);
			Assert.AreEqual(span.Value.EndOffset, range.EndOffset);
		}
	}

	[TestMethod]
	public void TryResolveOffsets_EmptyDocument_ReturnsFalse()
	{
		TextLineMap lineMap = TextLineMap.Build(string.Empty);

		bool resolved = TextRangeOffsetResolver.TryResolveOffsets(
			lineMap,
			new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 0)),
			s_identifierRule,
			out TextRange range);

		Assert.IsFalse(resolved);
		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(0, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_RepresentableRange_AgreesWithLineMapConversion()
	{
		// The resolver's representable path is TextLineMap.TryGetOffsets; its fallbacks run only when
		// that conversion fails or yields a zero-length range.
		TextLineMap lineMap = TextLineMap.Build("hello\nworld");
		var range = new TextPositionRange(new TextPosition(0, 1), new TextPosition(1, 3));

		Assert.IsTrue(lineMap.TryGetOffsets(range, out TextRange expected));
		Assert.IsTrue(TextRangeOffsetResolver.TryResolveOffsets(lineMap, range, s_identifierRule, out TextRange resolved));
		Assert.AreEqual(expected, resolved);
	}

	[TestMethod]
	public void TryResolveOffsets_EmptyRange_AnchorsTheNearestWord()
	{
		TextLineMap lineMap = TextLineMap.Build("foo bar");

		// On a word character the word itself is anchored.
		Assert.IsTrue(TextRangeOffsetResolver.TryResolveOffsets(lineMap,
			new TextPositionRange(new TextPosition(0, 4), new TextPosition(0, 4)), s_identifierRule, out TextRange atWord));
		Assert.AreEqual(new TextRange(4, 3), atWord);

		// On a non-word character the word that immediately precedes it wins.
		Assert.IsTrue(TextRangeOffsetResolver.TryResolveOffsets(lineMap,
			new TextPositionRange(new TextPosition(0, 3), new TextPosition(0, 3)), s_identifierRule, out TextRange afterWord));
		Assert.AreEqual(new TextRange(0, 3), afterWord);
	}

	[TestMethod]
	public void TryResolveOffsets_NonWordProbe_SearchesForwardWhenNothingPrecedes()
	{
		// The fallback also anchors leading whitespace: with no preceding word, the walk advances
		// to the next word character.
		TextLineMap lineMap = TextLineMap.Build(" bar");

		Assert.IsTrue(TextRangeOffsetResolver.TryResolveOffsets(lineMap,
			new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 0)), s_identifierRule, out TextRange resolved));
		Assert.AreEqual(new TextRange(1, 3), resolved);
	}

	[TestMethod]
	public void TryResolveOffsets_NonWordProbe_DivergesFromContainingTokenLookup()
	{
		// Deliberate probe-policy divergence: the resolver searches forward for the nearest word,
		// while FindTokenSpan(Containing) reports no token when the probe lies outside a token and
		// no valid preceding token exists.
		TextLineMap lineMap = TextLineMap.Build(" bar");
		var snapshot = new StringTextSnapshot(" bar");
		var emptyAtSpace = new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 0));

		Assert.IsTrue(TextRangeOffsetResolver.TryResolveOffsets(lineMap, emptyAtSpace, s_identifierRule, out TextRange resolved));
		Assert.AreEqual(1, resolved.Offset);
		Assert.IsNull(IdentifierOperations.FindTokenSpan(snapshot, 0, IdentifierCharacterPolicy.Default));
	}

	[TestMethod]
	public void TryResolveOffsets_UnresolvableRange_ReportsTheDefaultRange()
	{
		// A reversed range on a document without anchorable content has no fallback: the false return
		// reports the documented empty default range, never a range an earlier conversion mapped.
		TextLineMap lineMap = TextLineMap.Build("\n");

		bool resolved = TextRangeOffsetResolver.TryResolveOffsets(
			lineMap,
			new TextPositionRange(new TextPosition(1, 0), new TextPosition(0, 0)),
			s_identifierRule,
			out TextRange range);

		Assert.IsFalse(resolved);
		Assert.AreEqual(default(TextRange), range);
	}
}
