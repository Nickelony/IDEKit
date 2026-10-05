namespace Nickelony.IDEKit.Core.Tests;

/// <summary>Verifies the default configuration and validation of <see cref="AutoClosingOptions"/>.</summary>
[TestClass]
public sealed class AutoClosingOptionsTests
{
	[TestMethod]
	public void DefaultOptions_ContainLanguageNeutralPairsInOrder()
	{
		AutoClosingOptions options = AutoClosingOptions.Default;

		Assert.AreSequenceEqual(
			[
				AutoClosingPair.Parentheses,
				AutoClosingPair.Braces,
				AutoClosingPair.Brackets,
				AutoClosingPair.DoubleQuotes,
				AutoClosingPair.SingleQuotes,
			],

			[.. options.Pairs]
		);
	}

	[TestMethod]
	public void DefaultOptions_Presets_SuppressAfterWordCharacterMatchesQuoteKindsOnly()
	{
		Assert.IsTrue(AutoClosingPair.DoubleQuotes.SuppressAfterWordCharacter);
		Assert.IsTrue(AutoClosingPair.SingleQuotes.SuppressAfterWordCharacter);
		Assert.IsFalse(AutoClosingPair.Parentheses.SuppressAfterWordCharacter);
		Assert.IsFalse(AutoClosingPair.Backticks.SuppressAfterWordCharacter);
	}

	[TestMethod]
	public void DefaultPresets_MatchTheDocumentedCharacterSets()
	{
		// The presets are public constants that hosts compare or persist, so their exact character
		// sets are part of the contract. The computed expectations keep the assertion off the
		// constant-to-constant analyzer path.
		string expectedBracketPreset = new(['"', '\'', '`', ';', ':', '.', ',', '=', '}', ']', ')', '>']);
		string expectedQuotePreset = new([';', ':', '.', ',', '=', '}', ']', ')', '>']);

		Assert.AreEqual(AutoClosingOptions.DefaultBracketAutoCloseBefore, expectedBracketPreset);
		Assert.AreEqual(AutoClosingOptions.DefaultQuoteAutoCloseBefore, expectedQuotePreset);
	}

	[TestMethod]
	public void DefaultOptions_EveryPairWrapsSelections()
	{
		foreach (AutoClosingPair pair in AutoClosingOptions.Default.Pairs)
			Assert.IsTrue(pair.WrapSelection, $"Expected '{pair.Open}' to wrap selections by default.");
	}

	[TestMethod]
	public void Options_NullPairs_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new AutoClosingOptions { Pairs = null! });

	[TestMethod]
	public void Options_PairsWithNullEntry_ThrowsArgumentException()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
			_ = new AutoClosingOptions { Pairs = [AutoClosingPair.Parentheses, null!] }
		);

		Assert.AreEqual("value", exception.ParamName);
	}

	[TestMethod]
	public void Options_Pairs_CopyTheAssignedList()
	{
		var pairs = new List<AutoClosingPair> { AutoClosingPair.Parentheses };
		var options = new AutoClosingOptions { Pairs = pairs };

		pairs.Clear();

		Assert.HasCount(1, options.Pairs);
	}
}
