#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;
#else
namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;
#endif

/// <summary>
/// Pins the toolkit-neutral validation and copy-on-assign behavior of <see cref="MarkdownRenderTheme"/>.
/// </summary>
/// <remarks>
/// The theme's brush surface is toolkit-specific and is covered by each binding's own suite; everything
/// numeric is shared and covered here once.
/// </remarks>
[TestClass]
public sealed class MarkdownRenderThemeTests
{
	private static MarkdownRenderTheme CreateThemeWith(string propertyName, double value)
	{
		return propertyName switch
		{
			nameof(MarkdownRenderTheme.BodyFontSize) => MarkdownRenderTheme.Default with { BodyFontSize = value },
			nameof(MarkdownRenderTheme.CodeFontSize) => MarkdownRenderTheme.Default with { CodeFontSize = value },
			nameof(MarkdownRenderTheme.MaxWidth) => MarkdownRenderTheme.Default with { MaxWidth = value },
			nameof(MarkdownRenderTheme.MaxHeight) => MarkdownRenderTheme.Default with { MaxHeight = value },
			nameof(MarkdownRenderTheme.CodeMaxWidth) => MarkdownRenderTheme.Default with { CodeMaxWidth = value },
			nameof(MarkdownRenderTheme.MaxVisibleCodeBlockLines) => MarkdownRenderTheme.Default with { MaxVisibleCodeBlockLines = (int)value },
			nameof(MarkdownRenderTheme.BlockSpacing) => MarkdownRenderTheme.Default with { BlockSpacing = value },
			nameof(MarkdownRenderTheme.HeadingFontSizeScales) => MarkdownRenderTheme.Default with { HeadingFontSizeScales = [1.0, value] },
			_ => throw new InvalidOperationException($"Unknown theme property '{propertyName}'.")
		};
	}

	[DataRow(nameof(MarkdownRenderTheme.BodyFontSize), 0.0, DisplayName = "BodyFontSizeZero")]
	[DataRow(nameof(MarkdownRenderTheme.BodyFontSize), double.PositiveInfinity, DisplayName = "BodyFontSizeInfinity")]
	[DataRow(nameof(MarkdownRenderTheme.BodyFontSize), 160001.0, DisplayName = "BodyFontSizeAboveTheAcceptedLimit")]
	[DataRow(nameof(MarkdownRenderTheme.CodeFontSize), -1.0, DisplayName = "CodeFontSizeNegative")]
	[DataRow(nameof(MarkdownRenderTheme.CodeFontSize), 160001.0, DisplayName = "CodeFontSizeAboveTheAcceptedLimit")]
	[DataRow(nameof(MarkdownRenderTheme.MaxWidth), 0.0, DisplayName = "MaxWidthZero")]
	[DataRow(nameof(MarkdownRenderTheme.MaxWidth), double.NaN, DisplayName = "MaxWidthNaN")]
	[DataRow(nameof(MarkdownRenderTheme.MaxHeight), 0.0, DisplayName = "MaxHeightZero")]
	[DataRow(nameof(MarkdownRenderTheme.CodeMaxWidth), 0.0, DisplayName = "CodeMaxWidthZero")]
	[DataRow(nameof(MarkdownRenderTheme.MaxVisibleCodeBlockLines), 0.0, DisplayName = "MaxVisibleCodeBlockLinesZero")]
	[DataRow(nameof(MarkdownRenderTheme.BlockSpacing), -1.0, DisplayName = "BlockSpacingNegative")]
	[DataRow(nameof(MarkdownRenderTheme.BlockSpacing), double.PositiveInfinity, DisplayName = "BlockSpacingInfinity")]
	[DataRow(nameof(MarkdownRenderTheme.HeadingFontSizeScales), 0.0, DisplayName = "HeadingFontSizeScaleZero")]
	[TestMethod]
	public void Theme_InvalidValue_ThrowsArgumentOutOfRangeReportingThePropertyName(string propertyName, double value)
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = CreateThemeWith(propertyName, value));

		Assert.AreEqual(propertyName, exception.ParamName);
	}

	[TestMethod]
	public void Theme_NullHeadingScales_ThrowArgumentNull()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = MarkdownRenderTheme.Default with { HeadingFontSizeScales = null! });
	}

	[TestMethod]
	public void Theme_LargestFontSizeAtTheAcceptedLimit_IsAccepted()
	{
		double largestAcceptedSize = MarkdownRenderTheme.FontSizeUpperBound;

		var theme = MarkdownRenderTheme.Default with { BodyFontSize = largestAcceptedSize };

		Assert.AreEqual(largestAcceptedSize, theme.BodyFontSize);
	}

	[TestMethod]
	public void Theme_HeadingFontSizeScales_AreCopiedOnAssignment()
	{
		double[] scales = [2.0, 3.0];
		var theme = MarkdownRenderTheme.Default with { HeadingFontSizeScales = scales };

		scales[0] = 5.0;

		Assert.AreEqual(2.0, theme.HeadingFontSizeScales[0]);
	}

	[TestMethod]
	public void Theme_PositiveInfinitySizeLimits_AreAccepted()
	{
		var theme = MarkdownRenderTheme.Default with
		{
			MaxWidth = double.PositiveInfinity,
			MaxHeight = double.PositiveInfinity,
			CodeMaxWidth = double.PositiveInfinity
		};

		Assert.IsTrue(double.IsPositiveInfinity(theme.MaxWidth));
		Assert.IsTrue(double.IsPositiveInfinity(theme.MaxHeight));
		Assert.IsTrue(double.IsPositiveInfinity(theme.CodeMaxWidth));
	}

	[TestMethod]
	public void Theme_BlendRatioOutsideRange_IsClampedOnAssignment()
	{
		Assert.AreEqual(1.0, (MarkdownRenderTheme.Default with { CodeBackgroundBlendRatio = 5.0 }).CodeBackgroundBlendRatio);
		Assert.AreEqual(0.0, (MarkdownRenderTheme.Default with { CodeBackgroundBlendRatio = -1.0 }).CodeBackgroundBlendRatio);
		Assert.AreEqual(1.0, (MarkdownRenderTheme.Default with { BorderBlendRatio = 2.0 }).BorderBlendRatio);
		Assert.AreEqual(0.0, (MarkdownRenderTheme.Default with { BorderBlendRatio = -2.0 }).BorderBlendRatio);
	}

	[TestMethod]
	public void Theme_NaNBlendRatio_ThrowsArgumentOutOfRange()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = MarkdownRenderTheme.Default with { CodeBackgroundBlendRatio = double.NaN });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = MarkdownRenderTheme.Default with { BorderBlendRatio = double.NaN });
	}
}
