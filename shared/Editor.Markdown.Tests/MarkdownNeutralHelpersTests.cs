#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;
#else
namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;
#endif

/// <summary>
/// Pins the engine-neutral Markdown helpers the two editor bindings share.
/// </summary>
[TestClass]
public sealed class MarkdownNeutralHelpersTests
{
	[TestMethod]
	public void NormalizeCodeText_LineEndingsAndSingleTrailingTerminator_AreNormalized()
	{
		Assert.AreEqual("a\nb\n\n", MarkdownCodeText.Normalize("a\r\nb\n\n\n"));
	}

	[TestMethod]
	public void NormalizeCodeText_SingleTrailingTerminator_IsRemoved()
	{
		Assert.AreEqual("a\nb", MarkdownCodeText.Normalize("a\nb\n"));
	}

	[TestMethod]
	public void NormalizeCodeText_TextWithoutTrailingBlankLines_IsUnchanged()
	{
		Assert.AreEqual("a\nb", MarkdownCodeText.Normalize("a\nb"));
	}

	[TestMethod]
	public void NormalizeCodeText_WhitespaceOnlyContent_BecomesEmpty()
	{
		Assert.AreEqual(string.Empty, MarkdownCodeText.Normalize("   \n  \n"));
	}

	[TestMethod]
	public void GetInnerContentWidth_CarvesPaddingAndBorderOutOfTheSurfaceWidth()
	{
		Assert.AreEqual(80.0, MarkdownCodeLayout.GetInnerContentWidth(100.0, 8.0, 2.0));
	}

	[TestMethod]
	public void GetInnerContentWidth_DegenerateWidth_StaysAtLeastOnePixel()
	{
		Assert.AreEqual(1.0, MarkdownCodeLayout.GetInnerContentWidth(4.0, 8.0, 2.0));
	}

	[TestMethod]
	public void GetHeadingFontSize_ScalesTheBodySizeByTheLevel()
	{
		double size = MarkdownHeadingSizes.GetFontSize(10.0, [1.5, 1.2], 2, double.PositiveInfinity);

		Assert.AreEqual(12.0, size);
	}

	[TestMethod]
	public void GetHeadingFontSize_EmptyScaleList_UsesTheBodySize()
	{
		Assert.AreEqual(10.0, MarkdownHeadingSizes.GetFontSize(10.0, [], 3, double.PositiveInfinity));
	}

	[TestMethod]
	public void GetHeadingFontSize_LevelBeyondTheScaleList_ReusesTheLastScale()
	{
		Assert.AreEqual(20.0, MarkdownHeadingSizes.GetFontSize(10.0, [2.0], 5, double.PositiveInfinity));
	}

	[TestMethod]
	public void GetHeadingFontSize_ExtremeScale_ClampsToTheUpperBound()
	{
		Assert.AreEqual(123.0, MarkdownHeadingSizes.GetFontSize(10.0, [200000.0], 1, 123.0));
	}
}
