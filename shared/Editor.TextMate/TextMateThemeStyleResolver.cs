using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Media;
using Nickelony.IDEKit.TextMate;
#if AVALONIAEDIT
using Avalonia.Media;
using Nickelony.IDEKit.AvaloniaEdit.Media;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using Brush = Avalonia.Media.IBrush;
#else
using Nickelony.IDEKit.AvalonEdit.Media;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.TextMate;

#else

namespace Nickelony.IDEKit.AvalonEdit.TextMate;

#endif

/// <summary>Resolves TextMate token scopes into <see cref="TextRunStyle"/> instances using a <see cref="TextMateTokenTheme"/>.</summary>
/// <remarks>
/// <para>
/// The selector matching, the rule ranking, and the theme value conventions are those of <see cref="TextMateScopeResolver"/>, which this type wraps.
/// This type converts the neutral result to the toolkit's brush and text decorations.
/// </para>
/// <para>
/// Styles that resolve to the same color and traits share one brush and one decoration collection, and one resolver can be shared by several editors
/// and threads.
/// </para>
/// </remarks>
internal sealed class TextMateThemeStyleResolver
{
	private static readonly TextDecorationCollection s_underlineDecorations = CloneTextDecorations(TextDecorations.Underline);
	private static readonly TextDecorationCollection s_strikethroughDecorations = CloneTextDecorations(TextDecorations.Strikethrough);
	private static readonly TextDecorationCollection s_underlineAndStrikethroughDecorations = CreateCombinedDecorations();

	private readonly TextMateScopeResolver _scopeResolver;

	// The distinct neutral styles of a theme are few (its colors times its trait combinations), so the table needs no bound.
	private readonly ConcurrentDictionary<TextMateTokenStyle, TextRunStyle> _runStyles = new();

	/// <param name="theme">The token theme whose rules drive style resolution.</param>
	/// <param name="logger">
	/// An optional logger that reports invalid foreground colors, unsupported selectors, unrecognized font style traits, misplaced child combinators,
	/// and <see langword="null"/> theme rules.
	/// </param>
	internal TextMateThemeStyleResolver(TextMateTokenTheme theme, ILogger? logger = null) =>
		_scopeResolver = new TextMateScopeResolver(theme, logger);

	/// <summary>Resolves the visual style for a token's scope sequence.</summary>
	/// <param name="scopes">The token scopes to resolve, ordered from the root scope to the innermost scope.</param>
	/// <returns>
	/// The resolved style, or the shared <see cref="TextRunStyle.Empty"/> value when <paramref name="scopes"/> is <see langword="null"/> or empty.
	/// </returns>
	/// <exception cref="ArgumentException">The scope list contains a <see langword="null"/> entry.</exception>
	/// <remarks>
	/// The method is safe to call from any thread and returns an immutable style that is shared by all tokens whose scope sequence resolves to it.
	/// </remarks>
	internal TextRunStyle Resolve(IReadOnlyList<string>? scopes)
	{
		TextMateTokenStyle style = _scopeResolver.Resolve(scopes);
		return style.HasFormatting ? _runStyles.GetOrAdd(style, ConvertStyle) : TextRunStyle.Empty;
	}

	private static TextRunStyle ConvertStyle(TextMateTokenStyle style)
	{
		Brush? foreground = style.Foreground is { } color ? color.ToBrush() : null;

		return new TextRunStyle(
			foreground,
			IsBold: style.IsBold,
			IsItalic: style.IsItalic,
			TextDecorations: CreateTextDecorations(style.IsUnderline, style.IsStrikethrough)
		);
	}

	private static TextDecorationCollection? CreateTextDecorations(bool isUnderline, bool isStrikethrough)
	{
		return (isUnderline, isStrikethrough) switch
		{
			(true, true) => s_underlineAndStrikethroughDecorations,
			(true, false) => s_underlineDecorations,
			(false, true) => s_strikethroughDecorations,
			_ => null,
		};
	}

	private static TextDecorationCollection CreateCombinedDecorations()
	{
		var decorations = new TextDecorationCollection();

		foreach (TextDecoration decoration in s_underlineDecorations)
			decorations.Add(decoration);

		foreach (TextDecoration decoration in s_strikethroughDecorations)
			decorations.Add(decoration);

#if !AVALONIAEDIT
		decorations.Freeze();
#endif
		return decorations;
	}

	private static TextDecorationCollection CloneTextDecorations(TextDecorationCollection source)
	{
#if AVALONIAEDIT
		// The decoration collection is a plain list with no freeze or clone; copy it so the shared
		// value is owned by this type instead of aliasing the toolkit's static instance.
		var clone = new TextDecorationCollection(source);
#else
		TextDecorationCollection clone = source.Clone();
		clone.Freeze();
#endif
		return clone;
	}
}
