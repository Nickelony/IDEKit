using System.Windows;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>Pins the validation and the defaults of <see cref="CompletionTooltipTheme"/>.</summary>
[TestClass]
public sealed class CompletionTooltipThemeTests
{
	[TestMethod]
	public void TooltipTheme_NonFiniteOffset_IsRejectedAtAssignment()
	{
		// The tooltip theme validates itself, so the failure site is the with-expression instead of the
		// controller constructor, and the exception names the offending property rather than the argument
		// the theme happened to travel in.
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			CompletionTooltipTheme.Default with
			{
				HorizontalOffset = double.PositiveInfinity,
			}
		);

		Assert.AreEqual("HorizontalOffset", exception.ParamName);
	}

	[TestMethod]
	public void TooltipTheme_NegativeThickness_IsRejectedAtAssignment()
	{
		ArgumentOutOfRangeException borderException = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			CompletionTooltipTheme.Default with
			{
				BorderThickness = new Thickness(-1.0),
			}
		);

		Assert.AreEqual("BorderThickness", borderException.ParamName);

		ArgumentOutOfRangeException paddingException = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			CompletionTooltipTheme.Default with
			{
				Padding = new Thickness(double.NaN),
			}
		);

		Assert.AreEqual("Padding", paddingException.ParamName);
	}

	[TestMethod]
	public void TooltipTheme_Default_PinsTheHorizontalOffset() => Assert.AreEqual(10.0, CompletionTooltipTheme.Default.HorizontalOffset);
}
