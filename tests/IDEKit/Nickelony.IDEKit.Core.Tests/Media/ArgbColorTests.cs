namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class ArgbColorTests
{
	[TestMethod]
	public void FromRgb_CreatesAnOpaqueColor()
	{
		var color = ArgbColor.FromRgb(10, 20, 30);

		Assert.AreEqual(255, color.A);
		Assert.AreEqual(10, color.R);
		Assert.AreEqual(20, color.G);
		Assert.AreEqual(30, color.B);
	}

	[TestMethod]
	public void BlackAndWhite_AreOpaque()
	{
		Assert.AreEqual(new ArgbColor(A: 255, R: 0, G: 0, B: 0), ArgbColor.Black);
		Assert.AreEqual(new ArgbColor(A: 255, R: 255, G: 255, B: 255), ArgbColor.White);
	}

	[TestMethod]
	public void Equality_ComparesEveryChannel()
	{
		var color = new ArgbColor(A: 1, R: 2, G: 3, B: 4);

		Assert.AreEqual(color, new ArgbColor(A: 1, R: 2, G: 3, B: 4));
		Assert.AreEqual(color.GetHashCode(), new ArgbColor(A: 1, R: 2, G: 3, B: 4).GetHashCode());
		Assert.AreNotEqual(color, new ArgbColor(A: 0, R: 2, G: 3, B: 4));
		Assert.AreNotEqual(color, new ArgbColor(A: 1, R: 0, G: 3, B: 4));
		Assert.AreNotEqual(color, new ArgbColor(A: 1, R: 2, G: 0, B: 4));
		Assert.AreNotEqual(color, new ArgbColor(A: 1, R: 2, G: 3, B: 0));
	}

	[TestMethod]
	public void GetLuma_BlackIsZeroAndWhiteIsOne()
	{
		Assert.AreEqual(0.0, ArgbColor.Black.GetLuma(), 1e-9);
		Assert.AreEqual(1.0, ArgbColor.White.GetLuma(), 1e-9);
	}

	[TestMethod]
	public void GetLuma_WeightsEachChannelByItsContribution()
	{
		Assert.AreEqual(0.299, ArgbColor.FromRgb(255, 0, 0).GetLuma(), 1e-9);
		Assert.AreEqual(0.587, ArgbColor.FromRgb(0, 255, 0).GetLuma(), 1e-9);
		Assert.AreEqual(0.114, ArgbColor.FromRgb(0, 0, 255).GetLuma(), 1e-9);
	}

	[TestMethod]
	public void GetLuma_IgnoresTheAlphaChannel() =>
		Assert.AreEqual(new ArgbColor(A: 0, R: 10, G: 20, B: 30).GetLuma(), new ArgbColor(A: 255, R: 10, G: 20, B: 30).GetLuma(), 1e-9);

	[TestMethod]
	public void Blend_AtTheEnds_ReturnsTheInputs()
	{
		var first = new ArgbColor(A: 10, R: 20, G: 30, B: 40);
		var second = new ArgbColor(A: 200, R: 180, G: 160, B: 140);

		Assert.AreEqual(first, ArgbColor.Blend(first, second, 0.0));
		Assert.AreEqual(second, ArgbColor.Blend(first, second, 1.0));
	}

	[TestMethod]
	public void Blend_InterpolatesEveryChannelIncludingAlpha()
	{
		var first = new ArgbColor(A: 0, R: 100, G: 0, B: 200);
		var second = new ArgbColor(A: 100, R: 200, G: 100, B: 0);

		Assert.AreEqual(new ArgbColor(A: 25, R: 125, G: 25, B: 150), ArgbColor.Blend(first, second, 0.25));
	}

	[TestMethod]
	public void Blend_AtAnExactMidpoint_RoundsToTheEvenChannelValue()
	{
		// Math.Round rounds a midpoint to the even neighbor, so 1.5 becomes 2 and 2.5 also becomes 2.
		Assert.AreEqual(2, ArgbColor.Blend(ArgbColor.FromRgb(1, 0, 0), ArgbColor.FromRgb(2, 0, 0), 0.5).R);
		Assert.AreEqual(2, ArgbColor.Blend(ArgbColor.FromRgb(2, 0, 0), ArgbColor.FromRgb(3, 0, 0), 0.5).R);
	}

	[TestMethod]
	[DataRow(-0.001, DisplayName = "BelowZero")]
	[DataRow(1.001, DisplayName = "AboveOne")]
	[DataRow(double.NaN, DisplayName = "NaN")]
	[DataRow(double.PositiveInfinity, DisplayName = "PositiveInfinity")]
	[DataRow(double.NegativeInfinity, DisplayName = "NegativeInfinity")]
	public void Blend_RatioOutsideZeroToOne_Throws(double ratio)
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			ArgbColor.Blend(ArgbColor.Black, ArgbColor.White, ratio)
		);

		Assert.AreEqual("ratio", exception.ParamName);
	}

	[TestMethod]
	[DataRow("#F80", 255, 255, 136, 0, DisplayName = "ShortRgb")]
	[DataRow("#8F80", 136, 255, 136, 0, DisplayName = "ShortArgb")]
	[DataRow("#FF8800", 255, 255, 136, 0, DisplayName = "Rgb")]
	[DataRow("#80FF8800", 128, 255, 136, 0, DisplayName = "Argb")]
	[DataRow("#ff8800", 255, 255, 136, 0, DisplayName = "LowerCase")]
	[DataRow("  #80ff8800\t", 128, 255, 136, 0, DisplayName = "SurroundingWhiteSpace")]
	public void TryParse_HexadecimalForms_ProduceTheColor(string text, int a, int r, int g, int b)
	{
		Assert.IsTrue(ArgbColor.TryParse(text, out ArgbColor color));
		Assert.AreEqual(new ArgbColor(A: (byte)a, R: (byte)r, G: (byte)g, B: (byte)b), color);
	}

	[TestMethod]
	[DataRow(null, DisplayName = "Null")]
	[DataRow("", DisplayName = "Empty")]
	[DataRow("   ", DisplayName = "Blank")]
	[DataRow("FF8800", DisplayName = "MissingHash")]
	[DataRow("#", DisplayName = "HashOnly")]
	[DataRow("#F8", DisplayName = "TwoDigits")]
	[DataRow("#FF880", DisplayName = "FiveDigits")]
	[DataRow("#FF88000", DisplayName = "SevenDigits")]
	[DataRow("#FF8800000", DisplayName = "NineDigits")]
	[DataRow("#GG8800", DisplayName = "NonHexadecimalDigit")]
	[DataRow("# F8800", DisplayName = "InnerWhiteSpace")]
	[DataRow("Red", DisplayName = "ColorName")]
	[DataRow("rgb(255, 136, 0)", DisplayName = "Function")]
	[DataRow("sc#1,0,0", DisplayName = "ScRgb")]
	public void TryParse_OtherText_ReportsFalseAndTheDefaultColor(string? text)
	{
		Assert.IsFalse(ArgbColor.TryParse(text, out ArgbColor color));
		Assert.AreEqual(default, color);
	}
}
