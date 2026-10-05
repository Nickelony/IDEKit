namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class DiagnosticAnchorResolverTests
{
	private static readonly IdentifierCharacterPolicy s_identifierRule = IdentifierCharacterPolicy.Default;

	private static readonly IdentifierCharacterPolicy s_extendedRule = IdentifierCharacterPolicy.Create(static character =>
		char.IsLetterOrDigit(character) || character is '_' or '.' or ':' or '\'' or '"'
	);

	[TestMethod]
	public void TryResolveOffsets_ClampsStaleLineIndicesToDocumentBounds()
	{
		var lineMap = TextLineMap.Build("\nvalue");

		bool resolved = DiagnosticAnchorResolver.TryResolveOffsets(
			lineMap,
			range: new TextPositionRange(new TextPosition(99, 99), new TextPosition(99, 99)),
			wordCharacters: s_identifierRule,
			resolvedRange: out TextRange range
		);

		Assert.IsTrue(resolved);
		Assert.AreEqual(1, range.Offset);
		Assert.AreEqual(6, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_NonEmptyRange_ReturnsOffsetsUnchanged()
	{
		var lineMap = TextLineMap.Build("hello\nworld");

		bool resolved = DiagnosticAnchorResolver.TryResolveOffsets(
			lineMap,
			range: new TextPositionRange(new TextPosition(0, 1), new TextPosition(0, 3)),
			wordCharacters: s_identifierRule,
			resolvedRange: out TextRange range
		);

		Assert.IsTrue(resolved);
		Assert.AreEqual(1, range.Offset);
		Assert.AreEqual(3, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_ReversedRange_PrefersWordAtStartPosition()
	{
		var lineMap = TextLineMap.Build("foo bar");

		bool resolved = DiagnosticAnchorResolver.TryResolveOffsets(
			lineMap,
			range: new TextPositionRange(new TextPosition(0, 4), new TextPosition(0, 2)),
			wordCharacters: s_identifierRule,
			resolvedRange: out TextRange range
		);

		Assert.IsTrue(resolved);
		Assert.AreEqual(4, range.Offset);
		Assert.AreEqual(7, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_NegativeCharacter_ClampsToTheLineStart()
	{
		var lineMap = TextLineMap.Build("foo bar");

		bool resolved = DiagnosticAnchorResolver.TryResolveOffsets(
			lineMap,
			range: new TextPositionRange(new TextPosition(0, -3), new TextPosition(0, -1)),
			wordCharacters: s_identifierRule,
			resolvedRange: out TextRange range
		);

		Assert.IsTrue(resolved);
		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(3, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_WhitespaceOnlyLine_FallsBackToSingleCharacter()
	{
		var lineMap = TextLineMap.Build("  \nfoo");

		bool resolved = DiagnosticAnchorResolver.TryResolveOffsets(
			lineMap,
			range: new TextPositionRange(new TextPosition(0, 2), new TextPosition(0, 0)),
			wordCharacters: s_identifierRule,
			resolvedRange: out TextRange range
		);

		Assert.IsTrue(resolved);
		Assert.AreEqual(1, range.Offset);
		Assert.AreEqual(2, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_WhitespaceAroundContent_TrimsToContent()
	{
		var lineMap = TextLineMap.Build("  x  ");

		bool resolved = DiagnosticAnchorResolver.TryResolveOffsets(
			lineMap,
			range: new TextPositionRange(new TextPosition(0, 4), new TextPosition(0, 1)),
			wordCharacters: s_identifierRule,
			resolvedRange: out TextRange range
		);

		Assert.IsTrue(resolved);
		Assert.AreEqual(2, range.Offset);
		Assert.AreEqual(3, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_EmptyMiddleLine_AnchorsToNextNonEmptyLine()
	{
		var lineMap = TextLineMap.Build("a\n\nb");

		bool resolved = DiagnosticAnchorResolver.TryResolveOffsets(
			lineMap,
			range: new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 0)),
			wordCharacters: s_identifierRule,
			resolvedRange: out TextRange range
		);

		Assert.IsTrue(resolved);
		Assert.AreEqual(3, range.Offset);
		Assert.AreEqual(4, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_EmptyFinalLine_AnchorsToEndOfPreviousLine()
	{
		var lineMap = TextLineMap.Build("abc\n");

		bool resolved = DiagnosticAnchorResolver.TryResolveOffsets(
			lineMap,
			range: new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 0)),
			wordCharacters: s_identifierRule,
			resolvedRange: out TextRange range
		);

		Assert.IsTrue(resolved);
		Assert.AreEqual(2, range.Offset);
		Assert.AreEqual(3, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_AllLinesEmpty_ReturnsFalse()
	{
		var lineMap = TextLineMap.Build("\n");

		bool resolved = DiagnosticAnchorResolver.TryResolveOffsets(
			lineMap,
			range: new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 0)),
			wordCharacters: s_identifierRule,
			resolvedRange: out TextRange range
		);

		Assert.IsFalse(resolved);
		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(0, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_CustomWordPredicate_RestrictsWordCharacters()
	{
		var lineMap = TextLineMap.Build("foo.bar baz");

		bool resolved = DiagnosticAnchorResolver.TryResolveOffsets(
			lineMap,
			range: new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 0)),
			wordCharacters: IdentifierCharacterPolicy.Create(static character => char.IsLetter(character)),
			resolvedRange: out TextRange range
		);

		Assert.IsTrue(resolved);
		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(3, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_EmptyLineBeforeWhitespaceOnlyLine_AnchorsToNextContent()
	{
		// The whitespace-only line is skipped so the anchor lands on the content of the next line.
		var lineMap = TextLineMap.Build("a\n\n   \nb");

		bool resolved = DiagnosticAnchorResolver.TryResolveOffsets(
			lineMap,
			range: new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 0)),
			wordCharacters: s_identifierRule,
			resolvedRange: out TextRange range
		);

		Assert.IsTrue(resolved);
		Assert.AreEqual(7, range.Offset);
		Assert.AreEqual(8, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_EmptyLineAfterWhitespaceOnlyLine_AnchorsToLastContent()
	{
		var lineMap = TextLineMap.Build("a\n   \n");

		bool resolved = DiagnosticAnchorResolver.TryResolveOffsets(
			lineMap,
			range: new TextPositionRange(new TextPosition(2, 0), new TextPosition(2, 0)),
			wordCharacters: s_identifierRule,
			resolvedRange: out TextRange range
		);

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
		var lineMap = TextLineMap.Build("obj.field");
		var snapshot = new StringTextSnapshot("obj.field");

		bool resolved = DiagnosticAnchorResolver.TryResolveOffsets(
			lineMap,
			range: new TextPositionRange(new TextPosition(0, 2), new TextPosition(0, 2)),
			wordCharacters: s_extendedRule,
			resolvedRange: out TextRange range
		);

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
		var lineMap = TextLineMap.Build("player target name");
		var snapshot = new StringTextSnapshot("player target name");

		foreach (int probe in (int[])[0, 2, 5, 6, 8, 18])
		{
			bool resolved = DiagnosticAnchorResolver.TryResolveOffsets(
				lineMap,
				range: new TextPositionRange(new TextPosition(0, probe), new TextPosition(0, probe)),
				wordCharacters: s_identifierRule,
				resolvedRange: out TextRange range
			);

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
		var lineMap = TextLineMap.Build(string.Empty);

		bool resolved = DiagnosticAnchorResolver.TryResolveOffsets(
			lineMap,
			range: new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 0)),
			wordCharacters: s_identifierRule,
			resolvedRange: out TextRange range
		);

		Assert.IsFalse(resolved);
		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(0, range.EndOffset);
	}

	[TestMethod]
	public void TryResolveOffsets_RepresentableRange_AgreesWithLineMapConversion()
	{
		// The resolver's representable path is TextLineMap.TryGetOffsets; its fallbacks run only when
		// that conversion fails or yields a zero-length range.
		var lineMap = TextLineMap.Build("hello\nworld");
		var range = new TextPositionRange(new TextPosition(0, 1), new TextPosition(1, 3));

		Assert.IsTrue(lineMap.TryGetOffsets(range, out TextRange expected));

		Assert.IsTrue(
			DiagnosticAnchorResolver.TryResolveOffsets(lineMap, range, wordCharacters: s_identifierRule, resolvedRange: out TextRange resolved)
		);

		Assert.AreEqual(expected, resolved);
	}

	[TestMethod]
	public void TryResolveOffsets_EmptyRange_AnchorsTheNearestWord()
	{
		var lineMap = TextLineMap.Build("foo bar");

		// On a word character the word itself is anchored.
		Assert.IsTrue(
			DiagnosticAnchorResolver.TryResolveOffsets(
				lineMap,
				range: new TextPositionRange(new TextPosition(0, 4), new TextPosition(0, 4)),
				wordCharacters: s_identifierRule,
				resolvedRange: out TextRange atWord
			)
		);

		Assert.AreEqual(new TextRange(4, 3), atWord);

		// On a non-word character the word that immediately precedes it wins.
		Assert.IsTrue(
			DiagnosticAnchorResolver.TryResolveOffsets(
				lineMap,
				range: new TextPositionRange(new TextPosition(0, 3), new TextPosition(0, 3)),
				wordCharacters: s_identifierRule,
				resolvedRange: out TextRange afterWord
			)
		);

		Assert.AreEqual(new TextRange(0, 3), afterWord);
	}

	[TestMethod]
	public void TryResolveOffsets_NonWordProbe_SearchesForwardWhenNothingPrecedes()
	{
		// The fallback also anchors leading whitespace: with no preceding word, the walk advances
		// to the next word character.
		var lineMap = TextLineMap.Build(" bar");

		Assert.IsTrue(
			DiagnosticAnchorResolver.TryResolveOffsets(
				lineMap,
				range: new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 0)),
				wordCharacters: s_identifierRule,
				resolvedRange: out TextRange resolved
			)
		);

		Assert.AreEqual(new TextRange(1, 3), resolved);
	}

	[TestMethod]
	public void TryResolveOffsets_NonWordProbe_DivergesFromContainingTokenLookup()
	{
		// Deliberate probe-policy divergence: the resolver searches forward for the nearest word,
		// while FindTokenSpan(Containing) reports no token when the probe lies outside a token and
		// no valid preceding token exists.
		var lineMap = TextLineMap.Build(" bar");
		var snapshot = new StringTextSnapshot(" bar");
		var emptyAtSpace = new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 0));

		Assert.IsTrue(
			DiagnosticAnchorResolver.TryResolveOffsets(
				lineMap,
				range: emptyAtSpace,
				wordCharacters: s_identifierRule,
				resolvedRange: out TextRange resolved
			)
		);

		Assert.AreEqual(1, resolved.Offset);
		Assert.IsNull(IdentifierOperations.FindTokenSpan(snapshot, 0, IdentifierCharacterPolicy.Default));
	}

	[TestMethod]
	public void TryResolveOffsets_UnresolvableRange_ReportsTheDefaultRange()
	{
		// A reversed range on a document without anchorable content has no fallback: the false return
		// reports the documented empty default range, never a range an earlier conversion mapped.
		var lineMap = TextLineMap.Build("\n");

		bool resolved = DiagnosticAnchorResolver.TryResolveOffsets(
			lineMap,
			range: new TextPositionRange(new TextPosition(1, 0), new TextPosition(0, 0)),
			wordCharacters: s_identifierRule,
			resolvedRange: out TextRange range
		);

		Assert.IsFalse(resolved);
		Assert.AreEqual(default, range);
	}
}
