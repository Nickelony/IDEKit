using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Pins the documented defaults and the assignment-time validation of the completion window options.</summary>
[TestClass]
public sealed class CompletionWindowOptionsTests
{
	[TestMethod]
	public void Default_PinsDocumentedValues()
	{
		CompletionWindowOptions options = CompletionWindowOptions.Default;

		// The documented defaults are pinned, so a changed option default fails here.
		Assert.AreEqual(420.0, options.MinContentWidth);
		Assert.AreEqual(920.0, options.MaxWidth);
		Assert.AreEqual(300.0, options.MaxHeight);
		Assert.AreEqual(52.0, options.HorizontalChrome);
		Assert.AreEqual(24.0, options.ItemIconWidth);
		Assert.AreEqual(12.0, options.ItemDetailSpacing);
		Assert.IsTrue(options.PreventActivation);
		Assert.IsTrue(options.DismissWhenEmpty);
	}

	[TestMethod]
	[DataRow("MaxHeight", -1.0)]
	[DataRow("MinContentWidth", -1.0)]
	[DataRow("MaxWidth", -1.0)]
	[DataRow("HorizontalChrome", -0.5)]
	[DataRow("ItemIconWidth", -0.5)]
	[DataRow("ItemDetailSpacing", -0.25)]
	public void NegativeValue_IsRejectedAtAssignment(string propertyName, double value)
	{
		// The options validate themselves, so the offending assignment fails before a controller exists.
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AssignNumericOption(propertyName, value));
		Assert.AreEqual(propertyName, exception.ParamName);
	}

	[TestMethod]
	public void NonFiniteValue_IsRejectedAtAssignment()
	{
		ArgumentOutOfRangeException minWidthException = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			CompletionWindowOptions.Default with
			{
				MinContentWidth = double.NaN,
			}
		);

		Assert.AreEqual("MinContentWidth", minWidthException.ParamName);

		ArgumentOutOfRangeException maxHeightException = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			CompletionWindowOptions.Default with
			{
				MaxHeight = double.PositiveInfinity,
			}
		);

		Assert.AreEqual("MaxHeight", maxHeightException.ParamName);

		ArgumentOutOfRangeException chromeException = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			CompletionWindowOptions.Default with
			{
				HorizontalChrome = double.NaN,
			}
		);

		Assert.AreEqual("HorizontalChrome", chromeException.ParamName);
	}

	[TestMethod]
	public void MaxContentWidth_IsTheMaximumWidthMinusTheChrome()
	{
		CompletionWindowOptions options = CompletionWindowOptions.Default with { MaxWidth = 600, HorizontalChrome = 50 };
		Assert.AreEqual(550.0, options.MaxContentWidth);
	}

	[TestMethod]
	public void GetWindowWidth_AddsTheChromeAndCapsAtTheMaximumWidth()
	{
		CompletionWindowOptions options = CompletionWindowOptions.Default with { MaxWidth = 600, HorizontalChrome = 50 };

		Assert.AreEqual(350.0, options.GetWindowWidth(300));
		Assert.AreEqual(600.0, options.GetWindowWidth(550));
		Assert.AreEqual(600.0, options.GetWindowWidth(5000));
	}

	[TestMethod]
	public void Validate_DefaultOptions_Passes() => CompletionWindowOptions.Default.Validate("options");

	[TestMethod]
	public void Validate_MaximumWidthBelowTheContentFloorPlusTheChrome_ThrowsTheNamedParameter()
	{
		// 920 exceeds the 900 floor but cannot hold it with the default chrome, so the relation, not the raw order, decides.
		CompletionWindowOptions options = CompletionWindowOptions.Default with
		{
			MinContentWidth = 900,
			MaxWidth = 920,
		};

		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => options.Validate("presentation"));

		Assert.AreEqual("presentation", exception.ParamName);
	}

	[TestMethod]
	public void Validate_MaximumWidthEqualToTheFloorPlusTheChrome_Passes()
	{
		CompletionWindowOptions options = CompletionWindowOptions.Default with { MinContentWidth = 400, MaxWidth = 450, HorizontalChrome = 50 };
		options.Validate("options");
	}

	private static CompletionWindowOptions AssignNumericOption(string propertyName, double value)
	{
		return propertyName switch
		{
			"MinContentWidth" => CompletionWindowOptions.Default with { MinContentWidth = value },
			"MaxWidth" => CompletionWindowOptions.Default with { MaxWidth = value },
			"MaxHeight" => CompletionWindowOptions.Default with { MaxHeight = value },
			"HorizontalChrome" => CompletionWindowOptions.Default with { HorizontalChrome = value },
			"ItemIconWidth" => CompletionWindowOptions.Default with { ItemIconWidth = value },
			"ItemDetailSpacing" => CompletionWindowOptions.Default with { ItemDetailSpacing = value },
			_ => throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, "No numeric option has this name."),
		};
	}
}
