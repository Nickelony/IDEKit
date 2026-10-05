using System.Windows;
using System.Windows.Controls;

namespace Nickelony.IDEKit.AvalonEdit.Markdown;

/// <summary>
/// A flow-document viewer that is as wide as its widest unwrapped line, up to the width it is given, instead of always taking all of it.
/// </summary>
/// <remarks>
/// A <see cref="System.Windows.Documents.FlowDocument"/> has no natural width, so a viewer in a popup would always fill its maximum width.
/// The viewer measures the document at the full width, then searches for the narrowest width at which it is no taller: any narrower and a paragraph
/// or a code block would wrap one more line. A document that reaches the height it is given scrolls, and a viewer without a maximum width
/// fills its host, so both keep the full width.
/// </remarks>
internal sealed class ContentSizedFlowDocumentScrollViewer : FlowDocumentScrollViewer
{
	// A pixel of slack absorbs the rounding of the layout, and ten halvings narrow a 1,000-pixel range to that pixel.
	private const double HeightTolerance = 0.5;
	private const int MaxSearchSteps = 10;

	protected override Size MeasureOverride(Size constraint)
	{
		Size full = base.MeasureOverride(constraint);

		// An unbounded maximum width lets the host stretch the viewer, and content as tall as the room it has may scroll, where a narrower
		// width would not show as a taller viewer; either keeps the full width.
		if (double.IsInfinity(MaxWidth) || full.Height <= 0.0 || full.Height >= Math.Min(MaxHeight, constraint.Height) - HeightTolerance)
			return full;

		double narrowTooFar = 0.0;
		double wideEnough = constraint.Width;

		for (int step = 0; step < MaxSearchSteps && wideEnough - narrowTooFar > 1.0; step++)
		{
			double width = (narrowTooFar + wideEnough) / 2.0;

			if (base.MeasureOverride(new Size(width, constraint.Height)).Height > full.Height + HeightTolerance)
				narrowTooFar = width;
			else
				wideEnough = width;
		}

		double contentWidth = Math.Min(constraint.Width, Math.Ceiling(wideEnough));
		Size fitted = base.MeasureOverride(new Size(contentWidth, constraint.Height));

		return new Size(contentWidth, fitted.Height);
	}
}
