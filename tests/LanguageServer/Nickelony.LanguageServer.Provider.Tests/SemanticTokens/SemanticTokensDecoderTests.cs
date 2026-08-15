namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class SemanticTokensDecoderTests
{
	private static readonly IReadOnlyList<string> s_tokenTypes = ["variable"];

	[TestMethod]
	public void Decode_EmptyData_ReturnsEmpty()
	{
		IReadOnlyList<SemanticToken> tokens = SemanticTokensDecoder.Decode(
			[], "abc def\nghij", s_tokenTypes, null);

		Assert.AreEqual(0, tokens.Count);
	}

	[TestMethod]
	public void Decode_MissingTokenTypes_ReturnsEmpty()
	{
		int[] data = [0, 0, 3, 0, 0];

		Assert.AreEqual(0, SemanticTokensDecoder.Decode(data, "abc def", [], null).Count);
	}

	[TestMethod]
	public void Decode_MultiTokenDeltas_ResolveAbsolutePositions()
	{
		// Line 0 is "abc def" (7 characters) and line 1 is "ghij" (4 characters).
		int[] data =
		[
			0, 0, 3, 0, 0, // line 0, character 0..3
			0, 4, 3, 0, 0, // same line, character 4..7
			1, 0, 4, 0, 0  // next line, character 0..4
		];

		IReadOnlyList<SemanticToken> tokens = SemanticTokensDecoder.Decode(
			data, "abc def\nghij", s_tokenTypes, null);

		Assert.AreEqual(3, tokens.Count);

		Assert.AreEqual(0, tokens[0].Line);
		Assert.AreEqual(0, tokens[0].Character);
		Assert.AreEqual(3, tokens[0].Length);
		Assert.AreEqual("variable", tokens[0].Type);

		Assert.AreEqual(0, tokens[1].Line);
		Assert.AreEqual(4, tokens[1].Character);
		Assert.AreEqual(3, tokens[1].Length);

		Assert.AreEqual(1, tokens[2].Line);
		Assert.AreEqual(0, tokens[2].Character);
		Assert.AreEqual(4, tokens[2].Length);
	}

	[TestMethod]
	public void Decode_NegativeCharacter_ClampsToLineStart()
	{
		int[] data = [0, -3, 2, 0, 0];

		IReadOnlyList<SemanticToken> tokens = SemanticTokensDecoder.Decode(
			data, "abc def", s_tokenTypes, null);

		Assert.AreEqual(1, tokens.Count);
		Assert.AreEqual(0, tokens[0].Character);
		Assert.AreEqual(2, tokens[0].Length);
	}

	[TestMethod]
	public void Decode_CharacterBeyondLineEnd_SkipsTokenWhileDeltasAdvance()
	{
		int[] data =
		[
			0, 50, 3, 0, 0, // character clamps to the line end, leaving no length
			1, 1, 2, 0, 0   // the next tuple still resolves from the advanced position
		];

		IReadOnlyList<SemanticToken> tokens = SemanticTokensDecoder.Decode(
			data, "abc def\nghij", s_tokenTypes, null);

		Assert.AreEqual(1, tokens.Count);
		Assert.AreEqual(1, tokens[0].Line);
		Assert.AreEqual(1, tokens[0].Character);
		Assert.AreEqual(2, tokens[0].Length);
	}

	[TestMethod]
	public void Decode_LengthBeyondLineEnd_ClipsToRemainingLine()
	{
		int[] data = [0, 5, 10, 0, 0];

		IReadOnlyList<SemanticToken> tokens = SemanticTokensDecoder.Decode(
			data, "abc def", s_tokenTypes, null);

		Assert.AreEqual(1, tokens.Count);
		Assert.AreEqual(5, tokens[0].Character);
		Assert.AreEqual(2, tokens[0].Length);
	}

	[TestMethod]
	public void Decode_ZeroLength_IsSkipped()
	{
		int[] data = [0, 0, 0, 0, 0];

		IReadOnlyList<SemanticToken> tokens = SemanticTokensDecoder.Decode(
			data, "abc def", s_tokenTypes, null);

		Assert.AreEqual(0, tokens.Count);
	}

	[TestMethod]
	public void Decode_NegativeLine_SkipsTokenWhileDeltasAdvance()
	{
		int[] data =
		[
			0, 0, 3, 0, 0,  // valid
			-1, 2, 3, 0, 0, // negative line is skipped
			2, 1, 1, 0, 0   // resolves on line 1 because the delta still advances
		];

		IReadOnlyList<SemanticToken> tokens = SemanticTokensDecoder.Decode(
			data, "abc def\nghij", s_tokenTypes, null);

		Assert.AreEqual(2, tokens.Count);
		Assert.AreEqual(0, tokens[0].Line);
		Assert.AreEqual(1, tokens[1].Line);
		Assert.AreEqual(1, tokens[1].Character);
		Assert.AreEqual(1, tokens[1].Length);
	}

	[TestMethod]
	public void Decode_DeltasThatWouldWrapInt32_AreSkippedInsteadOfLandingOnAWrongLine()
	{
		// Two int.MaxValue line deltas plus a delta of 2 sum to exactly 2^32: 32-bit accumulation would wrap the
		// cursor back to line 0 and emit a token on a wrong, in-range line; 64-bit accumulation keeps the cursor at
		// its true line and skips every tuple.
		int[] data =
		[
			int.MaxValue, 0, 1, 0, 0,
			int.MaxValue, 0, 1, 0, 0,
			2, 0, 1, 0, 0
		];

		IReadOnlyList<SemanticToken> tokens = SemanticTokensDecoder.Decode(
			data, "abc def\nghij", s_tokenTypes, null);

		Assert.AreEqual(0, tokens.Count);
	}

	[TestMethod]
	public void Decode_ResolvesTokensFromContent()
	{
		int[] data = [0, 0, 3, 0, 0];

		IReadOnlyList<SemanticToken> tokens = SemanticTokensDecoder.Decode(data, "abc def", s_tokenTypes, null);

		Assert.AreEqual(1, tokens.Count);
		Assert.AreEqual(0, tokens[0].Line);
		Assert.AreEqual(0, tokens[0].Character);
		Assert.AreEqual(3, tokens[0].Length);
		Assert.AreEqual("variable", tokens[0].Type);
	}

	[TestMethod]
	public void Decode_NullData_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => SemanticTokensDecoder.Decode(null!, "abc", s_tokenTypes, null));
	}

	[TestMethod]
	public void Decode_NullContent_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => SemanticTokensDecoder.Decode([0, 0, 3, 0, 0], (string)null!, s_tokenTypes, null));
	}

	[TestMethod]
	public void Decode_TokenTypeIndexOutOfRange_SkipsTokenWhileDeltasAdvance()
	{
		int[] data =
		[
			0, 0, 3, 0, 0, // valid
			1, 0, 2, 5, 0, // token type index 5 does not exist
			0, 2, 1, 0, 0  // resolves on line 1, character 2
		];

		IReadOnlyList<SemanticToken> tokens = SemanticTokensDecoder.Decode(
			data, "abc def\nghij", s_tokenTypes, null);

		Assert.AreEqual(2, tokens.Count);
		Assert.AreEqual(1, tokens[1].Line);
		Assert.AreEqual(2, tokens[1].Character);
	}

	[TestMethod]
	public void Decode_MultipleModifierBits_ExpandsInAdvertisedOrder()
	{
		IReadOnlyList<string> tokenModifiers = ["declaration", "static", "readonly"];

		// Mask 5 sets bit 0 ("declaration") and bit 2 ("readonly").
		int[] data = [0, 0, 3, 0, 5];

		IReadOnlyList<SemanticToken> tokens = SemanticTokensDecoder.Decode(
			data, "abc def", s_tokenTypes, tokenModifiers);

		Assert.AreEqual(1, tokens.Count);
		Assert.AreEqual(2, tokens[0].Modifiers.Count);
		Assert.IsTrue(tokens[0].HasModifier("declaration"));
		Assert.IsTrue(tokens[0].HasModifier("readonly"));
		Assert.IsFalse(tokens[0].HasModifier("static"));
	}

	[TestMethod]
	public void Decode_TokensWithTheSameModifierMask_ShareOneFrozenModifierList()
	{
		IReadOnlyList<string> tokenModifiers = ["declaration", "readonly"];

		// Both tokens carry mask 1 ("declaration").
		int[] data =
		[
			0, 0, 3, 0, 1, // line 0, character 0..3
			0, 4, 3, 0, 1  // same line, character 4..7, same mask
		];

		IReadOnlyList<SemanticToken> tokens = SemanticTokensDecoder.Decode(
			data, "abc def", s_tokenTypes, tokenModifiers);

		Assert.AreEqual(2, tokens.Count);

		// The per-mask cache must hand every token the same frozen list instead of copying it per token.
		Assert.AreSame(tokens[0].Modifiers, tokens[1].Modifiers);
		CollectionAssert.AreEqual(new[] { "declaration" }, tokens[0].Modifiers.ToArray());
	}

	[TestMethod]
	public void Decode_ModifierMaskWithPartiallyAdvertisedBits_MapsKnownBitsOnly()
	{
		int[] data = [0, 0, 3, 0, 5];

		IReadOnlyList<SemanticToken> withNullModifiers = SemanticTokensDecoder.Decode(
			data, "abc def", s_tokenTypes, null);

		Assert.AreEqual(0, withNullModifiers[0].Modifiers.Count);

		IReadOnlyList<SemanticToken> withPartialBits = SemanticTokensDecoder.Decode(
			data, "abc def", s_tokenTypes, ["declaration"]);

		// Bit 0 maps to "declaration"; bit 2 is not advertised and is ignored.
		Assert.AreEqual(1, withPartialBits[0].Modifiers.Count);
		Assert.IsTrue(withPartialBits[0].HasModifier("declaration"));
	}

	[TestMethod]
	public void Decode_LegendWithMoreThan32Modifiers_DoesNotAliasHigherIndicesOntoLowBits()
	{
		// A 32-bit modifier mask cannot express modifiers beyond index 31; the decoder must not let the wrapped
		// shift counts of higher indices alias onto the low bits.
		IReadOnlyList<string> tokenModifiers = [.. Enumerable.Range(0, 33).Select(index => $"modifier{index}")];

		// Mask 1 sets bit 0 only.
		int[] data = [0, 0, 3, 0, 1];

		IReadOnlyList<SemanticToken> tokens = SemanticTokensDecoder.Decode(data, "abc def", s_tokenTypes, tokenModifiers);

		Assert.AreEqual(1, tokens.Count);
		Assert.AreEqual(1, tokens[0].Modifiers.Count);
		Assert.IsTrue(tokens[0].HasModifier("modifier0"));
	}

	[TestMethod]
	public void Decode_TrailingPartialTuple_IsIgnored()
	{
		int[] data =
		[
			0, 0, 3, 0, 0,
			1, 0, 4, 0, 0,
			9, 9
		];

		IReadOnlyList<SemanticToken> tokens = SemanticTokensDecoder.Decode(
			data, "abc def\nghij", s_tokenTypes, null);

		Assert.AreEqual(2, tokens.Count);
	}
}
