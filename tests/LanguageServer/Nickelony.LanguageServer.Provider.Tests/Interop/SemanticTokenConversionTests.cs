using Nickelony.IDEKit.IntelliSense.SemanticTokens;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class SemanticTokenConversionTests
{
	private const string Document = "alpha beta\r\ngamma\r\ndelta";

	[TestMethod]
	public void ToTextSemanticTokens_MapsProtocolPositionsToOffsets()
	{
		TextLineMap lineMap = TextLineMap.Build(Document);
		SemanticToken[] tokens =
		[
			new(0, 0, 5, "variable", []),
			new(1, 2, 3, "function", []),
			new(2, 0, 5, "class", [])
		];

		IReadOnlyList<TextSemanticToken> converted = SemanticTokenConversion.ToTextSemanticTokens(tokens, lineMap);

		Assert.AreEqual(3, converted.Count);
		Assert.AreEqual(new TextRange(0, 5), converted[0].Range);
		Assert.AreEqual(new TextRange(14, 3), converted[1].Range);
		Assert.AreEqual(new TextRange(19, 5), converted[2].Range);
	}

	[TestMethod]
	public void ToTextSemanticTokens_PreservesTypeAndModifiers()
	{
		TextLineMap lineMap = TextLineMap.Build(Document);
		SemanticToken[] tokens = [new(0, 6, 4, "parameter", ["declaration", "readonly"])];

		IReadOnlyList<TextSemanticToken> converted = SemanticTokenConversion.ToTextSemanticTokens(tokens, lineMap);

		Assert.AreEqual(1, converted.Count);
		Assert.AreEqual("parameter", converted[0].Type);
		CollectionAssert.AreEqual(new[] { "declaration", "readonly" }, converted[0].Modifiers.ToArray());
	}

	[TestMethod]
	public void ToTextSemanticTokens_TokenLengthBeyondTheLineEnd_ClampsLengthToTheRemainingLine()
	{
		TextLineMap lineMap = TextLineMap.Build(Document);
		SemanticToken[] tokens = [new(1, 3, 500, "variable", [])];

		IReadOnlyList<TextSemanticToken> converted = SemanticTokenConversion.ToTextSemanticTokens(tokens, lineMap);

		// "gamma" starts at offset 12; character 3 is offset 15 and the remaining line text is "ma".
		Assert.AreEqual(1, converted.Count);
		Assert.AreEqual(new TextRange(15, 2), converted[0].Range);
	}

	[TestMethod]
	public void ToTextSemanticTokens_CharacterAtLineEnd_ProducesEmptyRangeAndIsSkipped()
	{
		TextLineMap lineMap = TextLineMap.Build(Document);
		SemanticToken[] tokens = [new(1, 500, 2, "variable", [])];

		IReadOnlyList<TextSemanticToken> converted = SemanticTokenConversion.ToTextSemanticTokens(tokens, lineMap);

		Assert.AreEqual(0, converted.Count);
	}

	[TestMethod]
	public void ToTextSemanticTokens_LineOutsideDocument_IsSkipped()
	{
		TextLineMap lineMap = TextLineMap.Build(Document);
		SemanticToken[] tokens =
		[
			new(0, 0, 5, "variable", []),
			new(7, 0, 2, "stale", []),
			new(2, 0, 5, "class", [])
		];

		IReadOnlyList<TextSemanticToken> converted = SemanticTokenConversion.ToTextSemanticTokens(tokens, lineMap);

		Assert.AreEqual(2, converted.Count);
		Assert.AreEqual("variable", converted[0].Type);
		Assert.AreEqual("class", converted[1].Type);
	}

	[TestMethod]
	public void ToTextSemanticTokens_EmptyInput_ReturnsEmptyList()
	{
		TextLineMap lineMap = TextLineMap.Build(Document);

		IReadOnlyList<TextSemanticToken> converted = SemanticTokenConversion.ToTextSemanticTokens([], lineMap);

		Assert.AreEqual(0, converted.Count);
	}

	[TestMethod]
	public void ToTextSemanticTokens_NullArguments_Throw()
	{
		TextLineMap lineMap = TextLineMap.Build(Document);

		Assert.ThrowsExactly<ArgumentNullException>(() => SemanticTokenConversion.ToTextSemanticTokens(null!, lineMap));
		Assert.ThrowsExactly<ArgumentNullException>(() => SemanticTokenConversion.ToTextSemanticTokens([], null!));
	}
}
