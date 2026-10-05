using Nickelony.IDEKit.Markdown.Planning;

namespace Nickelony.IDEKit.Markdown.Tests;

/// <summary>Pins the heading-scale lookup and the padding-and-border arithmetic that both Markdown emitters share.</summary>
[TestClass]
public sealed class MarkdownLayoutTests
{
	[TestMethod]
	public void GetHeadingFontSize_ScalesTheBodySizeByTheLevel()
	{
		double size = MarkdownLayout.GetHeadingFontSize(bodyFontSize: 10.0, scales: [1.5, 1.2], level: 2, upperBound: double.PositiveInfinity);
		Assert.AreEqual(12.0, size);
	}

	[TestMethod]
	public void GetHeadingFontSize_LevelOne_UsesTheFirstScale()
	{
		Assert.AreEqual(
			15.0,
			MarkdownLayout.GetHeadingFontSize(bodyFontSize: 10.0, scales: [1.5, 1.2], level: 1, upperBound: double.PositiveInfinity)
		);
	}

	[TestMethod]
	public void GetHeadingFontSize_LevelBelowOne_UsesTheFirstScale()
	{
		Assert.AreEqual(
			15.0,
			MarkdownLayout.GetHeadingFontSize(bodyFontSize: 10.0, scales: [1.5, 1.2], level: 0, upperBound: double.PositiveInfinity)
		);
	}

	[TestMethod]
	public void GetHeadingFontSize_EmptyScaleList_UsesTheBodySize() =>
		Assert.AreEqual(10.0, MarkdownLayout.GetHeadingFontSize(bodyFontSize: 10.0, scales: [], level: 3, upperBound: double.PositiveInfinity));

	[TestMethod]
	public void GetHeadingFontSize_LevelBeyondTheScaleList_ReusesTheLastScale() =>
		Assert.AreEqual(20.0, MarkdownLayout.GetHeadingFontSize(bodyFontSize: 10.0, scales: [2.0], level: 5, upperBound: double.PositiveInfinity));

	[TestMethod]
	public void GetHeadingFontSize_ExtremeScale_ClampsToTheUpperBound() =>
		Assert.AreEqual(123.0, MarkdownLayout.GetHeadingFontSize(bodyFontSize: 10.0, scales: [200000.0], level: 1, upperBound: 123.0));

	[TestMethod]
	public void GetInnerContentWidth_CarvesPaddingAndBorderOutOfTheSurfaceWidth() =>
		Assert.AreEqual(80.0, MarkdownLayout.GetInnerContentWidth(100.0, 8.0, 2.0));

	[TestMethod]
	public void GetInnerContentWidth_DegenerateWidth_StaysAtLeastOnePixel() =>
		Assert.AreEqual(1.0, MarkdownLayout.GetInnerContentWidth(4.0, 8.0, 2.0));
}
