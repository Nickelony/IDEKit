using Nickelony.IDEKit.Infrastructure;
using System.Runtime.CompilerServices;
#if AVALONIAEDIT
using Avalonia.Media;
using MarkdownBrush = Avalonia.Media.ISolidColorBrush;
#else
using System.Windows.Media;
using MarkdownBrush = System.Windows.Media.SolidColorBrush;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;
#else
namespace Nickelony.IDEKit.AvalonEdit.Markdown;
#endif

/// <summary>
/// Provides the theme-derived brushes shared across the Markdown rendering pipeline.
/// </summary>
/// <remarks>
/// Derived brushes depend only on the theme's surface color, foreground color, and blend ratios, so they are
/// computed once per theme and cached, instead of being blended again once per quote, table cell, thematic
/// break, and inline-code container. The surface and foreground brushes are mutable - a host can change the
/// color of the theme's <see cref="MarkdownRenderTheme.SurfaceBackground"/> or
/// <see cref="MarkdownRenderTheme.Foreground"/> brush after the first render - so a cached entry is only
/// reused while both colors it was derived from are unchanged.
/// </remarks>
internal static class MarkdownDerivedBrushCache
{
	private static readonly ConditionalWeakTable<MarkdownRenderTheme, DerivedBrushes> s_derivedBrushes = new();

	/// <summary>
	/// Gets the theme-derived background brush for code blocks and inline code.
	/// </summary>
	/// <param name="theme">The theme to derive the brush from.</param>
	/// <returns>A shareable brush blended toward the contrasting pole of the theme's surface.</returns>
	internal static MarkdownBrush GetCodeBackground(MarkdownRenderTheme theme)
		=> GetDerivedBrushes(theme).CodeBackground;

	/// <summary>
	/// Gets the theme-derived border brush for quote bars, tables, thematic breaks, code blocks, and
	/// inline code.
	/// </summary>
	/// <param name="theme">The theme to derive the brush from.</param>
	/// <returns>A shareable brush blended toward the contrasting pole of the theme's surface.</returns>
	internal static MarkdownBrush GetBorderBrush(MarkdownRenderTheme theme)
		=> GetDerivedBrushes(theme).Border;

	/// <summary>
	/// Gets the theme-derived muted foreground brush for secondary text, such as an image placeholder.
	/// </summary>
	/// <param name="theme">The theme to derive the brush from.</param>
	/// <returns>A shareable brush quieter than the theme's body foreground but still legible.</returns>
	internal static MarkdownBrush GetMutedForeground(MarkdownRenderTheme theme)
		=> GetDerivedBrushes(theme).MutedForeground;

	private static DerivedBrushes GetDerivedBrushes(MarkdownRenderTheme theme)
	{
		Color baseColor = theme.SurfaceBackground.Color;
		Color foregroundColor = theme.Foreground.Color;

		// The blend ratios that also feed the derivation are init-only, so they cannot change on a cached instance;
		// only the surface and foreground colors can, and a mismatch on either replaces the stale entry.
		if (s_derivedBrushes.TryGetValue(theme, out DerivedBrushes? cached)
			&& cached.SourceColor == baseColor
			&& cached.SourceForeground == foregroundColor)
		{
			return cached;
		}

		DerivedBrushes brushes = CreateDerivedBrushes(theme, baseColor, foregroundColor);

		s_derivedBrushes.AddOrUpdate(theme, brushes);

		return brushes;
	}

	private static DerivedBrushes CreateDerivedBrushes(MarkdownRenderTheme theme, Color baseColor, Color foregroundColor)
	{
		// Blend toward the contrasting pole so derived colors stay visible on light and dark surfaces
		// alike; blending toward black alone would make the code background invisible on black.
		Color targetColor = BrushHelpers.GetLuma(baseColor) >= 0.5 ? Colors.Black : Colors.White;

		return new DerivedBrushes(
			baseColor,
			foregroundColor,
			BrushHelpers.CreateFrozenBrush(BrushHelpers.Blend(baseColor, targetColor, theme.BorderBlendRatio)),
			BrushHelpers.CreateFrozenBrush(BrushHelpers.Blend(baseColor, targetColor, theme.CodeBackgroundBlendRatio)),
			// The muted foreground is the body foreground blended partway toward the surface, so it stays
			// legible but quieter than body text.
			BrushHelpers.CreateFrozenBrush(BrushHelpers.Blend(foregroundColor, baseColor, MutedForegroundBlendRatio)));
	}

	// How far the muted foreground is blended from the body foreground toward the surface; a low value keeps
	// it close to the body text, a high value makes it faint. About half stays legible on light and dark
	// surfaces while reading as secondary text.
	private const double MutedForegroundBlendRatio = 0.45;

	private sealed class DerivedBrushes
	{
		public DerivedBrushes(
			Color sourceColor,
			Color sourceForeground,
			MarkdownBrush border,
			MarkdownBrush codeBackground,
			MarkdownBrush mutedForeground)
		{
			SourceColor = sourceColor;
			SourceForeground = sourceForeground;
			Border = border;
			CodeBackground = codeBackground;
			MutedForeground = mutedForeground;
		}

		/// <summary>Gets the surface color the brushes were derived from, so a stale entry is detectable.</summary>
		public Color SourceColor { get; }

		/// <summary>Gets the foreground color the muted brush was derived from, so a stale entry is detectable.</summary>
		public Color SourceForeground { get; }

		public MarkdownBrush Border { get; }
		public MarkdownBrush CodeBackground { get; }
		public MarkdownBrush MutedForeground { get; }
	}
}
