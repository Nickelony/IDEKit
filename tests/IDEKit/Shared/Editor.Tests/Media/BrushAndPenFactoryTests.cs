#if AVALONIAEDIT
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Nickelony.IDEKit.AvaloniaEdit.Media;
using static Nickelony.IDEKit.AvaloniaEdit.Tests.BrushAndPenTestHelpers;
using Brush = Avalonia.Media.IBrush;
using Pen = Avalonia.Media.IPen;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Windows.Media;
using Nickelony.IDEKit.AvalonEdit.Media;
using static Nickelony.IDEKit.AvalonEdit.Tests.BrushAndPenTestHelpers;
using Brush = System.Windows.Media.Brush;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

/// <summary>
/// Pins what the brush and pen factory produces in each engine: the color of a brush from a color, and the thickness, round caps and dash pattern
/// of a pen. A brush or pen is shareable when the engine marks it frozen or immutable.
/// </summary>
[TestClass]
public sealed class BrushAndPenFactoryTests
{
	[TestMethod]
	public void CreateBrush_FromAColor_ReturnsAShareableBrushOfThatColor()
	{
		var color = Color.FromRgb(0x12, 0x34, 0x56);

		Brush brush = BrushAndPenFactory.CreateBrush(color);

		Assert.AreEqual(color, ColorOf(brush));
		Assert.IsTrue(IsShareable(brush));
	}

	[TestMethod]
	public void CreatePen_ReturnsAShareablePenWithTheBrushAndThickness()
	{
		Brush brush = BrushAndPenFactory.CreateBrush(Color.FromRgb(0x01, 0x02, 0x03));

		Pen pen = BrushAndPenFactory.CreatePen(brush, 2.5);

		Assert.AreSame(brush, pen.Brush);
		Assert.AreEqual(2.5, pen.Thickness);
		Assert.IsTrue(IsShareable(pen));
	}

	[TestMethod]
	public void CreatePen_WithoutABrush_KeepsTheThickness()
	{
		Pen pen = BrushAndPenFactory.CreatePen(null, 1.0);

		Assert.IsNull(pen.Brush);
		Assert.AreEqual(1.0, pen.Thickness);
	}

	[TestMethod]
	public void CreatePen_WithANegativeThickness_Throws()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BrushAndPenFactory.CreatePen(null, -1.0));
		Assert.AreEqual("thickness", exception.ParamName);
	}

	[TestMethod]
	public void CreateDashedPen_ReturnsAShareablePenWithRoundCapsAndTheDashPattern()
	{
		Brush brush = BrushAndPenFactory.CreateBrush(Color.FromRgb(0x01, 0x02, 0x03));

		Pen pen = BrushAndPenFactory.CreateDashedPen(brush, 2.0, [1.0, 3.0]);

		Assert.AreSame(brush, pen.Brush);
		Assert.AreEqual(2.0, pen.Thickness);
		Assert.AreSequenceEqual([1.0, 3.0], DashesOf(pen));
		Assert.IsTrue(HasRoundCaps(pen));
		Assert.IsTrue(IsShareable(pen));
	}

	[TestMethod]
	public void CreateDashedPen_WithANegativeThickness_Throws()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			BrushAndPenFactory.CreateDashedPen(null, -1.0, [1.0])
		);

		Assert.AreEqual("thickness", exception.ParamName);
	}

#if AVALONIAEDIT
	[TestMethod]
	public void CreatePen_OverAMutableBrush_ReturnsAMutablePen()
	{
		var brush = new SolidColorBrush(Colors.Red);

		Pen pen = BrushAndPenFactory.CreatePen(brush, 1.0);

		Assert.IsFalse(pen is ImmutablePen);
		Assert.AreSame(brush, pen.Brush);
	}
#endif
}
