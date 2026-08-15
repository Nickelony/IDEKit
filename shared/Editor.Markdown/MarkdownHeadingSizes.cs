#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;
#else
namespace Nickelony.IDEKit.AvalonEdit.Markdown;
#endif

/// <summary>
/// Resolves the font size of a Markdown heading from the theme's heading-scale list.
/// </summary>
/// <remarks>
/// The scale lookup and the "reuse the last scale for deeper levels" rule are toolkit-neutral and defined
/// once for every editor binding. The upper bound is a parameter because the largest font size a toolkit
/// accepts is toolkit-specific: a binding supplies its own bound so an extreme scale cannot fail the whole
/// rendering.
/// </remarks>
internal static class MarkdownHeadingSizes
{
	/// <summary>
	/// Gets the font size for a heading level.
	/// </summary>
	/// <param name="bodyFontSize">The theme's body font size.</param>
	/// <param name="scales">
	/// The theme's heading font-size multipliers, one per heading level starting at level one. When the list
	/// is empty the body font size is used.
	/// </param>
	/// <param name="level">The heading level, one for the outermost heading.</param>
	/// <param name="upperBound">The largest font size the binding's toolkit accepts.</param>
	/// <returns>The heading font size, capped at <paramref name="upperBound"/>.</returns>
	internal static double GetFontSize(double bodyFontSize, IReadOnlyList<double> scales, int level, double upperBound)
	{
		if (scales.Count == 0)
			return bodyFontSize;

		int index = Math.Max(0, Math.Min(scales.Count - 1, level - 1));

		return Math.Min(bodyFontSize * scales[index], upperBound);
	}
}
