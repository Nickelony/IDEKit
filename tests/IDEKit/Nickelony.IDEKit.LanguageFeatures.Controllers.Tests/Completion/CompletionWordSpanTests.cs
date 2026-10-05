using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class CompletionWordSpanTests
{
	[TestMethod]
	public void Constructor_StoresWordAndRange()
	{
		var span = new CompletionWordSpan("Bet", new TextRange(4, 3));

		Assert.AreEqual("Bet", span.Word);
		Assert.AreEqual(new TextRange(4, 3), span.Range);
	}

	[TestMethod]
	public void Constructor_NullWord_NormalizesToEmpty()
	{
		var nullWord = new CompletionWordSpan(null, new TextRange(4, 3));
		var emptyWord = new CompletionWordSpan(string.Empty, new TextRange(4, 3));

		Assert.AreEqual(string.Empty, nullWord.Word);
		Assert.AreEqual(emptyWord, nullWord);
	}

	[TestMethod]
	public void Constructor_WhitespaceWord_IsPreserved()
	{
		// Only null and empty collapse to the empty state; a whitespace word is a real (literal)
		// match word, so it must not be trimmed away.
		var span = new CompletionWordSpan("   ", new TextRange(0, 3));
		Assert.AreEqual("   ", span.Word);
	}

	[TestMethod]
	public void Constructor_Range_IsStoredAsSupplied()
	{
		// The struct does not clamp the range against a document; the caller owns consistent values.
		var span = new CompletionWordSpan("Bet", new TextRange(0, 99));
		Assert.AreEqual(new TextRange(0, 99), span.Range);
	}

	[TestMethod]
	public void Default_BehavesLikeAnExplicitlyEmptyWord()
	{
		var defaultValue = default(CompletionWordSpan);
		var emptyWord = new CompletionWordSpan(null, default);

		Assert.AreEqual(string.Empty, defaultValue.Word);
		Assert.AreEqual(default, defaultValue.Range);
		Assert.AreEqual(emptyWord, defaultValue);
	}

	[TestMethod]
	public void Deconstruct_ReturnsTheNormalizedWordAndRange()
	{
		var span = new CompletionWordSpan(null, new TextRange(6, 0));

		span.Deconstruct(out string word, out TextRange range);

		Assert.AreEqual(string.Empty, word);
		Assert.AreEqual(new TextRange(6, 0), range);
	}

	[TestMethod]
	public void Equality_ComparesTheNormalizedWordAndRange()
	{
		var span = new CompletionWordSpan("Bet", new TextRange(4, 3));

		Assert.AreEqual(span, new CompletionWordSpan("Bet", new TextRange(4, 3)));
		Assert.AreEqual(span.GetHashCode(), new CompletionWordSpan("Bet", new TextRange(4, 3)).GetHashCode());
		Assert.AreNotEqual(span, new CompletionWordSpan("Bet", new TextRange(5, 3)));
		Assert.AreNotEqual(span, new CompletionWordSpan("Betting", new TextRange(4, 3)));
	}
}
