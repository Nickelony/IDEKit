using Nickelony.IDEKit.Core.Media;
#if AVALONIAEDIT
using Avalonia.Media;
using MarkdownBrush = Avalonia.Media.ISolidColorBrush;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;

#else
using System.Windows.Media;
using MarkdownBrush = System.Windows.Media.SolidColorBrush;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

#endif

/// <summary>
/// Pins the derived-brush math of <see cref="MarkdownThemeResources"/>: the code background and border are blended from the theme's surface,
/// and the muted foreground from its body foreground, so each must stay distinct from the input it is derived from and keep its alpha.
/// </summary>
[TestClass]
public sealed class MarkdownThemeResourcesTests
{
	private static readonly MarkdownRenderTheme s_light = MarkdownRenderTheme.Default;

	private static readonly MarkdownRenderTheme s_dark = MarkdownRenderTheme.Default with
	{
		SurfaceBackground = ArgbColor.Black,
		Foreground = ArgbColor.White,
	};

	[TestMethod]
	public void DerivedBrushes_LightTheme_DifferFromSurfaceAndForegroundAndStayOpaque()
	{
		Color surface = GetColor(MarkdownThemeResources.Resolve(s_light).SurfaceBackground);
		Color foreground = GetColor(MarkdownThemeResources.Resolve(s_light).Foreground);

		AssertDerived(MarkdownThemeResources.Resolve(s_light).CodeBackground, surface, "code background");
		AssertDerived(MarkdownThemeResources.Resolve(s_light).Border, surface, "border");
		AssertDerived(MarkdownThemeResources.Resolve(s_light).MutedForeground, foreground, "muted foreground");
	}

	[TestMethod]
	public void DerivedBrushes_DarkTheme_DifferFromSurfaceAndForegroundAndStayOpaque()
	{
		Color surface = GetColor(MarkdownThemeResources.Resolve(s_dark).SurfaceBackground);
		Color foreground = GetColor(MarkdownThemeResources.Resolve(s_dark).Foreground);

		AssertDerived(MarkdownThemeResources.Resolve(s_dark).CodeBackground, surface, "code background");
		AssertDerived(MarkdownThemeResources.Resolve(s_dark).Border, surface, "border");
		AssertDerived(MarkdownThemeResources.Resolve(s_dark).MutedForeground, foreground, "muted foreground");
	}

	[TestMethod]
	public void CodeBackground_BlendsTowardTheContrastingPole()
	{
		// The surface blend target is the pole contrasting with the surface's luma, so a light surface
		// darkens its code background while a dark surface lightens it.
		Color lightSurface = GetColor(MarkdownThemeResources.Resolve(s_light).SurfaceBackground);
		Color lightCode = GetColor(MarkdownThemeResources.Resolve(s_light).CodeBackground);
		Color darkSurface = GetColor(MarkdownThemeResources.Resolve(s_dark).SurfaceBackground);
		Color darkCode = GetColor(MarkdownThemeResources.Resolve(s_dark).CodeBackground);

		Assert.IsLessThan(lightSurface.R, lightCode.R);
		Assert.IsGreaterThan(darkSurface.R, darkCode.R);
	}

	[TestMethod]
	public void Resolve_SameThemeInstance_ReturnsTheSameResourcesAndCountsEachCall()
	{
		MarkdownRenderTheme theme = s_light with { BodyFontSize = 13.0 };

		var first = MarkdownThemeResources.Resolve(theme);
		var second = MarkdownThemeResources.Resolve(theme);

		Assert.AreSame(first, second);
		Assert.AreSame(theme, first.Theme);
		Assert.AreEqual(2, first.ResolveCount);
	}

	[TestMethod]
	public void Resolve_EqualThemeInstanceThatIsAnotherObject_ReturnsOtherResources()
	{
		MarkdownRenderTheme theme = s_light with { BodyFontSize = 13.0 };
		MarkdownRenderTheme copy = theme with { };

		Assert.AreEqual(theme, copy);
		Assert.AreNotSame(MarkdownThemeResources.Resolve(theme), MarkdownThemeResources.Resolve(copy));
	}

	private static void AssertDerived(MarkdownBrush brush, Color input, string name)
	{
		Color color = GetColor(brush);

		Assert.AreNotEqual(input, color, $"The {name} must not equal the theme input it is derived from.");
		Assert.AreEqual((byte)0xFF, color.A, $"The {name} must stay fully opaque.");
	}

	private static Color GetColor(MarkdownBrush brush) => brush.Color;
}
