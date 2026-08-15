#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;
#else
namespace Nickelony.IDEKit.AvalonEdit.Markdown;
#endif

/// <summary>
/// Shared geometry for the Markdown code surfaces: the width a code surface's content may occupy once
/// its horizontal padding and border are carved out of the surface's maximum width.
/// </summary>
/// <remarks>
/// A block code editor and an inline code border both size their content with this helper, so the
/// padding-and-border arithmetic is defined once instead of being repeated (and subtly restated) per
/// surface. The helper is engine-neutral: the caller supplies the surface's maximum width rather than
/// the binding's render theme.
/// </remarks>
internal static class MarkdownCodeLayout
{
	/// <summary>
	/// Gets the width available to the content inside a code surface, never reporting less than one
	/// device-independent pixel so a degenerate surface width cannot collapse the child to zero.
	/// </summary>
	/// <param name="maximumWidth">The maximum width the surface occupies.</param>
	/// <param name="horizontalPadding">The surface's left and right padding.</param>
	/// <param name="borderThickness">The surface's uniform border thickness.</param>
	/// <returns>The inner content width in device-independent pixels.</returns>
	internal static double GetInnerContentWidth(double maximumWidth, double horizontalPadding, double borderThickness)
		=> Math.Max(1.0, maximumWidth - (2.0 * (horizontalPadding + borderThickness)));
}
