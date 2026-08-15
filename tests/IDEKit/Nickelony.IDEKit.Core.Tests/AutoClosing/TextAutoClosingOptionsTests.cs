namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Verifies the default configuration and validation of <see cref="TextAutoClosingOptions"/>.
/// </summary>
[TestClass]
public sealed class TextAutoClosingOptionsTests
{
	[TestMethod]
	public void DefaultOptions_ContainLanguageNeutralPairsInOrder()
	{
		TextAutoClosingOptions options = TextAutoClosingOptions.Default;

		Assert.AreSequenceEqual(
			[
				TextAutoClosingPair.Parentheses,
				TextAutoClosingPair.Braces,
				TextAutoClosingPair.Brackets,
				TextAutoClosingPair.DoubleQuotes,
				TextAutoClosingPair.SingleQuotes,
			],
			[.. options.Pairs]);
	}

	[TestMethod]
	public void DefaultOptions_Presets_SuppressAfterWordCharacterMatchesQuoteKindsOnly()
	{
		Assert.IsTrue(TextAutoClosingPair.DoubleQuotes.SuppressAfterWordCharacter);
		Assert.IsTrue(TextAutoClosingPair.SingleQuotes.SuppressAfterWordCharacter);
		Assert.IsFalse(TextAutoClosingPair.Parentheses.SuppressAfterWordCharacter);
		Assert.IsFalse(TextAutoClosingPair.Backticks.SuppressAfterWordCharacter);
	}

	[TestMethod]
	public void DefaultPresets_MatchTheDocumentedCharacterSets()
	{
		// The presets are public constants that hosts compare or persist, so their exact character
		// sets are part of the contract. The computed expectations keep the assertion off the
		// constant-to-constant analyzer path.
		string expectedBracketPreset = new string(['"', '\'', '`', ';', ':', '.', ',', '=', '}', ']', ')', '>']);
		string expectedQuotePreset = new string([';', ':', '.', ',', '=', '}', ']', ')', '>']);

		Assert.AreEqual(TextAutoClosingOptions.DefaultBracketAutoCloseBefore, expectedBracketPreset);
		Assert.AreEqual(TextAutoClosingOptions.DefaultQuoteAutoCloseBefore, expectedQuotePreset);
	}

	[TestMethod]
	public void DefaultOptions_EveryPairWrapsSelections()
	{
		foreach (TextAutoClosingPair pair in TextAutoClosingOptions.Default.Pairs)
			Assert.IsTrue(pair.WrapSelection, $"Expected '{pair.Open}' to wrap selections by default.");
	}

	[TestMethod]
	public void Options_NullPairs_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new TextAutoClosingOptions { Pairs = null! });
	}

	[TestMethod]
	public void Options_PairsWithNullEntry_ThrowsArgumentException()
	{
		var exception = Assert.ThrowsExactly<ArgumentException>(() =>
			_ = new TextAutoClosingOptions { Pairs = [TextAutoClosingPair.Parentheses, null!] });

		Assert.AreEqual("value", exception.ParamName);
	}

	[TestMethod]
	public void Options_Pairs_CopyTheAssignedList()
	{
		var pairs = new List<TextAutoClosingPair> { TextAutoClosingPair.Parentheses };
		var options = new TextAutoClosingOptions { Pairs = pairs };

		pairs.Clear();

		Assert.HasCount(1, options.Pairs);
	}
}
