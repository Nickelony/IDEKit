namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the character-level text matcher's boundary behavior: the empty text, an offset outside the source, and an expected text longer
/// than the remaining source all match nothing.
/// </summary>
[TestClass]
public sealed class TextMatchProbeTests
{
	private sealed class StubCharSource(string text) : ITextCharSource
	{
		public int TextLength => text.Length;
		public char GetCharAt(int offset) => text[offset];
	}

	private readonly struct StubStructCharSource(string text) : ITextCharSource
	{
		public int TextLength => text.Length;
		public char GetCharAt(int offset) => text[offset];
	}

	[TestMethod]
	public void MatchesAt_TextAtTheOffset_Matches() => Assert.IsTrue(TextMatchProbe.MatchesAt(new StubCharSource("open(close)"), 5, "close"));

	[TestMethod]
	public void MatchesAt_StructSource_MatchesLikeAReferenceSource() =>
		Assert.IsTrue(TextMatchProbe.MatchesAt(new StubStructCharSource("open(close)"), 5, "close"));

	[TestMethod]
	public void MatchesAt_TextEndingAtTheSourceEnd_Matches() => Assert.IsTrue(TextMatchProbe.MatchesAt(new StubCharSource("abc"), 0, "abc"));

	[TestMethod]
	public void MatchesAt_SingleCharacterAtTheLastOffset_Matches() => Assert.IsTrue(TextMatchProbe.MatchesAt(new StubCharSource("abc"), 2, "c"));

	[TestMethod]
	public void MatchesAt_EmptyExpectedText_DoesNotMatch() => Assert.IsFalse(TextMatchProbe.MatchesAt(new StubCharSource("abc"), 0, string.Empty));

	[TestMethod]
	public void MatchesAt_ExpectedTextLongerThanTheRemainder_DoesNotMatch() =>
		Assert.IsFalse(TextMatchProbe.MatchesAt(new StubCharSource("abc"), 1, "bcx"));

	[TestMethod]
	public void MatchesAt_NegativeOffset_DoesNotMatch() => Assert.IsFalse(TextMatchProbe.MatchesAt(new StubCharSource("abc"), -1, "a"));

	[TestMethod]
	public void MatchesAt_NonEmptyTextAtTheSourceEnd_DoesNotMatch() => Assert.IsFalse(TextMatchProbe.MatchesAt(new StubCharSource("abc"), 3, "a"));

	[TestMethod]
	public void MatchesAt_DifferingCharacter_DoesNotMatch() => Assert.IsFalse(TextMatchProbe.MatchesAt(new StubCharSource("abc"), 1, "Bc"));

	[TestMethod]
	public void MatchesAt_NullExpectedText_Throws()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			TextMatchProbe.MatchesAt(new StubCharSource("abc"), 0, null!)
		);

		Assert.AreEqual("expectedText", exception.ParamName);
	}

	[TestMethod]
	public void MatchesAt_NullSource_Throws()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() => TextMatchProbe.MatchesAt<StubCharSource>(null!, 0, "a"));
		Assert.AreEqual("source", exception.ParamName);
	}
}
