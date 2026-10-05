namespace Nickelony.IDEKit.Markdown;

/// <summary>The four edge widths of a margin, padding or border in a render plan, in device-independent pixels.</summary>
/// <param name="Left">The left edge width.</param>
/// <param name="Top">The top edge width.</param>
/// <param name="Right">The right edge width.</param>
/// <param name="Bottom">The bottom edge width.</param>
/// <remarks>
/// The value carries no toolkit type, so the render plan stays engine-free; each editor binding turns it into the toolkit's own thickness.
/// </remarks>
public readonly record struct MarkdownThickness(double Left, double Top, double Right, double Bottom)
{
	/// <summary>Creates a thickness whose four edges have the same width.</summary>
	/// <param name="width">The width of every edge.</param>
	public static MarkdownThickness Uniform(double width) => new(Left: width, Top: width, Right: width, Bottom: width);

	/// <summary>Creates a thickness whose left and right edges share one width and whose top and bottom edges share another.</summary>
	/// <param name="horizontal">The width of the left and right edges.</param>
	/// <param name="vertical">The width of the top and bottom edges.</param>
	public static MarkdownThickness Symmetric(double horizontal, double vertical) =>
		new(Left: horizontal, Top: vertical, Right: horizontal, Bottom: vertical);

	/// <summary>Creates a thickness that spaces a block below it, with every other edge at zero.</summary>
	/// <param name="bottom">The width of the bottom edge.</param>
	public static MarkdownThickness Below(double bottom) => new(Left: 0.0, Top: 0.0, Right: 0.0, bottom);
}
