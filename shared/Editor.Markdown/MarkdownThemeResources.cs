using System.Runtime.CompilerServices;
using Nickelony.IDEKit.Core.Media;
using Nickelony.IDEKit.Markdown;
#if AVALONIAEDIT
using Avalonia.Media;
using Nickelony.IDEKit.AvaloniaEdit.Media;
using MarkdownBrush = Avalonia.Media.ISolidColorBrush;
#else
using Nickelony.IDEKit.AvalonEdit.Media;
using System.Windows;
using System.Windows.Media;
using MarkdownBrush = System.Windows.Media.SolidColorBrush;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;

#else

namespace Nickelony.IDEKit.AvalonEdit.Markdown;

#endif

/// <summary>The engine resources (brushes and fonts) a Markdown theme resolves to, created once per theme instance.</summary>
/// <remarks>
/// <para>
/// A <see cref="MarkdownRenderTheme"/> carries neutral descriptors (colors and font family sources). This type turns
/// them into the toolkit's own brush and font types once per theme, so the emitters do not rebuild the same brushes and families for every element.
/// The derived brushes (code background and border) depend only on the theme's surface color and blend ratios. They are computed once per theme
/// with the neutral blend helpers, instead of being blended again for each quote, table cell, thematic break, and inline-code container.
/// </para>
/// <para>
/// <see cref="Resolve"/> looks the instance of a theme up in a weak table, so a render resolves its theme once, at its entry point,
/// and hands the instance down to every emitter. The resolved fonts are created on the first thread that resolves a theme
/// and are cached with it, so resolve themes from the thread that renders with the resources.
/// </para>
/// </remarks>
internal sealed class MarkdownThemeResources
{
	private static readonly ConditionalWeakTable<MarkdownRenderTheme, MarkdownThemeResources> s_resources = [];

#if AVALONIAEDIT
	private static readonly FontFamily s_defaultBodyFontFamily = FontFamily.Default;
#else
	private static readonly FontFamily s_defaultBodyFontFamily = SystemFonts.MessageFontFamily;
#endif

	// How far the muted foreground is blended from the body foreground toward the surface; a low value keeps
	// it close to the body text, a high value makes it faint. A ratio near one half stays legible on light and
	// dark surfaces while reading as secondary text.
	private const double MutedForegroundBlendRatio = 0.45;

	private int _resolveCount;

	private MarkdownThemeResources(MarkdownRenderTheme theme)
	{
		ArgbColor surface = theme.SurfaceBackground;
		ArgbColor foreground = theme.Foreground;

		// Blend toward the contrasting pole so derived colors stay visible on light and dark surfaces
		// alike; blending toward black alone would make the code background invisible on black.
		ArgbColor target = surface.GetLuma() >= 0.5 ? ArgbColor.Black : ArgbColor.White;

		Theme = theme;
		SurfaceBackground = surface.ToBrush();
		Foreground = foreground.ToBrush();
		LinkForeground = theme.LinkForeground.ToBrush();
		Border = ArgbColor.Blend(surface, target, theme.BorderBlendRatio).ToBrush();
		CodeBackground = ArgbColor.Blend(surface, target, theme.CodeBackgroundBlendRatio).ToBrush();

		// The muted foreground is the body foreground blended partway toward the surface, so it stays
		// legible but quieter than body text.
		MutedForeground = ArgbColor.Blend(foreground, surface, MutedForegroundBlendRatio).ToBrush();

		BodyFontFamily = string.IsNullOrWhiteSpace(theme.BodyFontFamily) ? s_defaultBodyFontFamily : new FontFamily(theme.BodyFontFamily);

		CodeFontFamily = string.IsNullOrWhiteSpace(theme.CodeFontFamily)
			? new FontFamily(MarkdownRenderTheme.DefaultCodeFontFamily)
			: new FontFamily(theme.CodeFontFamily);
	}

	internal MarkdownRenderTheme Theme { get; }

	internal MarkdownBrush SurfaceBackground { get; }

	internal MarkdownBrush Foreground { get; }

	internal MarkdownBrush LinkForeground { get; }

	/// <summary>
	/// The theme-derived border brush for quote bars, tables, thematic breaks, code blocks, and inline code, blended toward the contrasting pole
	/// of the surface.
	/// </summary>
	internal MarkdownBrush Border { get; }

	/// <summary>The theme-derived background brush for code blocks and inline code, blended toward the contrasting pole of the surface.</summary>
	internal MarkdownBrush CodeBackground { get; }

	/// <summary>
	/// The theme-derived brush for secondary text, such as an image placeholder: quieter than the body foreground but still legible.
	/// </summary>
	internal MarkdownBrush MutedForeground { get; }

	/// <summary>The body font family, or the binding's default UI font when the theme names none.</summary>
	internal FontFamily BodyFontFamily { get; }

	internal FontFamily CodeFontFamily { get; }

	/// <summary>The number of times <see cref="Resolve"/> returned this instance.</summary>
	internal int ResolveCount => Volatile.Read(ref _resolveCount);

	/// <summary>Returns the resources of a theme instance, creating them on the first call for that instance.</summary>
	/// <param name="theme">The theme to resolve.</param>
	/// <returns>The resources shared by every render that uses this theme instance.</returns>
	/// <remarks>
	/// The table is thread-safe, so two threads resolving the same new theme cannot race a lookup and an add into a duplicate-key failure.
	/// </remarks>
	internal static MarkdownThemeResources Resolve(MarkdownRenderTheme theme)
	{
		MarkdownThemeResources resources = s_resources.GetValue(theme, static key => new MarkdownThemeResources(key));

		Interlocked.Increment(ref resources._resolveCount);

		return resources;
	}
}
