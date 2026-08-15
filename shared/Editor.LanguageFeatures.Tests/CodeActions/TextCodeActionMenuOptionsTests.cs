#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

/// <summary>
/// Pins the documented default and validation of the host-side code-action menu options (the sizing
/// policy that intentionally lives with the editor binding).
/// </summary>
[STATestClass]
public sealed class TextCodeActionMenuOptionsTests
{
	[TestMethod]
	public void Default_PinsTheThreeHundredTwentyUnitMenuCap()
		=> Assert.AreEqual(320.0, TextCodeActionMenuOptions.Default.MaxHeight);

	[TestMethod]
	public void MaxHeight_Negative_Throws()
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextCodeActionMenuOptions.Default with { MaxHeight = -1.0 });

	[TestMethod]
	public void MaxHeight_NonFinite_Throws()
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextCodeActionMenuOptions.Default with { MaxHeight = double.NaN });

	[DataRow(-1.0)]
	[DataRow(double.NaN)]
	[TestMethod]
	public void AnchorXOffset_Invalid_Throws(double value)
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextCodeActionMenuOptions.Default with { AnchorXOffset = value });

	[DataRow(-1.0)]
	[DataRow(double.NaN)]
	[TestMethod]
	public void CaretAnchorYOffset_Invalid_Throws(double value)
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextCodeActionMenuOptions.Default with { CaretAnchorYOffset = value });

	[DataRow(-1.0)]
	[DataRow(double.NaN)]
	[TestMethod]
	public void MarginAnchorYOffset_Invalid_Throws(double value)
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextCodeActionMenuOptions.Default with { MarginAnchorYOffset = value });

	[TestMethod]
	public void MaxHeight_Zero_IsAccepted()
		=> Assert.AreEqual(0.0, (TextCodeActionMenuOptions.Default with { MaxHeight = 0.0 }).MaxHeight);
}
