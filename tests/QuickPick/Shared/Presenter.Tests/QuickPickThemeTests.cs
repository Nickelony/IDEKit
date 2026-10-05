#if AVALONIAQUICKPICK
using Avalonia;

namespace Nickelony.QuickPick.Avalonia.Tests;

#else
using System.Windows;

namespace Nickelony.QuickPick.Wpf.Tests;

#endif

/// <summary>
/// Covers the measures of the theme of the quick-pick window: each one refuses a value that is not finite, or is negative where a measure cannot be.
/// </summary>
[STATestClass]
public sealed class QuickPickThemeTests
{
	[TestMethod]
	public void Padding_ASideThatIsNegativeOrNotFinite_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			_ = new QuickPickTheme { Padding = new Thickness(left: -1.0, top: 0.0, right: 0.0, bottom: 0.0) }
		);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			_ = new QuickPickTheme { Padding = new Thickness(left: 0.0, top: double.NaN, right: 0.0, bottom: 0.0) }
		);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			_ = new QuickPickTheme { Padding = new Thickness(left: 0.0, top: 0.0, right: double.PositiveInfinity, bottom: 0.0) }
		);
	}

	[TestMethod]
	public void Padding_ZeroOnEverySide_IsValid()
	{
		var theme = new QuickPickTheme { Padding = new Thickness(0.0) };
		Assert.AreEqual(0.0, theme.Padding.Left);
	}

	[TestMethod]
	public void Measures_InfinityAndNaN_Throw()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new QuickPickTheme { Width = double.PositiveInfinity });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new QuickPickTheme { TopOffset = double.NaN });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new QuickPickTheme { CornerRadius = double.PositiveInfinity });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new QuickPickTheme { BorderThickness = double.NaN });
	}

	[TestMethod]
	public void Measures_ZeroWhereTheMeasureMayBeZero_IsValidButAWidthAndARowHeightNeedMore()
	{
		var theme = new QuickPickTheme
		{
			TopOffset = 0.0,
			CornerRadius = 0.0,
			BorderThickness = 0.0,
		};

		Assert.AreEqual(0.0, theme.TopOffset);
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new QuickPickTheme { RowHeight = 0.0 });
	}
}
