using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Pins the documented default and the assignment-time validation of the code-action menu options.</summary>
[TestClass]
public sealed class CodeActionMenuOptionsTests
{
	[TestMethod]
	public void Default_PinsTheThreeHundredTwentyUnitMenuCap() => Assert.AreEqual(320.0, CodeActionMenuOptions.Default.MaxHeight);

	[TestMethod]
	public void MaxHeight_Negative_Throws() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CodeActionMenuOptions.Default with { MaxHeight = -1.0 });

	[TestMethod]
	public void MaxHeight_NonFinite_Throws() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CodeActionMenuOptions.Default with { MaxHeight = double.NaN });

	[DataRow(-1.0)]
	[DataRow(double.NaN)]
	[TestMethod]
	public void AnchorXOffset_Invalid_Throws(double value) =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CodeActionMenuOptions.Default with { AnchorXOffset = value });

	[DataRow(-1.0)]
	[DataRow(double.NaN)]
	[TestMethod]
	public void CaretAnchorYOffset_Invalid_Throws(double value) =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CodeActionMenuOptions.Default with { CaretAnchorYOffset = value });

	[DataRow(-1.0)]
	[DataRow(double.NaN)]
	[TestMethod]
	public void MarginAnchorYOffset_Invalid_Throws(double value) =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CodeActionMenuOptions.Default with { MarginAnchorYOffset = value });

	[TestMethod]
	public void MaxHeight_Zero_IsAccepted() => Assert.AreEqual(0.0, (CodeActionMenuOptions.Default with { MaxHeight = 0.0 }).MaxHeight);
}
