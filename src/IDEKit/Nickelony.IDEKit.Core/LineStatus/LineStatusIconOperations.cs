namespace Nickelony.IDEKit.Core.LineStatus;

/// <summary>Computes where a line-status margin draws an icon.</summary>
public static class LineStatusIconOperations
{
	/// <summary>Scales an icon's bounds by the font scale and centers the scaled icon horizontally in the margin.</summary>
	/// <param name="boundsLeft">The left edge of the icon geometry's bounds.</param>
	/// <param name="boundsTop">The top edge of the icon geometry's bounds.</param>
	/// <param name="boundsWidth">The width of the icon geometry's bounds.</param>
	/// <param name="boundsHeight">The height of the icon geometry's bounds.</param>
	/// <param name="fontScale">The uniform scale that fits the geometry to the editor font.</param>
	/// <param name="marginWidth">The width of the margin the icon is centered in.</param>
	/// <returns>The scale and the offsets that place the geometry.</returns>
	/// <remarks>
	/// The offsets undo the geometry's own origin, so a geometry whose bounds do not start at the origin lands where the bounds say.
	/// The method does not validate its input; a non-finite value yields a non-finite placement.
	/// </remarks>
	public static LineStatusIconPlacement Place(
		double boundsLeft,
		double boundsTop,
		double boundsWidth,
		double boundsHeight,
		double fontScale,
		double marginWidth
	)
	{
		double iconWidth = boundsWidth * fontScale;
		double iconLeft = (marginWidth - iconWidth) / 2.0;

		return new LineStatusIconPlacement(
			Scale: fontScale,
			IconHeight: boundsHeight * fontScale,
			OffsetX: iconLeft - (boundsLeft * fontScale),
			OffsetY: -(boundsTop * fontScale)
		);
	}

	/// <summary>Returns the icon's top offset within its line, clamped into the box of the line's first text row.</summary>
	/// <param name="lineHeight">The height of the whole line, which is several text rows tall when the line wraps.</param>
	/// <param name="firstRowHeight">The height of the first text row of the line.</param>
	/// <param name="iconHeight">The height of the icon.</param>
	/// <returns>The offset of the icon's top edge from the top of the line.</returns>
	/// <remarks>
	/// A wrapped line's box can be taller than the viewport, so centering the icon in the whole box could keep it out of view while the first
	/// text row is visible. The centered position is therefore clamped so the icon stays inside the first row, and an icon taller than that row sits
	/// at the top.
	/// </remarks>
	public static double GetTopWithinLine(double lineHeight, double firstRowHeight, double iconHeight) =>
		Math.Clamp((lineHeight - iconHeight) / 2.0, 0.0, Math.Max(0.0, firstRowHeight - iconHeight));
}
