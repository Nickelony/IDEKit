using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

[TestClass]
public sealed class TextCompletionWordSpanTests
{
	[TestMethod]
	public void Constructor_StoresWordAndRange()
	{
		var span = new TextCompletionWordSpan("Bet", new TextRange(4, 3));

		Assert.AreEqual("Bet", span.Word);
		Assert.AreEqual(new TextRange(4, 3), span.Range);
	}

	[TestMethod]
	public void Constructor_NullWord_NormalizesToEmpty()
	{
		var nullWord = new TextCompletionWordSpan(null, new TextRange(4, 3));
		var emptyWord = new TextCompletionWordSpan(string.Empty, new TextRange(4, 3));

		Assert.AreEqual(string.Empty, nullWord.Word);
		Assert.AreEqual(emptyWord, nullWord);
	}

	[TestMethod]
	public void Constructor_WhitespaceWord_IsPreserved()
	{
		// Only null and empty collapse to the empty state; a whitespace word is a real (literal)
		// match word, so it must not be trimmed away.
		var span = new TextCompletionWordSpan("   ", new TextRange(0, 3));

		Assert.AreEqual("   ", span.Word);
	}

	[TestMethod]
	public void Constructor_Range_IsStoredAsSupplied()
	{
		// The struct does not clamp the range against a document; the caller owns consistent values.
		var span = new TextCompletionWordSpan("Bet", new TextRange(0, 99));

		Assert.AreEqual(new TextRange(0, 99), span.Range);
	}

	[TestMethod]
	public void Default_BehavesLikeAnExplicitlyEmptyWord()
	{
		var defaultValue = default(TextCompletionWordSpan);
		var emptyWord = new TextCompletionWordSpan(null, default);

		Assert.AreEqual(string.Empty, defaultValue.Word);
		Assert.AreEqual(default(TextRange), defaultValue.Range);
		Assert.AreEqual(emptyWord, defaultValue);
	}

	[TestMethod]
	public void Deconstruct_ReturnsTheNormalizedWordAndRange()
	{
		var span = new TextCompletionWordSpan(null, new TextRange(6, 0));

		span.Deconstruct(out string word, out TextRange range);

		Assert.AreEqual(string.Empty, word);
		Assert.AreEqual(new TextRange(6, 0), range);
	}

	[TestMethod]
	public void Equality_ComparesTheNormalizedWordAndRange()
	{
		var span = new TextCompletionWordSpan("Bet", new TextRange(4, 3));

		Assert.AreEqual(span, new TextCompletionWordSpan("Bet", new TextRange(4, 3)));
		Assert.AreEqual(span.GetHashCode(), new TextCompletionWordSpan("Bet", new TextRange(4, 3)).GetHashCode());
		Assert.AreNotEqual(span, new TextCompletionWordSpan("Bet", new TextRange(5, 3)));
		Assert.AreNotEqual(span, new TextCompletionWordSpan("Betting", new TextRange(4, 3)));
	}
}
