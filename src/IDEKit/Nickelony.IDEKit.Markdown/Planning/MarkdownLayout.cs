namespace Nickelony.IDEKit.Markdown.Planning;

/// <summary>The block-layout measurements and sizing rules the render planner applies, expressed in device-independent pixels.</summary>
/// <remarks>
/// The measurements are internal to the package rather than public API, because a published constant would become a contract
/// that a visual tweak breaks. The plan carries the values an emitter needs.
/// </remarks>
internal static class MarkdownLayout
{
	internal const double InlineCodeHorizontalPadding = 4.0;
	internal const double InlineCodeVerticalPadding = 1.0;
	internal const double InlineCodeBorderThickness = 1.0;
	internal const double InlineCodeCornerRadius = 2.0;
	internal const double QuotePaddingHorizontal = 10.0;
	internal const double QuotePaddingVertical = 4.0;
	internal const double QuoteBarThickness = 3.0;
	internal const double ListIndent = 18.0;
	internal const double ListMarkerGap = 6.0;
	internal const double TableCellBorderThickness = 0.5;
	internal const double TableCellHorizontalPadding = 6.0;
	internal const double TableCellVerticalPadding = 2.0;
	internal const double ThematicBreakThickness = 1.0;
	internal const double CodeBlockHorizontalPadding = 8.0;
	internal const double CodeBlockVerticalPadding = 6.0;
	internal const double CodeBlockBorderThickness = 1.0;
	internal const double CodeBlockCornerRadius = 3.0;

	/// <summary>
	/// Returns the width available to the content inside a code surface, never reporting less than one device-independent pixel so a degenerate
	/// surface width cannot collapse the child to zero.
	/// </summary>
	/// <param name="maximumWidth">The maximum width the surface occupies.</param>
	/// <param name="horizontalPadding">The surface's left and right padding.</param>
	/// <param name="borderThickness">The surface's uniform border thickness.</param>
	/// <returns>The inner content width in device-independent pixels.</returns>
	/// <remarks>
	/// A block code surface and an inline code border both size their content with this method, so the padding-and-border arithmetic is defined
	/// once for both surfaces.
	/// </remarks>
	internal static double GetInnerContentWidth(double maximumWidth, double horizontalPadding, double borderThickness) =>
		Math.Max(1.0, maximumWidth - (2.0 * (horizontalPadding + borderThickness)));

	/// <summary>Returns the font size for a heading level from the theme's heading-scale list.</summary>
	/// <param name="bodyFontSize">The theme's body font size.</param>
	/// <param name="scales">
	/// The theme's heading font-size multipliers, one per heading level starting at level one. When the list is empty the body font size is used.
	/// </param>
	/// <param name="level">The heading level, one for the outermost heading.</param>
	/// <param name="upperBound">The cap on the returned font size, so a heading stays within what the binding's toolkit accepts.</param>
	/// <returns>The heading font size, capped at <paramref name="upperBound"/>.</returns>
	/// <remarks>
	/// The scale lookup and the rule that the last scale serves every deeper level are toolkit-neutral and defined once for every editor binding.
	/// </remarks>
	internal static double GetHeadingFontSize(double bodyFontSize, IReadOnlyList<double> scales, int level, double upperBound)
	{
		if (scales.Count == 0)
			return bodyFontSize;

		int index = Math.Max(0, Math.Min(scales.Count - 1, level - 1));

		return Math.Min(bodyFontSize * scales[index], upperBound);
	}
}
