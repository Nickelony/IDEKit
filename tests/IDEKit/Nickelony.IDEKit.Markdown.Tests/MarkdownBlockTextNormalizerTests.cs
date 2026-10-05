using Nickelony.IDEKit.Markdown.Model;

namespace Nickelony.IDEKit.Markdown.Tests;

/// <summary>Pins the block-text normalization that the model builder and <see cref="MarkdownCodeBlock.NormalizedCode"/> share.</summary>
[TestClass]
public sealed class MarkdownBlockTextNormalizerTests
{
	[TestMethod]
	public void Normalize_LineEndingsAndSingleTrailingTerminator_AreNormalized() =>
		Assert.AreEqual("a\nb\n\n", MarkdownBlockTextNormalizer.Normalize("a\r\nb\n\n\n"));

	[TestMethod]
	public void Normalize_SingleTrailingTerminator_IsRemoved() => Assert.AreEqual("a\nb", MarkdownBlockTextNormalizer.Normalize("a\nb\n"));

	[TestMethod]
	public void Normalize_TextWithoutTrailingBlankLines_IsUnchanged() => Assert.AreEqual("a\nb", MarkdownBlockTextNormalizer.Normalize("a\nb"));

	[TestMethod]
	public void Normalize_WhitespaceOnlyContent_BecomesEmpty() => Assert.AreEqual(string.Empty, MarkdownBlockTextNormalizer.Normalize("   \n  \n"));
}
