namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class LineStatusIconOperationsTests
{
	[TestMethod]
	public void Place_CentersTheIconInTheMargin()
	{
		LineStatusIconPlacement placement = LineStatusIconOperations.Place(
			boundsLeft: 0.0,
			boundsTop: 0.0,
			boundsWidth: 10.0,
			boundsHeight: 9.0,
			fontScale: 1.0,
			marginWidth: 16.0
		);

		Assert.AreEqual(new LineStatusIconPlacement(Scale: 1.0, IconHeight: 9.0, OffsetX: 3.0, OffsetY: 0.0), placement);
	}

	[TestMethod]
	public void Place_ScalesTheBoundsAndUndoesTheirOffsetFromTheOrigin()
	{
		LineStatusIconPlacement placement = LineStatusIconOperations.Place(
			boundsLeft: 2.0,
			boundsTop: 3.0,
			boundsWidth: 10.0,
			boundsHeight: 8.0,
			fontScale: 1.5,
			marginWidth: 30.0
		);

		// The scaled icon is 15 wide and sits at 7.5 in the margin; the geometry's own left edge, 3 after scaling, is taken out of the offset.
		Assert.AreEqual(new LineStatusIconPlacement(Scale: 1.5, IconHeight: 12.0, OffsetX: 4.5, OffsetY: -4.5), placement);
	}

	[TestMethod]
	public void Place_IconWiderThanTheMargin_OffsetsNegatively()
	{
		LineStatusIconPlacement placement = LineStatusIconOperations.Place(
			boundsLeft: 0.0,
			boundsTop: 0.0,
			boundsWidth: 10.0,
			boundsHeight: 9.0,
			fontScale: 1.0,
			marginWidth: 4.0
		);

		Assert.AreEqual(-3.0, placement.OffsetX);
	}

	[TestMethod]
	public void GetTopWithinLine_LineOfOneRow_CentersTheIcon()
	{
		double top = LineStatusIconOperations.GetTopWithinLine(lineHeight: 20.0, firstRowHeight: 20.0, iconHeight: 8.0);
		Assert.AreEqual(6.0, top);
	}

	[TestMethod]
	public void GetTopWithinLine_WrappedLine_KeepsTheIconInsideTheFirstRow()
	{
		// Centering in the 100-tall box would put the icon at 46, below the first 20-tall row.
		double top = LineStatusIconOperations.GetTopWithinLine(lineHeight: 100.0, firstRowHeight: 20.0, iconHeight: 8.0);
		Assert.AreEqual(12.0, top);
	}

	[TestMethod]
	public void GetTopWithinLine_IconTallerThanTheLine_SitsAtTheTop()
	{
		double top = LineStatusIconOperations.GetTopWithinLine(lineHeight: 10.0, firstRowHeight: 10.0, iconHeight: 12.0);
		Assert.AreEqual(0.0, top);
	}

	[TestMethod]
	public void GetTopWithinLine_IconTallerThanTheFirstRow_SitsAtTheTop()
	{
		double top = LineStatusIconOperations.GetTopWithinLine(lineHeight: 100.0, firstRowHeight: 6.0, iconHeight: 8.0);
		Assert.AreEqual(0.0, top);
	}
}
